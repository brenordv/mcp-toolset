using System.Text;
using System.Text.RegularExpressions;
using RaccoonNinja.McpToolset.Files.Security;
using RaccoonNinja.McpToolset.Files.Selection;
using RaccoonNinja.McpToolset.Files.Storage;
using RaccoonNinja.McpToolset.Files.Text;
using RaccoonNinja.McpToolset.Server.TextEdit.Configuration;
using RaccoonNinja.McpToolset.Server.TextEdit.Errors;
using RaccoonNinja.McpToolset.Server.TextEdit.Journal;

namespace RaccoonNinja.McpToolset.Server.TextEdit.Content;

/// <summary>
/// The single mutation choke point. Every candidate path passes through the same unconditional sequence,
/// run on the confined resolved path and never on the raw input: confine, denylist, ancestor-aware ignore,
/// exists-and-is-a-file, size cap, encoding detection, the not-binary and rewrite-confidence gates, then
/// the transform. Files that would actually change are written under the journal's write-ahead protocol:
/// pre-images are recorded and the batch rows committed as pending before any disk write, the atomic writes
/// run, and the post-image hashes finalize the rows. A batch that changes nothing writes no journal row.
/// </summary>
public sealed class GatedFileWriter
{
    static GatedFileWriter()
    {
        // Needed when a caller supplies an explicit legacy source encoding (for example windows-1252);
        // registered here rather than relying on the detector's static-constructor side effect.
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    private readonly IRootResolver _root;
    private readonly ISecretDenylist _denylist;
    private readonly IEncodingDetector _detector;
    private readonly JournalStore _journal;
    private readonly EditConfig _config;
    private readonly string _rootName;

    /// <summary>Create the writer bound to one root and its journal.</summary>
    /// <param name="root">The confiner for the root.</param>
    /// <param name="denylist">The non-overridable secret denylist.</param>
    /// <param name="detector">The encoding detector.</param>
    /// <param name="journal">The mutation journal.</param>
    /// <param name="config">The server caps.</param>
    /// <param name="rootName">The root's agent-facing name, recorded on each batch.</param>
    public GatedFileWriter(
        IRootResolver root,
        ISecretDenylist denylist,
        IEncodingDetector detector,
        JournalStore journal,
        EditConfig config,
        string rootName)
    {
        _root = root ?? throw new ArgumentNullException(nameof(root));
        _denylist = denylist ?? throw new ArgumentNullException(nameof(denylist));
        _detector = detector ?? throw new ArgumentNullException(nameof(detector));
        _journal = journal ?? throw new ArgumentNullException(nameof(journal));
        _config = config ?? throw new ArgumentNullException(nameof(config));
        _rootName = rootName ?? throw new ArgumentNullException(nameof(rootName));
    }

    /// <summary>Apply <paramref name="transform"/> to each selected path, journaling and writing the files that change.</summary>
    /// <param name="tool">The tool producing the batch (recorded in the journal).</param>
    /// <param name="relativePaths">The root-relative candidate paths from selection.</param>
    /// <param name="transform">The text transform to apply.</param>
    /// <param name="argsSummary">A machine-privacy-safe summary of the call arguments.</param>
    /// <param name="expectedMatchCount">When set, the call aborts before any write unless exactly this many matches would be rewritten.</param>
    /// <param name="dryRun">When true, computes diffs and writes nothing.</param>
    /// <param name="sourceEncoding">An explicit source encoding that bypasses the confidence gate, or <c>null</c> to auto-detect.</param>
    /// <param name="skippedSymlinks">The selector's skipped-symlink count, echoed into the outcome.</param>
    /// <param name="truncated">Whether selection hit its ceiling, echoed into the outcome.</param>
    /// <param name="cancellationToken">Checked between files so the operation budget can bound the batch.</param>
    /// <returns>The batch outcome.</returns>
    /// <exception cref="TextEditException">Thrown for an unknown source encoding or an expected-match-count mismatch (before any write).</exception>
    public BatchOutcome Apply(
        string tool,
        IReadOnlyList<string> relativePaths,
        ITextTransform transform,
        string argsSummary,
        int? expectedMatchCount,
        bool dryRun,
        string sourceEncoding,
        int skippedSymlinks,
        bool truncated,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(relativePaths);
        ArgumentNullException.ThrowIfNull(transform);

        var codec = ResolveSourceEncoding(sourceEncoding);
        var outcomes = new List<FileOutcome>(relativePaths.Count);
        var prepared = new List<PreparedWrite>();
        var totalMatches = 0;

        foreach (var relativePath in relativePaths)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var preparation = PrepareFile(relativePath, transform, codec, dryRun);
            totalMatches += preparation.MatchCount;
            outcomes.Add(preparation.Outcome);
            if (preparation.Write is not null)
            {
                prepared.Add(preparation.Write);
            }
        }

        if (expectedMatchCount.HasValue && totalMatches != expectedMatchCount.Value)
        {
            throw TextEditException.ExpectedMatchCountMismatch(expectedMatchCount.Value, totalMatches);
        }

        if (prepared.Count == 0 || dryRun)
        {
            return new BatchOutcome
            {
                BatchId = null,
                Files = outcomes,
                SkippedSymlinks = skippedSymlinks,
                Truncated = truncated,
            };
        }

        var batchId = WriteBatch(tool, argsSummary, prepared, out var writeFailed);
        var files = writeFailed.Count == 0
            ? outcomes
            : outcomes
                .Select(outcome => writeFailed.Contains(outcome.Path)
                    ? outcome with { Changed = false, Reason = RefusalReason.WriteFailed, Diff = null }
                    : outcome)
                .ToList();

        return new BatchOutcome
        {
            BatchId = batchId,
            Files = files,
            SkippedSymlinks = skippedSymlinks,
            Truncated = truncated,
        };
    }

