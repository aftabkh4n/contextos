using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace ContextOS.Tests;

/// <summary>
/// End-to-end tests that spawn the MCP server as a subprocess and exercise it
/// via line-delimited JSON-RPC 2.0 over stdio.
///
/// Requires the Mcp project to be built first (dotnet test handles this via
/// the ReferenceOutputAssembly=false project reference in ContextOS.Tests.csproj).
/// </summary>
[Trait("Category", "Integration")]
public sealed class McpIntegrationTests : IAsyncLifetime
{
    private Process? _server;
    private StreamWriter? _stdin;
    private StreamReader? _stdout;
    private string? _tempDir;
    private string? _dbPath;
    private int _nextId = 1;

    // -------------------------------------------------------------------------
    // Lifecycle
    // -------------------------------------------------------------------------

    public async Task InitializeAsync()
    {
        string dll = McpTestHelpers.FindMcpDll();
        _tempDir = Path.Combine(Path.GetTempPath(), $"contextos-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);

        string workspaceId = ComputeWorkspaceId(_tempDir);
        _dbPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".contextos", $"{workspaceId}.db");

        var psi = new ProcessStartInfo("dotnet", $"exec \"{dll}\"")
        {
            WorkingDirectory = _tempDir,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = false, // server logs go to test runner's stderr
            UseShellExecute = false,
        };

        _server = Process.Start(psi)!;
        _stdin = _server.StandardInput;
        _stdout = _server.StandardOutput;

        // Give the generic host a moment to finish startup.
        await Task.Delay(1000);
    }

