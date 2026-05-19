using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ContextOS.Core;
using ContextOS.Storage;

namespace ContextOS.Tests;

/// <summary>
/// Integration tests for auto-hydration. Each test spawns the MCP server against a workspace
/// whose DB is pre-populated via <see cref="SqliteStore"/> before the subprocess starts.
/// CONTEXTOS_HOME is set to a temp dir so tests are fully isolated from the real ~/.contextos.
/// </summary>
[Trait("Category", "Integration")]
public sealed class AutoHydrationTests : IAsyncLifetime
{
    private Process? _server;
    private StreamWriter? _stdin;
    private StreamReader? _stdout;
    private Task<string>? _stderrTask;
    private string? _tempWorkspace;
    private string? _tempContextosHome;
    private int _nextId = 1;

    public async Task InitializeAsync()
    {
        string dll = McpTestHelpers.FindMcpDll();

        _tempWorkspace = Path.Combine(Path.GetTempPath(), $"ctos-ws-{Guid.NewGuid():N}");
        _tempContextosHome = Path.Combine(Path.GetTempPath(), $"ctos-home-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempWorkspace);
        Directory.CreateDirectory(_tempContextosHome);

        // Pre-populate a memory so it shows up in the hydration instructions.
        string workspaceId = ComputeWorkspaceId(_tempWorkspace);
        string dbPath = Path.Combine(_tempContextosHome, $"{workspaceId}.db");
        SqliteStore preStore = await SqliteStore.OpenAsync(dbPath);
        await preStore.UpsertWorkspaceAsync(new Workspace(
            workspaceId, _tempWorkspace, "auto-hydration-test", null,
            DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()));
        await preStore.AddMemoryAsync(workspaceId, "decision",
            "kafka outbox pattern chosen for reliable event delivery", importance: 0.9);
        await preStore.DisposeAsync();

        var psi = new ProcessStartInfo("dotnet", $"exec \"{dll}\"")
        {
            WorkingDirectory = _tempWorkspace,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        psi.Environment["CONTEXTOS_HOME"] = _tempContextosHome;

        _server = Process.Start(psi)!;
        _stdin = _server.StandardInput;
        _stdout = _server.StandardOutput;
        _stderrTask = _server.StandardError.ReadToEndAsync();

        using var startupCts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        Task exitTask = _server.WaitForExitAsync(startupCts.Token);
        Task delayTask = Task.Delay(TimeSpan.FromSeconds(2), startupCts.Token);

        if (await Task.WhenAny(exitTask, delayTask) == exitTask)
        {
            string stderr = await _stderrTask.WaitAsync(TimeSpan.FromSeconds(3));
            throw new InvalidOperationException(
                $"MCP server subprocess died during startup. Exit code: {_server.ExitCode}.\nStderr:\n{stderr}");
        }
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

        await Task.Delay(200);

        if (_tempWorkspace is not null && Directory.Exists(_tempWorkspace))
            try { Directory.Delete(_tempWorkspace, recursive: true); } catch { /* best-effort */ }

        if (_tempContextosHome is not null && Directory.Exists(_tempContextosHome))
            try { Directory.Delete(_tempContextosHome, recursive: true); } catch { /* best-effort */ }
    }

    // -------------------------------------------------------------------------
    // Tests
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Initialize_InstructionsFieldIsPresent()
    {
        using JsonDocument response = await SendRequestAsync("initialize", new
        {
            protocolVersion = "2024-11-05",
            capabilities = new { },
            clientInfo = new { name = "test", version = "1.0" }
        });

        JsonElement result = response.RootElement.GetProperty("result");
        Assert.True(result.TryGetProperty("instructions", out _),
            $"Expected 'instructions' field in initialize result. Got: {result}");
    }

    [Fact]
    public async Task Initialize_InstructionsContainsStoredMemoryContent()
    {
        using JsonDocument response = await SendRequestAsync("initialize", new
        {
            protocolVersion = "2024-11-05",
            capabilities = new { },
            clientInfo = new { name = "test", version = "1.0" }
        });

        string instructions = response.RootElement
            .GetProperty("result")
            .GetProperty("instructions")
            .GetString()!;

        Assert.Contains("kafka outbox", instructions, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Initialize_InstructionsContainsFramingPrefix()
    {
        using JsonDocument response = await SendRequestAsync("initialize", new
        {
            protocolVersion = "2024-11-05",
            capabilities = new { },
            clientInfo = new { name = "test", version = "1.0" }
        });

        string instructions = response.RootElement
            .GetProperty("result")
            .GetProperty("instructions")
            .GetString()!;

        Assert.Contains("automatically loaded by ContextOS", instructions, StringComparison.OrdinalIgnoreCase);
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

    private async Task<JsonDocument> ReadResponseAsync(int id)
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        while (true)
        {
            string? line = await _stdout!.ReadLineAsync(cts.Token);
            if (line is null)
            {
                string stderr = await (_stderrTask ?? Task.FromResult("(stderr not captured)"))
                    .WaitAsync(TimeSpan.FromSeconds(3));
                int code = (_server?.HasExited == true) ? _server.ExitCode : -1;
                throw new InvalidOperationException(
                    $"MCP server subprocess died during test. Exit code: {code}.\nStderr:\n{stderr}");
            }
            if (string.IsNullOrWhiteSpace(line) || !line.StartsWith('{'))
                continue;

            var doc = JsonDocument.Parse(line);
            if (doc.RootElement.TryGetProperty("id", out JsonElement idEl) && idEl.GetInt32() == id)
                return doc;
            doc.Dispose();
        }
    }

    private static string ComputeWorkspaceId(string path) =>
        Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes(path)))[..16].ToLowerInvariant();
}
