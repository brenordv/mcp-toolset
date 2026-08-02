using RaccoonNinja.McpToolset.Files.Selection;
using RaccoonNinja.McpToolset.Server.TextEdit.Configuration;
using RaccoonNinja.McpToolset.Server.TextEdit.Envelope;
using RaccoonNinja.McpToolset.Server.TextEdit.Errors;

namespace RaccoonNinja.McpToolset.Server.TextEdit.Tools;

/// <summary>
/// Shared plumbing for the selector-driven mutation tools: build a validated <see cref="FileSelector"/> from
/// the wire arguments, run the single-root selection under a time budget, clamp the page size, and echo the
/// arguments safely. Ignore rules are never bypassed here (there is no <c>include_ignored</c> on the write
/// path), and every <c>.Files</c> exception is mapped to a typed, path-free error.
/// </summary>
internal static class SelectorSupport
{
    /// <summary>Build a validated selector over the scope; ignore rules always apply (the write path never bypasses them).</summary>
    /// <param name="glob">The glob pattern, or null.</param>
    /// <param name="regex">The regex, or null.</param>
    /// <param name="paths">The explicit paths, or null.</param>
    /// <param name="extensions">The extension filter, or null.</param>
    /// <param name="caseSensitive">Whether matching is case-sensitive.</param>
    /// <param name="maxFiles">The clamped result cap.</param>
    /// <returns>The validated selector.</returns>
    /// <exception cref="TextEditException">Thrown (as <c>SelectorInvalid</c>) when more than one of glob, regex, paths is given.</exception>
    public static FileSelector Build(
        string glob,
        string regex,
        string[] paths,
        string[] extensions,
        bool caseSensitive,
        int maxFiles)
    {
        try
        {
            // The effective root is the resolved scope, so the selector's own root is always null; a
            // subdirectory is scoped through the cwd, not here.
            return FileSelector.Create(
                root: null,
                glob: glob,
                regex: regex,
                paths: paths,
                caseSensitive: caseSensitive,
                maxFiles: maxFiles,
                extensions: extensions);
        }
        catch (SelectorException ex)
        {
            throw TextEditException.SelectorInvalid(ex.Message);
        }
    }

    /// <summary>Run the selection, mapping <c>.Files</c> failures to typed, path-free errors.</summary>
    /// <param name="selection">The root's selection service.</param>
    /// <param name="selector">The validated selector.</param>
    /// <param name="config">The server config (regex timeout and operation budget).</param>
    /// <param name="budgetToken">A token cancelled at the operation deadline, bounding the walk by time.</param>
    /// <returns>The walk result.</returns>
    /// <exception cref="TextEditException">Thrown for a bad regex, or (as <c>OperationBudgetExceeded</c>) a timed-out walk.</exception>
    public static WalkResult Run(FileSelection selection, FileSelector selector, EditConfig config, CancellationToken budgetToken)
    {
        try
        {
            return selection.Select(selector, new SafeRegexOptions { MatchTimeout = config.RegexTimeout }, budgetToken);
        }
        catch (RegexCompilationException ex)
        {
            throw TextEditException.PatternInvalid(ex.Message);
        }
        catch (OperationCanceledException)
        {
            throw TextEditException.OperationBudgetExceeded((int)config.OperationBudget.TotalMilliseconds);
        }
    }

    /// <summary>Clamp the requested page size to <c>[1, ceiling]</c>, defaulting when unset.</summary>
    /// <param name="config">The server config.</param>
    /// <param name="maxFiles">The requested page size; zero or negative means the default.</param>
    /// <returns>The effective page size.</returns>
    public static int PageSize(EditConfig config, int maxFiles)
        => Math.Clamp(maxFiles <= 0 ? config.MaxFilesDefault : maxFiles, 1, config.MaxFilesCeiling);

    /// <summary>Create a linked token source that trips at the operation deadline (and on client cancellation).</summary>
    /// <param name="config">The server config (supplies the operation budget).</param>
    /// <param name="cancellationToken">The client cancellation token to link in.</param>
    /// <returns>The budget token source; the caller disposes it.</returns>
    public static CancellationTokenSource CreateBudget(EditConfig config, CancellationToken cancellationToken)
    {
        var source = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        if (config.OperationBudget <= TimeSpan.Zero)
        {
            source.Cancel();
        }
        else
        {
            source.CancelAfter(config.OperationBudget);
        }

        return source;
    }

    /// <summary>Build the safe echo of the selector arguments (user strings redacted, scope key and counts kept).</summary>
    /// <param name="scopeKey">The base-relative scope key (never the absolute or raw <c>cwd</c>).</param>
    /// <param name="glob">The glob argument.</param>
    /// <param name="regex">The regex argument.</param>
    /// <param name="paths">The paths argument.</param>
    /// <param name="extensions">The extensions argument.</param>
    /// <param name="caseSensitive">The case-sensitive flag.</param>
    /// <param name="dryRun">The dry-run flag.</param>
    /// <returns>The filters builder.</returns>
    public static FiltersAppliedBuilder Filters(
        string scopeKey,
        string glob,
        string regex,
        string[] paths,
        string[] extensions,
        bool caseSensitive,
        bool dryRun)
        => FiltersAppliedBuilder.Create()
            .Value("cwd", scopeKey)
            .Redact("glob", glob)
            .Redact("regex", regex)
            .Count("paths", paths?.Length ?? 0)
            .Count("extensions", extensions?.Length ?? 0)
            .Flag("case_sensitive", caseSensitive)
            .Flag("dry_run", dryRun);
}