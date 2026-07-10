using RaccoonNinja.McpToolset.Server.GitOps.Models;

namespace RaccoonNinja.McpToolset.Server.GitOps.Parsers;

/// <summary>Parser for <c>git stash list --format=%gd%x1f%gs%x1f%at</c>.</summary>
public static class StashListParser
{
    public static IReadOnlyList<StashEntry> Parse(byte[] raw)
    {
        var text = TextDecoding.Decode(raw);
        var result = new List<StashEntry>();
        foreach (var line in text.Split('\n'))
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            var parts = line.Split('\x1f');
            if (parts.Length < 3) continue;
            var refStr = parts[0];
            var subject = parts[1];
            var ts = long.TryParse(parts[2], out var seconds) ? seconds : 0L;
            var idx = 0;
            if (refStr.StartsWith("stash@{", StringComparison.Ordinal) && refStr.EndsWith('}'))
            {
                var inner = refStr.Substring("stash@{".Length, refStr.Length - "stash@{".Length - 1);
                _ = int.TryParse(inner, out idx);
            }
            var when = DateTimeOffset.FromUnixTimeSeconds(ts);
            result.Add(new StashEntry
            {
                Index = idx,
                Name = refStr,
                Subject = subject,
                RelativeTime = RelativeTime.Describe(when),
            });
        }
        return result;
    }
}