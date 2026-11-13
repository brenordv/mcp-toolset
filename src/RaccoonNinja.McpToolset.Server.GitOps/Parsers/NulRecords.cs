using RaccoonNinja.McpToolset.Server.GitOps.Errors.GitCheckExceptions;

namespace RaccoonNinja.McpToolset.Server.GitOps.Parsers;

/// <summary>
/// Splits git output in which each record is a fixed number of fields, each ended by NUL: a format whose fields are joined
/// by <c>%x00</c>, printed as <c>tformat:</c> with <c>-z</c>, so git ends each record with one more NUL instead of a
/// newline. git copies every field these formats print (commit text, author names, reflog messages) as a C string that
/// stops at the first NUL, so no field holds one, the field count alone frames the records, and no field's text can add,
/// split, or shift a record. Two pins in the fixed config keep that output in this shape: <c>log.showSignature=false</c>
/// (signature text would print ahead of a record) and <c>i18n.logOutputEncoding=UTF-8</c> (another encoding would
/// re-encode the NUL bytes along with every field). <see cref="SplitNewlineTerminated"/> reads the same fields with a
/// newline after each record, the shape <c>git for-each-ref</c> prints.
/// </summary>
internal static class NulRecords
{
    /// <summary>The message a call fails with when git's output is not whole records.</summary>
    internal const string NotWholeRecordsMessage = "git printed output that is not a whole number of NUL-framed records";

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
        => Frame(TextDecoding.Decode(raw).Split('\0'), fieldCount, truncated);

    /// <summary>
    /// Split <paramref name="raw"/> into records of <paramref name="fieldCount"/> fields, each field ended by NUL and each
    /// record then ended by a newline: a <c>git for-each-ref</c> format with <c>%00</c> after every field, since that
    /// command has no <c>-z</c> and ends each ref it prints with a newline. The fields are C strings as in
    /// <see cref="Split"/>, so the field count finds the end of each record, and the byte after it must be that newline;
    /// a newline anywhere else is text inside a field.
    /// </summary>
    /// <param name="raw">git's standard output; <c>null</c> or empty reads as no records.</param>
    /// <param name="fieldCount">How many fields one record has.</param>
    /// <param name="truncated">
    /// Whether the output hit the output cap. A cut output can end inside a record, or between a record's last NUL and
    /// its newline; a partial record is returned with its missing fields empty.
    /// </param>
    /// <returns>The fields of each record, in order.</returns>
    /// <exception cref="GitCommandException">
    /// A record is not followed by a newline, unless <paramref name="truncated"/> is <c>true</c> and the output ends right
    /// after that record's last NUL; or the output is not whole records and <paramref name="truncated"/> is <c>false</c>.
    /// </exception>
    public static IReadOnlyList<string[]> SplitNewlineTerminated(byte[] raw, int fieldCount, bool truncated)
    {
        var fields = TextDecoding.Decode(raw).Split('\0');
        for (var index = fieldCount; index < fields.Length; index += fieldCount)
        {
            var cutRightAfterLastRecord = truncated && index == fields.Length - 1 && fields[index].Length == 0;
            if (fields[index].StartsWith('\n'))
            {
                fields[index] = fields[index][1..];
            }
            else if (!cutRightAfterLastRecord)
            {
                throw new GitCommandException(NotWholeRecordsMessage);
            }
        }

        return Frame(fields, fieldCount, truncated);
    }

    /// <summary>
    /// Return <paramref name="records"/> without its last record when <paramref name="truncated"/> is <c>true</c> and every
    /// field of that record after its first <paramref name="identifyingFieldCount"/> is empty. Those leading fields name
    /// something (an object id, a selector): a non-empty later field shows the output reached past them, so they are whole,
    /// while a record the cap cut inside one of them can carry a cut value, and git reads a name as a ref before it reads it
    /// as an abbreviated object id, so a cut id passed back as a ref could name another ref. A whole last record whose
    /// later fields are all empty is left out of a cut output too.
    /// </summary>
    /// <param name="records">Records as <see cref="Split"/> returns them, a cut last record padded with empty fields.</param>
    /// <param name="identifyingFieldCount">How many leading fields of a record name something.</param>
    /// <param name="truncated">Whether the output hit the output cap.</param>
    /// <returns>The records, without a last record that may carry a cut identifying field.</returns>
    public static IReadOnlyList<string[]> LeaveOutCutIdentifiers(IReadOnlyList<string[]> records, int identifyingFieldCount, bool truncated)
    {
        var cutBeforeLaterFields = truncated
            && records.Count > 0
            && records[^1].Skip(identifyingFieldCount).All(field => field.Length == 0);
        return cutBeforeLaterFields ? [.. records.Take(records.Count - 1)] : records;
    }

    /// <summary>Group NUL-separated <paramref name="fields"/> into records of <paramref name="fieldCount"/>.</summary>
    /// <param name="fields">The output split on NUL; the last element is whatever followed the last NUL.</param>
    /// <param name="fieldCount">How many fields one record has.</param>
    /// <param name="truncated">Whether the output hit the output cap.</param>
    /// <returns>The fields of each record, in order, a cut last record padded with empty fields.</returns>
    /// <exception cref="GitCommandException">
    /// <paramref name="truncated"/> is <c>false</c> and anything follows the last whole record.
    /// </exception>
    private static List<string[]> Frame(string[] fields, int fieldCount, bool truncated)
    {
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