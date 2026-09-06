namespace ContextOS.Core;

/// <summary>Mirrors the <c>memory</c> section of <c>~/.contextos/config.json</c>.</summary>
public record MemoryConfig(int DecayDays = 90);
