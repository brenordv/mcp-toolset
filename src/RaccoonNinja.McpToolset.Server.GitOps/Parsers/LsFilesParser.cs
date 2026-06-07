namespace RaccoonNinja.McpToolset.Server.GitOps.Parsers;

/// <summary>Parser for <c>git ls-files -z</c> output (NUL-separated path list).</summary>
public static class LsFilesParser
{
    public static IReadOnlyList<string> Parse(byte[] raw)
    {
        var text = TextDecoding.Decode(raw);

        return string.IsNullOrWhiteSpace(text)
            ? []
            : [.. text.Split('\0').Where(path => !string.IsNullOrEmpty(path))];
    }
}