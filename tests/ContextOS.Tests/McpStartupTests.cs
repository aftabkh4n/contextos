using System.Diagnostics;

namespace ContextOS.Tests;

/// <summary>
/// Tests that the MCP server exits with a non-zero code and writes an actionable
/// error to stderr when the configured embeddings provider is not functional.
/// </summary>
[Trait("Category", "Integration")]
public sealed class McpStartupTests
{
    /// <summary>
    /// Configures the Ollama provider (which throws NotImplementedException on every
    /// embed call in v1) via a temp CONTEXTOS_HOME and asserts the server exits before
    /// accepting connections.
    /// </summary>
    [Fact]
    public async Task StartupFails_WhenEmbeddingsProviderIsNotFunctional()
    {
        // Point CONTEXTOS_HOME at a temp dir so this test does not read or modify the
        // developer's real ~/.contextos/config.json.
        string tempHome = Path.Combine(Path.GetTempPath(), $"contextos-home-{Guid.NewGuid():N}");
        string tempWork = Path.Combine(Path.GetTempPath(), $"contextos-work-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempHome);
        Directory.CreateDirectory(tempWork);

        // Ollama provider throws NotImplementedException on every EmbedAsync call in v1.
        await File.WriteAllTextAsync(
            Path.Combine(tempHome, "config.json"),
            """{"embeddings": {"provider": "ollama"}}""");

        try
        {
            string dll = McpTestHelpers.FindMcpDll();
            var psi = new ProcessStartInfo("dotnet", $"exec \"{dll}\"")
            {
                WorkingDirectory = tempWork,
                RedirectStandardInput  = true,
                RedirectStandardOutput = true,
                RedirectStandardError  = true,
                UseShellExecute        = false,
            };
            psi.EnvironmentVariables["CONTEXTOS_HOME"] = tempHome;

            using var process = Process.Start(psi)!;

            // Read stderr concurrently — if the buffer fills, the process would deadlock
            // waiting for the reader while we wait for exit.
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            Task<string> stderrTask = process.StandardError.ReadToEndAsync(cts.Token);

            try
            {
                await process.WaitForExitAsync(cts.Token);
            }
            catch (OperationCanceledException)
            {
                process.Kill();
                throw new TimeoutException(
                    "MCP server did not exit within 15 seconds after bad embeddings config.");
            }

            string stderr = await stderrTask;

            Assert.NotEqual(0, process.ExitCode);
            Assert.Contains("ContextOS cannot start", stderr);
            Assert.Contains("no functional embeddings provider", stderr);
            Assert.Contains("The configured provider is: ollama", stderr);
        }
        finally
        {
            try { Directory.Delete(tempHome, recursive: true); } catch { }
            try { Directory.Delete(tempWork, recursive: true); } catch { }
        }
    }
}
