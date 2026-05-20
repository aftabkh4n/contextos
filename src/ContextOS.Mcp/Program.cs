using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ContextOS.Core;
using ContextOS.Embeddings;
using ContextOS.Git;
using ContextOS.Mcp;
using ContextOS.Mcp.Tools;
using ContextOS.Retrieval;
using ContextOS.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using Serilog;
using Serilog.Events;

// -------------------------------------------------------------------------
// Serilog: initialise as early as possible so all code below can log.
// File-only — never stdout/stderr (those are used by the MCP stdio transport).
// -------------------------------------------------------------------------

string contextosHome = EmbeddingsFactory.GetContextosHome();
LogEventLevel logLevel = LoadLogLevel(Path.Combine(contextosHome, "config.json"));
string logDir = Path.Combine(contextosHome, "logs");
Directory.CreateDirectory(logDir);

Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Is(logLevel)
    .WriteTo.File(
        path: Path.Combine(logDir, "contextos-.log"),
        rollingInterval: RollingInterval.Day,
        outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {Message:lj}{NewLine}{Exception}")
    .CreateLogger();

// -------------------------------------------------------------------------
// CLI dispatch
// -------------------------------------------------------------------------

CliCommand command = CliArgs.Parse(args);

if (command == CliCommand.Help)
{
    CliPrinter.PrintHelp();
    return 0;
}

if (command == CliCommand.Version)
{
    CliPrinter.PrintVersion();
    return 0;
}

if (command == CliCommand.Init)
{
    CliPrinter.PrintInit();
    return 0;
}

if (command == CliCommand.Selftest)
{
    EmbeddingsConfig selftestCfg = EmbeddingsFactory.LoadConfig();
    IEmbeddingsProvider selftestProvider = EmbeddingsFactory.CreateFromConfig(selftestCfg);
    try
    {
        await selftestProvider.EmbedAsync("selftest", CancellationToken.None);
        Console.WriteLine("OK");
        return 0;
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"Embeddings selftest failed: {ex}");
        return 1;
    }
    finally
    {
        if (selftestProvider is IDisposable sd) sd.Dispose();
    }
}

// -------------------------------------------------------------------------
// Serve: workspace detection, embeddings, git, hydration, host.
// -------------------------------------------------------------------------

Log.Information("ContextOS starting (version {Version})", CliPrinter.GetVersion());

string cwd = Directory.GetCurrentDirectory();
string workspaceRoot = LibGit2SharpProbe.DiscoverRoot(cwd) ?? cwd;
string workspaceId = ComputeWorkspaceId(workspaceRoot);
string workspaceName = Path.GetFileName(workspaceRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))
                       ?? workspaceId;

Log.Information("Workspace: {WorkspaceName} ({WorkspaceId}) at {WorkspaceRoot}",
    workspaceName, workspaceId, workspaceRoot);

string dbPath = Path.Combine(contextosHome, $"{workspaceId}.db");

EmbeddingsConfig embeddingsCfg = EmbeddingsFactory.LoadConfig();
string providerName = embeddingsCfg.Provider.ToLowerInvariant() switch
{
    "ollama" => "ollama",
    "openai" => "openai",
    _        => "onnx"
};

IEmbeddingsProvider embeddings;
try
{
    embeddings = EmbeddingsFactory.CreateFromConfig(embeddingsCfg);
}
catch (Exception ex)
{
    Log.Fatal(ex, "Failed to create embeddings provider ({Provider})", providerName);
    WriteEmbeddingError(providerName, ex.Message);
    Log.CloseAndFlush();
    return 1;
}

try
{
    using var valCts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
    await embeddings.EmbedAsync("contextos startup check", valCts.Token);
    Log.Information("Embeddings provider validated: {Provider}", providerName);
}
catch (Exception ex)
{
    if (embeddings is IDisposable d) d.Dispose();
    Log.Fatal(ex, "Embeddings provider not functional ({Provider})", providerName);
    WriteEmbeddingError(providerName, ex.Message);
    Log.CloseAndFlush();
    return 1;
}

