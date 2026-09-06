using ContextOS.Core;
using Microsoft.Extensions.Logging;

namespace ContextOS.Storage;

/// <summary>
/// Archives memories that have not been recalled within their decay window.
/// Run once on startup via <see cref="RunDecayPassAsync"/>.
/// </summary>
public sealed class DecayService
{
    private readonly MemoryConfig _config;

    /// <param name="config">Global memory configuration. <see cref="MemoryConfig.DecayDays"/> = 0 disables decay globally.</param>
    public DecayService(MemoryConfig config)
    {
        _config = config;
    }

    /// <summary>
    /// Archives any memory whose recall clock has expired.
    /// If <see cref="MemoryConfig.DecayDays"/> is 0, the pass is skipped entirely.
    /// Per-row <c>decay_days = 0</c> exempts an individual memory even when global decay is enabled.
    /// </summary>
    public async Task RunDecayPassAsync(
        IMemoryStore store,
        ILogger logger,
        CancellationToken ct = default)
    {
        if (_config.DecayDays == 0)
        {
            logger.LogDebug("Memory decay disabled globally (decayDays=0 in config)");
            return;
        }

        IReadOnlyList<Memory> active = await store.ListAllActiveMemoriesAsync(ct);
        DateTimeOffset now = DateTimeOffset.UtcNow;
        var toArchive = new List<string>();

        foreach (Memory m in active)
        {
            if (m.DecayDays == 0)
                continue;

            if (m.LastRecalledAt.HasValue)
            {
                if ((now - m.LastRecalledAt.Value).TotalDays > m.DecayDays)
                    toArchive.Add(m.Id);
            }
            else
            {
                // Never recalled: clock starts from creation.
                DateTimeOffset createdAt = DateTimeOffset.FromUnixTimeMilliseconds(m.CreatedAt);
                if ((now - createdAt).TotalDays > m.DecayDays)
                    toArchive.Add(m.Id);
            }
        }

        if (toArchive.Count > 0)
        {
            await store.ArchiveManyAsync(toArchive, ct);
            logger.LogInformation("Archived {Count} memories due to decay", toArchive.Count);
        }
    }
}
