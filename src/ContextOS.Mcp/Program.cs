using System.Security.Cryptography;
using System.Text;
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

// --selftest: validate the embeddings provider and exit. Used by CI smoke tests.
if (args.Contains("--selftest"))
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

// Detect workspace root via LibGit2Sharp repository discovery, falling back to cwd.
string cwd = Directory.GetCurrentDirectory();
string workspaceRoot = LibGit2SharpProbe.DiscoverRoot(cwd) ?? cwd;
string workspaceId = ComputeWorkspaceId(workspaceRoot);
string workspaceName = Path.GetFileName(workspaceRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))
                       ?? workspaceId;

string dbPath = Path.Combine(EmbeddingsFactory.GetContextosHome(), $"{workspaceId}.db");

// Load embeddings config first so the provider name is available for error messages.
EmbeddingsConfig embeddingsCfg = EmbeddingsFactory.LoadConfig();
string providerName = embeddingsCfg.Provider.ToLowerInvariant() switch
{
    "ollama" => "ollama",
    "openai" => "openai",
    _ => "onnx"
};

// Create provider. Fails immediately for ONNX when model files are absent.
IEmbeddingsProvider embeddings;
try
{
    embeddings = EmbeddingsFactory.CreateFromConfig(embeddingsCfg);
}
catch (Exception ex)
{
    WriteEmbeddingError(providerName, ex.Message);
    return 1;
}

// Validate the provider actually produces embeddings before starting the server.
try
{
    using var valCts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
    await embeddings.EmbedAsync("contextos startup check", valCts.Token);
}
catch (Exception ex)
{
    if (embeddings is IDisposable d) d.Dispose();
    WriteEmbeddingError(providerName, ex.Message);
    return 1;
}

// Probe git once at startup. On failure (no repo / library error) gitInfo is null.
var gitProbe = new LibGit2SharpProbe(NullLogger<LibGit2SharpProbe>.Instance);
GitInfo? gitInfo = gitProbe.Probe(workspaceRoot);

SqliteStore store = await SqliteStore.OpenAsync(dbPath, embeddings);

var workspace = new Workspace(workspaceId, workspaceRoot, workspaceName, null,
    DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
await store.UpsertWorkspaceAsync(workspace);

var search = new HybridSearch(store.Connection, embeddings);
var workspaceCtx = new WorkspaceContext(workspaceId, workspaceRoot, gitInfo);

HostApplicationBuilder builder = Host.CreateApplicationBuilder(args);

// All logging goes to stderr so we don't corrupt the JSON-RPC stdio stream.
builder.Logging.ClearProviders();
builder.Logging.AddConsole(opts => opts.LogToStandardErrorThreshold = LogLevel.Trace);

builder.Services.AddSingleton<WorkspaceContext>(workspaceCtx);
builder.Services.AddSingleton<IMemoryStore>(store);
builder.Services.AddSingleton<ISearch>(search);
builder.Services.AddSingleton<IGitProbe, LibGit2SharpProbe>();

builder.Services.AddMcpServer()
    .WithStdioServerTransport()
    .WithTools<RememberTool>()
    .WithTools<RecallTool>()
    .WithTools<ContextTool>();

await builder.Build().RunAsync();
return 0;

// -------------------------------------------------------------------------
// Helpers
// -------------------------------------------------------------------------

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

        See docs/CONFIG.md (when written) or PROJECT.md section 8.
        """);
}

static string ComputeWorkspaceId(string path) =>
    Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes(path)))[..16].ToLowerInvariant();