    private long WriteBatch(string tool, string argsSummary, List<PreparedWrite> prepared, out HashSet<string> writeFailed)
    {
        var pending = new List<FileRecord>(prepared.Count);
        foreach (var write in prepared)
        {
            var blobRef = _journal.PutPreImage(write.PreImage);
            pending.Add(new FileRecord
            {
                Path = write.RelativePath,
                PreHash = write.PreHash,
                BlobRef = blobRef,
                Encoding = write.Encoding,
                EncodingConfidence = write.EncodingConfidence,
                LadderStep = write.LadderStep,
                SourceEncodingSupplied = write.SourceEncodingSupplied,
                Outcome = JournalOutcome.Pending,
            });
        }

        var batchId = _journal.BeginBatch(tool, _rootName, argsSummary, pending);

        var postHashes = new Dictionary<string, string>(StringComparer.Ordinal);
        writeFailed = new HashSet<string>(StringComparer.Ordinal);
        foreach (var write in prepared)
        {
            try
            {
                AtomicWriter.ReplacePreservingMetadata(write.RealPath, write.NewBytes);
                postHashes[write.RelativePath] = Blake3.Hasher.Hash(write.NewBytes).ToString();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // The atomic write left the file unchanged (temp-then-rename), so its content still equals
                // the pre-image. Finalize the row with post_hash == pre_hash so undo hash-gates it: the
                // no-op restore succeeds while the file is untouched, but a later manual edit fails the gate
                // and is skipped rather than clobbered. Leaving the row pending would restore it
                // unconditionally. Report the file as refused to the caller.
                postHashes[write.RelativePath] = write.PreHash;
                writeFailed.Add(write.RelativePath);
            }
        }

        _journal.FinalizeChanged(batchId, postHashes);
        _journal.PruneRetention(_config.JournalRetentionBatches, _config.JournalRetentionHours);
        return batchId;
    }

