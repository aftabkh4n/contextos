namespace ContextOS.Embeddings;

/// <summary>
/// Mirrors the <c>embeddings</c> section of <c>~/.contextos/config.json</c>.
/// All fields have sensible defaults so the file can be absent.
/// </summary>
public record EmbeddingsConfig(
    string Provider = "onnx",
    string OllamaUrl = "http://localhost:11434",
    string OllamaModel = "nomic-embed-text",
    string OpenAiModel = "text-embedding-3-small",
    string? OpenAiApiKey = null
);