    public async Task DisposeAsync()
    {
        try { _server?.Kill(); } catch { /* already exited */ }
        try
        {
            if (_server is not null)
                await _server.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5));
        }
        catch { /* timeout — proceed with cleanup anyway */ }
        _server?.Dispose();

        // Brief yield so the OS has time to release file handles after process exit.
        await Task.Delay(200);

        if (_tempDir is not null && Directory.Exists(_tempDir))
            try { Directory.Delete(_tempDir, recursive: true); } catch { /* best-effort */ }

        if (_dbPath is not null && File.Exists(_dbPath))
            File.Delete(_dbPath);

        await Task.CompletedTask;
    }

    // -------------------------------------------------------------------------
    // Tests
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Initialize_Handshake_Succeeds()
    {
        using JsonDocument response = await SendRequestAsync("initialize", new
        {
            protocolVersion = "2024-11-05",
            capabilities = new { },
            clientInfo = new { name = "test", version = "1.0" }
        });

        Assert.True(response.RootElement.TryGetProperty("result", out _),
            $"Expected 'result' in initialize response. Got: {response.RootElement}");
    }

    [Fact]
    public async Task ToolsList_ContainsRememberAndRecall()
    {
        await DoInitHandshakeAsync();

        using JsonDocument response = await SendRequestAsync("tools/list", new { });

        JsonElement tools = response.RootElement.GetProperty("result").GetProperty("tools");
        var names = tools.EnumerateArray()
            .Select(t => t.GetProperty("name").GetString())
            .ToHashSet();

        Assert.Contains("remember", names);
        Assert.Contains("recall", names);
    }

    [Fact]
    public async Task Remember_ValidInput_ReturnsId()
    {
        await DoInitHandshakeAsync();

        using JsonDocument response = await CallToolAsync("remember", new
        {
            content = "the deployment pipeline runs on GitHub Actions",
            type = "note"
        });

        JsonElement result = response.RootElement.GetProperty("result");
        Assert.False(IsError(result), $"Expected isError=false. Response: {result}");

        string text = result.GetProperty("content")[0].GetProperty("text").GetString()!;
        using JsonDocument payload = JsonDocument.Parse(text);
        Assert.True(payload.RootElement.TryGetProperty("id", out _), "Expected 'id' field in response payload");
        Assert.True(payload.RootElement.TryGetProperty("message", out _), "Expected 'message' field in response payload");
    }

    [Fact]
    public async Task Recall_MatchesStoredMemory()
    {
        await DoInitHandshakeAsync();

        // Store a distinguishable memory.
        using JsonDocument remResp = await CallToolAsync("remember", new
        {
            content = "Redis is used for session caching with a 30 minute TTL",
            type = "note"
        });
        Assert.False(IsError(remResp.RootElement.GetProperty("result")),
            "remember call unexpectedly failed");

        // Recall it by keyword.
        using JsonDocument response = await CallToolAsync("recall", new { query = "Redis session cache", k = 3 });
        JsonElement result = response.RootElement.GetProperty("result");
        Assert.False(IsError(result), $"Expected isError=false on recall. Response: {result}");

        string text = result.GetProperty("content")[0].GetProperty("text").GetString()!;
        Assert.Contains("Redis", text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Remember_EmptyContent_ReturnsIsError()
    {
        await DoInitHandshakeAsync();

        using JsonDocument response = await CallToolAsync("remember", new { content = "", type = "note" });
        JsonElement result = response.RootElement.GetProperty("result");

        Assert.True(IsError(result),
            $"Expected isError=true for empty content. Response: {result}");
    }

    [Fact]
    public async Task Remember_InvalidType_ReturnsIsError()
    {
        await DoInitHandshakeAsync();

        using JsonDocument response = await CallToolAsync("remember", new { content = "some memory", type = "invalid_type" });
        JsonElement result = response.RootElement.GetProperty("result");

        Assert.True(IsError(result),
            $"Expected isError=true for invalid type. Response: {result}");
    }

    [Fact]
    public async Task ServerRemainsAlive_AfterToolError()
    {
        await DoInitHandshakeAsync();

        // Bad call.
        using JsonDocument errResp = await CallToolAsync("remember", new { content = "" });
        Assert.True(IsError(errResp.RootElement.GetProperty("result")),
            "Expected error for empty content");

        // Server should still respond to valid calls.
        using JsonDocument goodResp = await CallToolAsync("remember", new
        {
            content = "server recovered after error",
            type = "note"
        });
        Assert.False(IsError(goodResp.RootElement.GetProperty("result")),
            "Expected success after earlier error");
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private async Task<JsonDocument> SendRequestAsync(string method, object @params)
    {
        int id = _nextId++;
        var request = new { jsonrpc = "2.0", id, method, @params };
        string json = JsonSerializer.Serialize(request);
        await _stdin!.WriteLineAsync(json);
        await _stdin.FlushAsync();
        return await ReadResponseAsync(id);
    }

    private Task<JsonDocument> CallToolAsync(string toolName, object arguments) =>
        SendRequestAsync("tools/call", new { name = toolName, arguments });

    private async Task DoInitHandshakeAsync()
    {
        using JsonDocument _ = await SendRequestAsync("initialize", new
        {
            protocolVersion = "2024-11-05",
            capabilities = new { },
            clientInfo = new { name = "test", version = "1.0" }
        });

        // Notification — no response expected.
        string notif = JsonSerializer.Serialize(new
        {
            jsonrpc = "2.0",
            method = "notifications/initialized",
            @params = new { }
        });
        await _stdin!.WriteLineAsync(notif);
        await _stdin.FlushAsync();
    }

    private async Task<JsonDocument> ReadResponseAsync(int id)
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        while (true)
        {
            string? line = await _stdout!.ReadLineAsync(cts.Token);
            if (line is null)
                throw new EndOfStreamException("MCP server closed stdout unexpectedly.");
            if (string.IsNullOrWhiteSpace(line) || !line.StartsWith('{'))
                continue;

            var doc = JsonDocument.Parse(line);
            if (doc.RootElement.TryGetProperty("id", out JsonElement idEl) && idEl.GetInt32() == id)
                return doc;
            doc.Dispose();
        }
    }

    private static bool IsError(JsonElement result) =>
        result.TryGetProperty("isError", out JsonElement el) && el.GetBoolean();

    private static string ComputeWorkspaceId(string path) =>
        Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes(path)))[..16].ToLowerInvariant();
}
