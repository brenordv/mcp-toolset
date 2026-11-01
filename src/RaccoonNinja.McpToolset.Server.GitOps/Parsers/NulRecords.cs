using RaccoonNinja.McpToolset.Server.GitOps.Errors.GitCheckExceptions;

namespace RaccoonNinja.McpToolset.Server.GitOps.Parsers;

/// <summary>
/// Splits git output in which each record is a fixed number of fields, each ended by NUL: a format whose fields are joined
/// by <c>%x00</c>, printed as <c>tformat:</c> with <c>-z</c>, so git ends each record with one more NUL instead of a
/// newline. git copies every field these formats print (commit text, author names, reflog messages) as a C string that
/// stops at the first NUL, so no field holds one, the field count alone frames the records, and no field's text can add,
/// split, or shift a record. Two pins in the fixed config keep that output in this shape: <c>log.showSignature=false</c>
/// (signature text would print ahead of a record) and <c>i18n.logOutputEncoding=UTF-8</c> (another encoding would
/// re-encode the NUL bytes along with every field).
/// </summary>
internal static class NulRecords
{
    private const string NotWholeRecordsMessage = "git printed output that is not a whole number of NUL-framed records";

    /// <summary>Split <paramref name="raw"/> into records of <paramref name="fieldCount"/> fields.</summary>
    /// <param name="raw">git's standard output; <c>null</c> or empty reads as no records.</param>
    /// <param name="fieldCount">How many fields one record has.</param>
    /// <param name="truncated">
    /// Whether the output hit the output cap. A cut output can end inside a record; that partial record is returned with
    /// its missing fields empty.
    /// </param>
    /// <returns>The fields of each record, in order.</returns>
    /// <exception cref="GitCommandException">
    /// <paramref name="truncated"/> is <c>false</c> and the output does not end with a NUL, or its field count is not a
    /// multiple of <paramref name="fieldCount"/>.
    /// </exception>
    public static IReadOnlyList<string[]> Split(byte[] raw, int fieldCount, bool truncated)
    {
        var fields = TextDecoding.Decode(raw).Split('\0');
        var wholeRecords = (fields.Length - 1) / fieldCount;
        var records = Enumerable.Range(0, wholeRecords)
            .Select(record => fields[(record * fieldCount)..((record + 1) * fieldCount)])
            .ToList();
        var tail = fields[(wholeRecords * fieldCount)..];
        if (tail is [""])
        {
            return records;
        }

        if (!truncated)
        {
            throw new GitCommandException(NotWholeRecordsMessage);
        }

        records.Add([.. tail, .. Enumerable.Repeat(string.Empty, fieldCount - tail.Length)]);
        return records;
    }
}