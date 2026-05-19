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

    /// <summary>
    /// Creates a provider from an explicit config record.
    /// Throws <see cref="InvalidOperationException"/> or <see cref="ArgumentException"/>
    /// for invalid configuration (missing API key, unknown model).
    /// </summary>
    public static IEmbeddingsProvider CreateFromConfig(EmbeddingsConfig cfg, string? modelsDir = null) =>
        cfg.Provider.ToLowerInvariant() switch
        {
            "ollama" => CreateOllamaProvider(cfg),
            "openai" => CreateOpenAiProvider(cfg),
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

    private static OllamaProvider CreateOllamaProvider(EmbeddingsConfig cfg) =>
        new(cfg.OllamaUrl, cfg.OllamaModel,
            new HttpClient { Timeout = TimeSpan.FromSeconds(30) });

    private static OpenAiProvider CreateOpenAiProvider(EmbeddingsConfig cfg)
    {
        // Environment variable wins over config file, matching conventions of other tools.
        string apiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY")
                        ?? cfg.OpenAiApiKey
                        ?? string.Empty;

        if (string.IsNullOrWhiteSpace(apiKey))
            throw new InvalidOperationException(
                "OpenAI API key is required. Set openAiApiKey in ~/.contextos/config.json " +
                "or set the OPENAI_API_KEY environment variable.");

        return new OpenAiProvider(cfg.OpenAiModel, apiKey,
            new HttpClient { Timeout = TimeSpan.FromSeconds(30) });
    }
}
