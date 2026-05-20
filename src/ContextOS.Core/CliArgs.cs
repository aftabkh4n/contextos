namespace ContextOS.Core;

/// <summary>Top-level commands the ContextOS binary accepts.</summary>
public enum CliCommand
{
    /// <summary>Start the MCP server on stdio (default).</summary>
    Serve,
    /// <summary>Print setup snippets for Claude Code and Cursor.</summary>
    Init,
    /// <summary>Print usage summary.</summary>
    Help,
    /// <summary>Print the assembly version and exit.</summary>
    Version,
    /// <summary>Validate the embeddings provider and exit.</summary>
    Selftest,
}

/// <summary>Parses the first command-line argument into a <see cref="CliCommand"/>.</summary>
public static class CliArgs
{
    public static CliCommand Parse(string[] args) =>
        args.Length == 0
            ? CliCommand.Serve
            : args[0].ToLowerInvariant() switch
            {
                "serve"      => CliCommand.Serve,
                "init"       => CliCommand.Init,
                "--help" or "-h" or "help" => CliCommand.Help,
                "--version" or "-v" or "version" => CliCommand.Version,
                "--selftest" or "selftest" => CliCommand.Selftest,
                _ => CliCommand.Serve,
            };
}