var gitProbe = new LibGit2SharpProbe(NullLogger<LibGit2SharpProbe>.Instance);
GitInfo? gitInfo = gitProbe.Probe(workspaceRoot);
if (gitInfo is not null)
    Log.Information("Git: branch={Branch} commits={CommitCount}", gitInfo.Branch, gitInfo.RecentCommits.Count);

SqliteStore store = await SqliteStore.OpenAsync(dbPath, embeddings);

var workspace = new Workspace(workspaceId, workspaceRoot, workspaceName, null,
    DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
await store.UpsertWorkspaceAsync(workspace);

var search = new HybridSearch(store.Connection, embeddings);
var workspaceCtx = new WorkspaceContext(workspaceId, workspaceRoot, gitInfo);

string sessionId = UlidHelper.NewUlid();

string hydrationBlob = await HydrationBuilder.BuildAsync(store, workspaceId, workspaceName, gitInfo);
string contextHash = HydrationBuilder.ComputeHash(hydrationBlob);
await store.LogHydrationAsync(workspaceId, sessionId, contextHash);

Log.Information("Hydration complete: hash={Hash} bytes={Bytes}", contextHash[..8], hydrationBlob.Length);

string version = CliPrinter.GetVersion();
HostApplicationBuilder builder = Host.CreateApplicationBuilder(args);

builder.Logging.ClearProviders();
builder.Logging.AddSerilog(Log.Logger, dispose: false);  // Log.Logger disposed in finally below

builder.Services.AddSingleton<WorkspaceContext>(workspaceCtx);
builder.Services.AddSingleton<IMemoryStore>(store);
builder.Services.AddSingleton<ISearch>(search);
builder.Services.AddSingleton<IGitProbe, LibGit2SharpProbe>();

builder.Services.AddMcpServer(options =>
    {
        options.ServerInfo = new Implementation { Name = "ContextOS", Version = version };
        options.ServerInstructions = hydrationBlob;
    })
    .WithStdioServerTransport()
    .WithTools<RememberTool>()
    .WithTools<RecallTool>()
    .WithTools<ContextTool>();

Log.Information("MCP server ready (session {SessionId})", sessionId);

try
{
    await builder.Build().RunAsync();
    return 0;
}
catch (Exception ex)
{
    Log.Fatal(ex, "ContextOS terminated unexpectedly");
    return 1;
}
finally
{
    Log.CloseAndFlush();
}

// -------------------------------------------------------------------------
// Helpers
// -------------------------------------------------------------------------

static LogEventLevel LoadLogLevel(string configPath)
{
    if (!File.Exists(configPath)) return LogEventLevel.Information;
    try
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(configPath));
        if (doc.RootElement.TryGetProperty("logging", out JsonElement loggingEl) &&
            loggingEl.TryGetProperty("level", out JsonElement levelEl) &&
            Enum.TryParse<LogEventLevel>(levelEl.GetString(), ignoreCase: true, out LogEventLevel parsed))
        {
            return parsed;
        }
    }
    catch { /* malformed config — use default */ }
    return LogEventLevel.Information;
}

static void WriteEmbeddingError(string providerName, string detail)
{
    Console.Error.WriteLine(
        $"""
        ContextOS cannot start: no functional embeddings provider.

        The configured provider is: {providerName}
        Detail: {detail}

        To fix:
          - For the default ONNX provider: run `bash scripts/fetch-model.sh`
            in the ContextOS repo root to download the model.
          - For Ollama: ensure Ollama is running at the configured URL and
            the model is pulled. Default: http://localhost:11434
          - For OpenAI: set OPENAI_API_KEY in your environment or
            ~/.contextos/config.json.

        See docs/CONFIG.md or README.md for setup instructions.
        """);
}

static string ComputeWorkspaceId(string path) =>
    Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes(path)))[..16].ToLowerInvariant();