    private Preparation PrepareFile(string relativePath, ITextTransform transform, SourceCodec codec, bool dryRun)
    {
        ConfinedPath confined;
        try
        {
            confined = _root.Confine(relativePath, "path");
        }
        catch (PathConfinementException)
        {
            return Refusal(relativePath, RefusalReason.OutOfRoot);
        }

        if (!confined.Exists)
        {
            return Refusal(confined.RelativePath, RefusalReason.NotFound);
        }

        if (_denylist.IsDeniedFile(confined.RelativePath))
        {
            return Refusal(confined.RelativePath, RefusalReason.Denied);
        }

        if (PathIgnoreEvaluator.IsIgnored(_root.CanonicalRoot, confined.RelativePath))
        {
            return Refusal(confined.RelativePath, RefusalReason.Ignored);
        }

        if (Directory.Exists(confined.RealPath))
        {
            return Refusal(confined.RelativePath, RefusalReason.IsDirectory);
        }

        byte[] bytes;
        try
        {
            bytes = ReadCapped(confined.RealPath, out var tooLarge);
            if (tooLarge)
            {
                return Refusal(confined.RelativePath, RefusalReason.TooLarge);
            }
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
        {
            return Refusal(confined.RelativePath, RefusalReason.NotFound);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            return Refusal(confined.RelativePath, RefusalReason.IoError);
        }

        var detected = _detector.Detect(bytes);
        if (detected.IsBinary)
        {
            return Refusal(confined.RelativePath, RefusalReason.Binary);
        }

        Encoding encoding;
        string encodingName;
        bool supplied;
        if (codec is not null)
        {
            encoding = codec.Encoding;
            encodingName = codec.Name;
            supplied = true;
        }
        else
        {
            if (detected.Confidence < _config.RewriteConfidence)
            {
                return Refusal(confined.RelativePath, RefusalReason.LowConfidenceEncoding);
            }

            encoding = detected.Encoding;
            encodingName = detected.Name;
            supplied = false;
        }

        var text = TextCodec.Decode(bytes, encoding, detected.HasBom);
        TransformResult result;
        try
        {
            result = transform.Transform(text);
        }
        catch (RegexMatchTimeoutException)
        {
            return Refusal(confined.RelativePath, RefusalReason.RegexTimeout);
        }

        var withBom = result.BomOverride ?? detected.HasBom;
        // The mark bytes are chosen from the canonical detected name, not encodingName: a caller-supplied
        // source_encoding alias (for example "utf-16") is not one of MarkFor's canonical keys and would
        // otherwise drop the BOM. encodingName still feeds the journal's Encoding field below.
        var newBytes = TextCodec.Encode(result.NewText, encoding, withBom, detected.Name);

        if (newBytes.AsSpan().SequenceEqual(bytes))
        {
            return new Preparation
            {
                Outcome = new FileOutcome { Path = confined.RelativePath, Changed = false },
                Write = null,
                MatchCount = result.MatchCount,
            };
        }

        if (newBytes.Length > _config.MaxFileBytes)
        {
            // The read is capped, but a transform can expand past the cap (for example lf -> crlf near the
            // limit). Refuse rather than write an oversized post-image: undo reads under the same cap and
            // would skip a file larger than it as a hash mismatch, leaving the change unrevertable.
            return Refusal(confined.RelativePath, RefusalReason.TooLarge);
        }

        var write = new PreparedWrite
        {
            RelativePath = confined.RelativePath,
            RealPath = confined.RealPath,
            PreImage = bytes,
            PreHash = Blake3.Hasher.Hash(bytes).ToString(),
            NewBytes = newBytes,
            Encoding = encodingName,
            EncodingConfidence = detected.Confidence,
            LadderStep = detected.Step.ToString(),
            SourceEncodingSupplied = supplied,
        };

        return new Preparation
        {
            Outcome = new FileOutcome
            {
                Path = confined.RelativePath,
                Changed = true,
                Diff = dryRun ? UnifiedDiff.Format(text, result.NewText, confined.RelativePath) : null,
            },
            Write = write,
            MatchCount = result.MatchCount,
        };
    }

    private byte[] ReadCapped(string realPath, out bool tooLarge)
    {
        tooLarge = false;
        using var stream = new FileStream(realPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        var length = stream.Length;
        if (length > _config.MaxFileBytes)
        {
            tooLarge = true;
            return null;
        }

        var bytes = new byte[length];
        stream.ReadExactly(bytes, 0, bytes.Length);
        return bytes;
    }

    private static SourceCodec ResolveSourceEncoding(string sourceEncoding)
    {
        if (string.IsNullOrWhiteSpace(sourceEncoding))
        {
            return null;
        }

        try
        {
            return new SourceCodec { Encoding = Encoding.GetEncoding(sourceEncoding), Name = sourceEncoding.ToLowerInvariant() };
        }
        catch (ArgumentException)
        {
            throw TextEditException.InvalidArgument($"unknown source encoding '{sourceEncoding}'");
        }
    }

    private static Preparation Refusal(string path, string reason)
        => new()
        {
            Outcome = new FileOutcome { Path = path, Changed = false, Reason = reason },
            Write = null,
            MatchCount = 0,
        };

    private sealed record SourceCodec
    {
        public Encoding Encoding { get; init; }

        public string Name { get; init; }
    }

    private sealed record PreparedWrite
    {
        public string RelativePath { get; init; }

        public string RealPath { get; init; }

        public byte[] PreImage { get; init; }

        public string PreHash { get; init; }

        public byte[] NewBytes { get; init; }

        public string Encoding { get; init; }

        public double EncodingConfidence { get; init; }

        public string LadderStep { get; init; }

        public bool SourceEncodingSupplied { get; init; }
    }

    private sealed record Preparation
    {
        public FileOutcome Outcome { get; init; }

        public PreparedWrite Write { get; init; }

        public int MatchCount { get; init; }
    }
}