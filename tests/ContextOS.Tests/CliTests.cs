using System.Diagnostics;
using ContextOS.Core;

namespace ContextOS.Tests;

/// <summary>
/// Unit tests for CLI argument parsing (pure function, no subprocess).
/// Subprocess-based tests for --version and init live at the bottom of this file.
/// </summary>
public sealed class CliTests
{
    // -------------------------------------------------------------------------
    // CliArgs.Parse unit tests
    // -------------------------------------------------------------------------

    [Theory]
    [InlineData(new string[0], CliCommand.Serve)]
    [InlineData(new[] { "serve" }, CliCommand.Serve)]
    [InlineData(new[] { "SERVE" }, CliCommand.Serve)]
    [InlineData(new[] { "unknown-command" }, CliCommand.Serve)]
    [InlineData(new[] { "init" }, CliCommand.Init)]
    [InlineData(new[] { "INIT" }, CliCommand.Init)]
    [InlineData(new[] { "--help" }, CliCommand.Help)]
    [InlineData(new[] { "-h" }, CliCommand.Help)]
    [InlineData(new[] { "help" }, CliCommand.Help)]
    [InlineData(new[] { "--version" }, CliCommand.Version)]
    [InlineData(new[] { "-v" }, CliCommand.Version)]
    [InlineData(new[] { "version" }, CliCommand.Version)]
    [InlineData(new[] { "--selftest" }, CliCommand.Selftest)]
    [InlineData(new[] { "selftest" }, CliCommand.Selftest)]
    public void Parse_ReturnsExpectedCommand(string[] args, CliCommand expected)
    {
        Assert.Equal(expected, CliArgs.Parse(args));
    }

    [Fact]
    public void Parse_OnlyFirstArgMatters()
    {
        // Extra args after the command are ignored by the parser.
        Assert.Equal(CliCommand.Init, CliArgs.Parse(["init", "--extra"]));
        Assert.Equal(CliCommand.Version, CliArgs.Parse(["--version", "garbage"]));
    }

    // -------------------------------------------------------------------------
    // Version subprocess test
    // -------------------------------------------------------------------------

    [Trait("Category", "Integration")]
    [Fact]
    public async Task VersionFlag_PrintsVersionString()
    {
        string dll = McpTestHelpers.FindMcpDll();
        var psi = new ProcessStartInfo("dotnet", $"exec \"{dll}\" --version")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };

        using var proc = Process.Start(psi)!;
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        await proc.WaitForExitAsync(cts.Token);

        string stdout = await proc.StandardOutput.ReadToEndAsync();
        Assert.Equal(0, proc.ExitCode);
        Assert.Contains("0.1.0", stdout);
        Assert.Contains("ContextOS", stdout, StringComparison.OrdinalIgnoreCase);
    }

    [Trait("Category", "Integration")]
    [Fact]
    public async Task HelpFlag_PrintsCommandList()
    {
        string dll = McpTestHelpers.FindMcpDll();
        var psi = new ProcessStartInfo("dotnet", $"exec \"{dll}\" --help")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };

        using var proc = Process.Start(psi)!;
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        await proc.WaitForExitAsync(cts.Token);

        string stdout = await proc.StandardOutput.ReadToEndAsync();
        Assert.Equal(0, proc.ExitCode);
        Assert.Contains("serve", stdout, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("init", stdout, StringComparison.OrdinalIgnoreCase);
    }
}
