namespace ContextOS.Core;

/// <summary>Shared scoring helpers used by retrieval and context assembly.</summary>
public static class Scoring
{
    private const double DecayDays = 30.0;

    /// <summary>
    /// Returns <c>exp(-age_days / 30) * (0.5 + importance)</c>, the recency-importance
    /// score used for ranking memories. DecayDays=30 means a 30-day-old memory scores
    /// ~37% of a brand-new one at equal importance.
    /// </summary>
    public static double RecencyImportance(long createdAtUnix, double importance, long nowUnix)
    {
        double ageDays = (nowUnix - createdAtUnix) / 86400.0;
        return Math.Exp(-ageDays / DecayDays) * (0.5 + importance);
    }
}
