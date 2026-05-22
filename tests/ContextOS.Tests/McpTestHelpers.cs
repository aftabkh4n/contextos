namespace ContextOS.Tests;

internal static class McpTestHelpers
{
    internal static string FindMcpDll()
    {
        string? dir = AppContext.BaseDirectory;
        while (dir is not null)
        {
            if (Directory.GetFiles(dir, "*.slnx").Length > 0)
            {
                foreach (string config in new[] { "Debug", "Release" })
                {
                    // Day 12: AssemblyName changed to "contextos". Fall back to the old
                    // name so a stale build without the rename does not silently break.
                    // Also require the runtimeconfig: a DLL without one is a stale artifact
                    // from a previous AssemblyName and will fail under dotnet exec.
                    foreach (string name in new[] { "contextos.dll", "ContextOS.Mcp.dll" })
                    {
                        string dll = Path.Combine(dir, "src", "ContextOS.Mcp", "bin", config, "net10.0", name);
                        string runtimeconfig = Path.ChangeExtension(dll, null) + ".runtimeconfig.json";
                        if (File.Exists(dll) && File.Exists(runtimeconfig)) return dll;
                    }
                }
                throw new FileNotFoundException(
                    "MCP assembly not found under src/ContextOS.Mcp/bin/. " +
                    "Expected contextos.dll (or ContextOS.Mcp.dll) with a matching runtimeconfig. " +
                    "Build the solution first.");
            }
            dir = Path.GetDirectoryName(dir);
        }
        throw new InvalidOperationException("Could not find solution root (.slnx) by walking up from test BaseDirectory.");
    }
}
