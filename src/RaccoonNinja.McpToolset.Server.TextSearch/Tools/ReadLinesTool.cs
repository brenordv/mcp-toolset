using System.ComponentModel;
using ModelContextProtocol.Server;
using RaccoonNinja.McpToolset.Files.Text;
using RaccoonNinja.McpToolset.Server.TextSearch.Configuration;
using RaccoonNinja.McpToolset.Server.TextSearch.Content;
using RaccoonNinja.McpToolset.Server.TextSearch.Envelope;
using RaccoonNinja.McpToolset.Server.TextSearch.Errors;
using RaccoonNinja.McpToolset.Server.TextSearch.Models;

namespace RaccoonNinja.McpToolset.Server.TextSearch.Tools;

/// <summary>The <c>read_lines</c> tool: return a numbered, span-capped slice of one text file in one root.</summary>
[McpServerToolType]
public sealed class ReadLinesTool(ToolCommon common, SearchConfig config, RootRegistry registry, IEncodingDetector detector)
{
    /// <summary>Return lines <paramref name="start_line"/> through <paramref name="end_line"/> of a file in one root.</summary>
    /// <param name="path">The root-relative file path.</param>
    /// <param name="root">The root the file is in; omit only when a single root is configured.</param>
    /// <param name="start_line">The first line (1-based).</param>
    /// <param name="end_line">The last line (1-based); 0 reads a full span from the start.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>An envelope of numbered lines.</returns>
    [McpServerTool(Name = "read_lines", ReadOnly = true, Idempotent = true, OpenWorld = false)]
    [Description(
        "Return a numbered slice of one text file, from start_line to end_line (both 1-based, inclusive). "
        + "The file lives in exactly one root: give its `root` name (omit only when a single root is "
        + "configured; a group like \"@all\" is not valid here). Leave end_line at 0 to read a capped span "
        + "from start_line. Binary files are refused; very long lines are truncated.")]
    public Task<ResultEnvelope> InvokeAsync(
        [Description("The root-relative path of the file to read.")]
        string path,
        [Description("The name of the root the file is in; omit only when a single root is configured.")]
        string root = null,
        [Description("The first line to return (1-based). Default 1.")]
        int start_line = 1,
        [Description("The last line to return (1-based, inclusive). 0 (default) reads a capped span from start_line.")]
        int end_line = 0,
        CancellationToken cancellationToken = default)
    {
        var ctx = common.MakeContext("read_lines");
        return common.WrapAsync(ctx, () =>
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                throw TextSearchException.InvalidArgument("path must not be empty");
            }

            if (start_line < 1)
            {
                throw TextSearchException.InvalidArgument("start_line must be 1 or greater");
            }

            if (end_line != 0 && end_line < start_line)
            {
                throw TextSearchException.InvalidArgument("end_line must be 0 or at least start_line");
            }

            var spec = ResolveSingleRoot(root);
            if (spec.Kind == RootKind.Package)
            {
                common.PackageTargeted(ctx);
            }

            var document = LoadOrRefuse(ctx, spec.Reader, path);
            var lineCount = document.LineCount;

            // Compute in long so a near-int.MaxValue start_line cannot overflow the span arithmetic.
            var spanCap = (long)start_line + config.MaxLineSpan - 1;
            var requestedEnd = end_line == 0 ? spanCap : end_line;
            var end = (int)Math.Min(Math.Min(requestedEnd, spanCap), lineCount);

            var lines = new List<object>();
            for (var number = start_line; number <= end; number++)
            {
                lines.Add(new NumberedLine(number, Cap(document.Lines[number - 1].Content)));
            }

            var filters = FiltersAppliedBuilder.Create()
                .Value("root", spec.Name)
                .Redact("path", path)
                .Number("start_line", start_line)
                .Number("end_line", end)
                .Build();

            var truncated = end < lineCount;
            return Task.FromResult(ToolCommon.ListSuccess(lines, truncated: truncated, filtersApplied: filters));
        });
    }

    private RootSpec ResolveSingleRoot(string root)
    {
        if (!string.IsNullOrWhiteSpace(root))
        {
            if (root.Equals(RootRegistry.PackagesTarget, StringComparison.OrdinalIgnoreCase)
                || root.Equals(RootRegistry.AllTarget, StringComparison.OrdinalIgnoreCase))
            {
                throw TextSearchException.InvalidArgument("read_lines targets a single file; give a specific root name, not a group");
            }

            return registry.Resolve(root)[0];
        }

        var workspace = registry.Resolve(null);
        return workspace.Count == 1
            ? workspace[0]
            : throw TextSearchException.InvalidArgument("more than one root is configured; specify the file's root");
    }

    private TextDocument LoadOrRefuse(CallContext ctx, GatedFileReader reader, string path)
    {
        var read = reader.Read(path);
        if (!read.IsOk)
        {
            common.Refusal(ctx, RefusalReason.From(read.Status));

            // Denied/out-of-root/io are reported as "not found" so a single-path read is not an
            // existence oracle for a secret; a real size overflow is reported honestly.
            throw read.Status == ReadStatus.TooLarge
                ? new TextSearchException(ErrorCodes.TooLarge, "file is larger than the configured read limit")
                : new TextSearchException(ErrorCodes.NotFound, "file not found");
        }

        var document = TextDocument.Load(read.Bytes, detector);
        return document.IsBinary
            ? throw new TextSearchException(ErrorCodes.IsBinary, "file is binary and cannot be read as text")
            : document;
    }

    private static string Cap(string text)
        => text.Length <= ContentSearch.MaxEmittedLineLength ? text : text[..ContentSearch.MaxEmittedLineLength];
}