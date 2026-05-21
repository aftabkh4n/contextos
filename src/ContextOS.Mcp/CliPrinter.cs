using System.Reflection;

namespace ContextOS.Mcp;

/// <summary>Prints CLI output for init, help, and version commands.</summary>
internal static class CliPrinter
{
    internal static string GetVersion() =>
        typeof(CliPrinter).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion?.Split('+')[0]   // strip build metadata suffix
        ?? "0.1.0";

    internal static void PrintVersion() =>
        Console.WriteLine($"ContextOS {GetVersion()}");

    internal static void PrintHelp()
    {
        Console.WriteLine("ContextOS — persistent engineering context for AI coding agents");
        Console.WriteLine();
        Console.WriteLine("Usage: contextos <command>");
        Console.WriteLine();
        Console.WriteLine("Commands:");
        Console.WriteLine("  serve        Start the MCP server on stdio (default when no command given)");
        Console.WriteLine("  init         Print setup commands for Claude Code and Cursor");
        Console.WriteLine("  --version    Print version and exit");
        Console.WriteLine("  --selftest   Validate the embeddings provider and exit");
        Console.WriteLine("  --help       Show this message");
        Console.WriteLine();
        Console.WriteLine("Documentation: https://github.com/aftabkh4n/contextos");
    }

    internal static void PrintInit()
    {
        string processPath = Environment.ProcessPath ?? "contextos";
        bool isDotnetExec = Path.GetFileNameWithoutExtension(processPath)
            .Equals("dotnet", StringComparison.OrdinalIgnoreCase);

        Console.WriteLine("ContextOS init — copy one of the following to register the MCP server.");
        Console.WriteLine();

        if (isDotnetExec)
        {
#pragma warning disable IL3000 // Assembly.Location is valid here: isDotnetExec=true means we are not inside a single-file bundle
            string dllPath = typeof(CliPrinter).Assembly.Location;
#pragma warning restore IL3000
            Console.WriteLine("--- Claude Code (run once in your terminal) ---");
            Console.WriteLine($"claude mcp add --scope user contextos -- dotnet exec \"{dllPath}\" serve");
            Console.WriteLine();
            Console.WriteLine("--- Cursor (~/.cursor/mcp.json) ---");
            Console.WriteLine("{");
            Console.WriteLine("  \"mcpServers\": {");
            Console.WriteLine("    \"contextos\": {");
            Console.WriteLine("      \"command\": \"dotnet\",");
            Console.WriteLine($"      \"args\": [\"exec\", \"{dllPath.Replace("\\", "\\\\")}\", \"serve\"]");
            Console.WriteLine("    }");
            Console.WriteLine("  }");
            Console.WriteLine("}");
        }
        else
        {
            Console.WriteLine("--- Claude Code (run once in your terminal) ---");
            Console.WriteLine($"claude mcp add --scope user contextos -- \"{processPath}\" serve");
            Console.WriteLine();
            Console.WriteLine("--- Cursor (~/.cursor/mcp.json) ---");
            Console.WriteLine("{");
            Console.WriteLine("  \"mcpServers\": {");
            Console.WriteLine("    \"contextos\": {");
            Console.WriteLine($"      \"command\": \"{processPath.Replace("\\", "\\\\")}\",");
            Console.WriteLine("      \"args\": [\"serve\"]");
            Console.WriteLine("    }");
            Console.WriteLine("  }");
            Console.WriteLine("}");
        }
    }
}
