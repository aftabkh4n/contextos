namespace ContextOS.Core;

/// <summary>Optional filters for <see cref="IMemoryStore.ListMemoriesAsync"/>.</summary>
public record MemoryFilter(
    string? Type = null,
    bool IncludeArchived = false,
    int Limit = 50
);
