namespace RaccoonNinja.McpToolset.Server.GitOps.Parsers;

/// <summary>Human-readable "3 days ago" / "in 2 hours" formatter.</summary>
public static class RelativeTime
{
    /// <summary>Format the elapsed time between <paramref name="then"/> and <paramref name="now"/>.</summary>
    public static string Describe(DateTimeOffset then, DateTimeOffset? now = null)
    {
        var reference = now ?? DateTimeOffset.UtcNow;
        var seconds = (reference - then).TotalSeconds;
        return Format(seconds);
    }

    private static string Format(double seconds)
    {
        var absSeconds = Math.Abs(seconds);
        int n;
        string unit;

        switch (absSeconds)
        {
            case < 60:
                n = (int)absSeconds;
                unit = "second";
                break;
            case < 3600:
                n = (int)(absSeconds / 60);
                unit = "minute";
                break;
            case < 86400:
                n = (int)(absSeconds / 3600);
                unit = "hour";
                break;
            case < 86400 * 30:
                n = (int)(absSeconds / 86400);
                unit = "day";
                break;
            case < 86400 * 365:
                n = (int)(absSeconds / (86400 * 30));
                unit = "month";
                break;
            default:
                n = (int)(absSeconds / (86400 * 365));
                unit = "year";
                break;
        }

        var plural = n == 1 ? string.Empty : "s";

        return seconds >= 0
            ? $"{n} {unit}{plural} ago"
            : $"in {n} {unit}{plural}";
    }
}