using System.Text.Json;
using ContextOS.Core;

namespace ContextOS.Embeddings;

/// <summary>
/// Reads <c>~/.contextos/config.json</c> and creates the configured <see cref="IEmbeddingsProvider"/>.
/// The base directory can be overridden via the <c>CONTEXTOS_HOME</c> environment variable,
/// which is used in tests and non-standard deployments.
/// </summary>
public static class EmbeddingsFactory
{
    private static readonly JsonSerializerOptions JsonOpts =
        new(JsonSerializerDefaults.Web);

    /// <summary>
    /// Returns the ContextOS data directory. Defaults to <c>~/.contextos</c>.
    /// Override with the <c>CONTEXTOS_HOME</c> environment variable.
    /// </summary>
    public static string GetContextosHome() =>
        Environment.GetEnvironmentVariable("CONTEXTOS_HOME")
        ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".contextos");

    /// <summary>
    /// Loads config from disk (defaults if absent) and creates the provider.
    /// </summary>
    public static IEmbeddingsProvider Create(string? modelsDir = null) =>
        CreateFromConfig(LoadConfig(), modelsDir);

    /// <summary>Creates a provider from an explicit config record.</summary>
    public static IEmbeddingsProvider CreateFromConfig(EmbeddingsConfig cfg, string? modelsDir = null) =>
        cfg.Provider.ToLowerInvariant() switch
        {
            "ollama" => new OllamaProvider(cfg.OllamaUrl, cfg.OllamaModel),
            "openai" => new OpenAiProvider(cfg.OpenAiModel, cfg.OpenAiApiKey ?? string.Empty),
            _        => OnnxMiniLmProvider.Create(modelsDir),
        };

    /// <summary>
    /// Reads the config file and returns the embeddings section.
    /// Never throws; returns default config if the file is absent or malformed.
    /// </summary>
    public static EmbeddingsConfig LoadConfig()
    {
        string path = Path.Combine(GetContextosHome(), "config.json");

        if (!File.Exists(path))
            return new EmbeddingsConfig();

        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            if (!doc.RootElement.TryGetProperty("embeddings", out JsonElement el))
                return new EmbeddingsConfig();

            return JsonSerializer.Deserialize<EmbeddingsConfig>(el.GetRawText(), JsonOpts)
                   ?? new EmbeddingsConfig();
        }
        catch
        {
            // Malformed config — use defaults rather than crashing.
            return new EmbeddingsConfig();
        }
    }
}
