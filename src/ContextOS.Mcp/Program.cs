using System.Security.Cryptography;
using System.Text;
using ContextOS.Core;
using ContextOS.Embeddings;
using ContextOS.Mcp;
using ContextOS.Mcp.Tools;
using ContextOS.Retrieval;
using ContextOS.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

// Detect workspace root: walk up from cwd looking for .git.
string cwd = Directory.GetCurrentDirectory();
string workspaceRoot = FindGitRoot(cwd) ?? cwd;
string workspaceId = ComputeWorkspaceId(workspaceRoot);
string workspaceName = Path.GetFileName(workspaceRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))
                       ?? workspaceId;

string dbDir = Path.Combine(
    Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
    ".contextos");
string dbPath = Path.Combine(dbDir, $"{workspaceId}.db");

// Embeddings: try ONNX, fall back silently to null provider.
IEmbeddingsProvider embeddings;
try
{
    embeddings = EmbeddingsFactory.Create();
}
catch
{
    embeddings = new NullEmbeddingsProvider();
}

SqliteStore store = await SqliteStore.OpenAsync(dbPath, embeddings);

var workspace = new Workspace(workspaceId, workspaceRoot, workspaceName, null,
    DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
await store.UpsertWorkspaceAsync(workspace);

var search = new HybridSearch(store.Connection, embeddings);
var workspaceCtx = new WorkspaceContext(workspaceId, workspaceRoot);

HostApplicationBuilder builder = Host.CreateApplicationBuilder(args);

// All logging goes to stderr so we don't corrupt the JSON-RPC stdio stream.
builder.Logging.ClearProviders();
builder.Logging.AddConsole(opts => opts.LogToStandardErrorThreshold = LogLevel.Trace);

builder.Services.AddSingleton<WorkspaceContext>(workspaceCtx);
builder.Services.AddSingleton<IMemoryStore>(store);
builder.Services.AddSingleton<ISearch>(search);

builder.Services.AddMcpServer()
    .WithStdioServerTransport()
    .WithTools<RememberTool>()
    .WithTools<RecallTool>();

await builder.Build().RunAsync();

// -------------------------------------------------------------------------
// Helpers
// -------------------------------------------------------------------------

static string? FindGitRoot(string start)
{
    string? dir = start;
    while (dir is not null)
    {
        if (Directory.Exists(Path.Combine(dir, ".git")))
            return dir;
        dir = Path.GetDirectoryName(dir);
    }
    return null;
}

static string ComputeWorkspaceId(string path) =>
    Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes(path)))[..16].ToLowerInvariant();
