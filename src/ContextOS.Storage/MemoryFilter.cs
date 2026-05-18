namespace ContextOS.Storage;

/// <summary>Optional filters for <see cref="SqliteStore.ListMemoriesAsync"/>.</summary>
public record MemoryFilter(
    string? Type = null,
    bool IncludeArchived = false,
    int Limit = 50
);
