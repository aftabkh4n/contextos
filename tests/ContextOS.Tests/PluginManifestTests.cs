using System.Text.Json;

namespace ContextOS.Tests;

/// <summary>
/// Guards the plugin manifest. ensure-binary.sh installs into bin/&lt;version&gt;,
/// so the MCP server command must point at the folder for the manifest's own version.
/// </summary>
public sealed class PluginManifestTests
{
    [Fact]
    public void McpServerCommand_PointsAtManifestVersionFolder()
    {
        string? dir = AppContext.BaseDirectory;
        while (dir is not null && Directory.GetFiles(dir, "*.slnx").Length == 0)
            dir = Path.GetDirectoryName(dir);
        Assert.NotNull(dir);

        string manifestPath = Path.Combine(dir, "plugins", "contextos", ".claude-plugin", "plugin.json");
        using JsonDocument manifest = JsonDocument.Parse(File.ReadAllText(manifestPath));
        string version = manifest.RootElement.GetProperty("version").GetString()!;
        string command = manifest.RootElement
            .GetProperty("mcpServers").GetProperty("contextos").GetProperty("command").GetString()!;

        Assert.Contains($"/bin/{version}/", command);
    }
}
