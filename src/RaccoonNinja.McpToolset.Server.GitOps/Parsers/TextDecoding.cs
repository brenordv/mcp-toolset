using System.Text;

namespace RaccoonNinja.McpToolset.Server.GitOps.Parsers;

/// <summary>UTF-8 decoding helpers shared across parsers.</summary>
public static class TextDecoding
{
    /// <summary>Decode raw git bytes as UTF-8 (invalid sequences become replacement chars).</summary>
    public static string Decode(byte[] raw)
    {
        return raw == null || raw.Length == 0
            ? string.Empty
            : Encoding.UTF8.GetString(raw);
    }
}