using RaccoonNinja.McpToolset.Server.GitOps.Models;

namespace RaccoonNinja.McpToolset.Server.GitOps.Parsers;

/// <summary>
/// Parser for <c>git for-each-ref</c> output produced with the format
/// <c>%(refname)\x1f%(HEAD)\x1f%(objectname)\x1f%(upstream:short)\x1f%(contents:subject)</c>.
/// </summary>
public static class BranchParser
{
    public static IReadOnlyList<Branch> Parse(byte[] raw)
    {
        var text = TextDecoding.Decode(raw);
        var result = new List<Branch>();
        foreach (var line in text.Split('\n'))
        {
            if (string.IsNullOrEmpty(line)) continue;
            var parts = line.Split('\x1f');
            if (parts.Length < 5) continue;
            var refname = parts[0];
            var headMarker = parts[1];
            var oid = parts[2];
            var upstream = parts[3];
            var subject = parts[4];
            var isRemote = refname.StartsWith("refs/remotes/", StringComparison.Ordinal);
            string name;
            if (refname.StartsWith("refs/heads/", StringComparison.Ordinal))
                name = refname.Substring("refs/heads/".Length);
            else if (isRemote)
                name = refname.Substring("refs/remotes/".Length);
            else
                name = refname;
            result.Add(new Branch
            {
                Name = name,
                IsCurrent = headMarker == "*",
                IsRemote = isRemote,
                Upstream = string.IsNullOrEmpty(upstream) ? null : upstream,
                TipHash = oid,
                Subject = string.IsNullOrEmpty(subject) ? null : subject,
            });
        }
        return result;
    }
}