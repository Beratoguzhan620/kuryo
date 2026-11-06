namespace Kuryo.Api.Features.Matching;

public sealed class MatchingOptions
{
    public const string Section = "Matching";

    /// <summary>Off in tests, which run the matcher by hand.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>How often the matcher looks for expired offers and waiting orders when nothing wakes it sooner.</summary>
    public TimeSpan Interval { get; set; } = TimeSpan.FromSeconds(2);

    /// <summary>Orders handled in one pass, so one busy company cannot hold up the rest for long.</summary>
    public int BatchSize { get; set; } = 200;
}
