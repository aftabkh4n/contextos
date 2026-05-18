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
                    string dll = Path.Combine(dir, "src", "ContextOS.Mcp", "bin", config, "net10.0", "ContextOS.Mcp.dll");
                    if (File.Exists(dll)) return dll;
                }
                throw new FileNotFoundException(
                    $"ContextOS.Mcp.dll not found under {dir}/src/ContextOS.Mcp/bin/. Build the solution first.");
            }
            dir = Path.GetDirectoryName(dir);
        }
        throw new InvalidOperationException("Could not find solution root (.slnx) by walking up from test BaseDirectory.");
    }
}
