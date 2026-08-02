using RaccoonNinja.McpToolset.Files.Selection;
using RaccoonNinja.McpToolset.Server.TextSearch.Configuration;
using RaccoonNinja.McpToolset.Server.TextSearch.Envelope;
using RaccoonNinja.McpToolset.Server.TextSearch.Errors;

namespace RaccoonNinja.McpToolset.Server.TextSearch.Tools;

/// <summary>
/// Shared plumbing for the selector-driven tools: build a validated <see cref="FileSelector"/> from the
/// wire arguments (the effective root is the resolved scope, so the selector's own root is always null and
/// a subdirectory is scoped with a glob), run the scoped selection, clamp the page size, bound the
/// operation, and echo arguments safely. It maps the <c>.Files</c> exceptions to the server's typed errors,
/// always with a static, path-free message.
/// </summary>
internal static class SelectorSupport
{
    /// <summary>Build a validated selector over the scope; the include-ignored globs are compiled here.</summary>
    /// <param name="config">The server config.</param>
    /// <param name="glob">The glob pattern, or null.</param>
    /// <param name="regex">The regex, or null.</param>
    /// <param name="paths">The explicit paths, or null.</param>
    /// <param name="extensions">The extension filter, or null.</param>
    /// <param name="includeIgnored">The globs re-including otherwise-ignored paths, or null/empty for none.</param>
    /// <param name="caseSensitive">Whether matching is case-sensitive.</param>
    /// <returns>The selector.</returns>
    /// <exception cref="TextSearchException">
    /// Thrown (as <c>PatternInvalid</c>) when an include-ignored glob is malformed, or (as <c>SelectorInvalid</c>)
    /// when more than one selector is given.
    /// </exception>
    public static FileSelector Build(
        SearchConfig config,
        string glob,
        string regex,
        string[] paths,
        string[] extensions,
        string[] includeIgnored,
        bool caseSensitive)
    {
        IncludeGlobSet includeSet;
        try
        {
            includeSet = IncludeGlobSet.Compile(includeIgnored);
        }
        catch (RegexCompilationException ex)
        {
            throw TextSearchException.PatternInvalid(ex.Message);
        }

        try
        {
            return FileSelector.Create(
                root: null,
                glob: glob,
                regex: regex,
                paths: paths,
                includeIgnored: includeSet,
                caseSensitive: caseSensitive,
                maxFiles: config.MaxFilesCeiling,
                extensions: extensions);
        }
        catch (SelectorException ex)
        {
            throw TextSearchException.SelectorInvalid(ex.Message);
        }
    }

    /// <summary>Run the scope's selection, mapping <c>.Files</c> failures to typed, path-free errors.</summary>
    /// <param name="selection">The scope's selection service.</param>
    /// <param name="selector">The validated selector.</param>
    /// <param name="config">The server config (regex timeout and operation budget).</param>
    /// <param name="budgetToken">A token cancelled at the operation deadline, bounding the walk by time.</param>
    /// <returns>The walk result (ordinal-sorted, capped at the ceiling).</returns>
    /// <exception cref="TextSearchException">Thrown for a bad regex, or (as <c>OperationBudgetExceeded</c>) a timed-out walk.</exception>
    public static WalkResult Run(FileSelection selection, FileSelector selector, SearchConfig config, CancellationToken budgetToken)
    {
        try
        {
            return selection.Select(selector, new SafeRegexOptions { MatchTimeout = config.RegexTimeout }, budgetToken);
        }
        catch (RegexCompilationException ex)
        {
            throw TextSearchException.PatternInvalid(ex.Message);
        }
        catch (OperationCanceledException)
        {
            throw TextSearchException.OperationBudgetExceeded((int)config.OperationBudget.TotalMilliseconds);
        }
    }

    /// <summary>Clamp the requested page size to <c>[1, ceiling]</c>, defaulting when unset.</summary>
    /// <param name="config">The server config.</param>
    /// <param name="maxFiles">The requested page size; zero or negative means the default.</param>
    /// <returns>The effective page size.</returns>
    public static int PageSize(SearchConfig config, int maxFiles)
        => Math.Clamp(maxFiles <= 0 ? config.MaxFilesDefault : maxFiles, 1, config.MaxFilesCeiling);

    /// <summary>Create a linked token source that trips at the operation deadline (and on client cancellation).</summary>
    /// <param name="config">The server config (supplies the operation budget).</param>
    /// <param name="cancellationToken">The client cancellation token to link in.</param>
    /// <returns>The budget token source; the caller disposes it.</returns>
    public static CancellationTokenSource CreateBudget(SearchConfig config, CancellationToken cancellationToken)
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

    /// <summary>Throw the typed budget error if the operation deadline has passed.</summary>
    /// <param name="config">The server config.</param>
    /// <param name="budgetToken">The token cancelled at the operation deadline (links in client cancellation).</param>
    /// <exception cref="TextSearchException">Thrown (as <c>OperationBudgetExceeded</c>) past the budget.</exception>
    public static void CheckBudget(SearchConfig config, CancellationToken budgetToken)
    {
        if (budgetToken.IsCancellationRequested)
        {
            throw TextSearchException.OperationBudgetExceeded((int)config.OperationBudget.TotalMilliseconds);
        }
    }

    /// <summary>Build the safe echo of the selector arguments (user strings redacted, scope key and counts kept).</summary>
    /// <param name="scopeKey">The base-relative scope key (never the absolute <c>cwd</c>).</param>
    /// <param name="glob">The glob argument.</param>
    /// <param name="regex">The regex argument.</param>
    /// <param name="paths">The paths argument.</param>
    /// <param name="extensions">The extensions argument.</param>
    /// <param name="includeIgnored">The include-ignored globs argument (echoed as a count).</param>
    /// <param name="caseSensitive">The case-sensitive flag.</param>
    /// <returns>The filters builder.</returns>
    public static FiltersAppliedBuilder Filters(
        string scopeKey,
        string glob,
        string regex,
        string[] paths,
        string[] extensions,
        string[] includeIgnored,
        bool caseSensitive)
        => FiltersAppliedBuilder.Create()
            .Value("cwd", scopeKey)
            .Redact("glob", glob)
            .Redact("regex", regex)
            .Count("paths", paths?.Length ?? 0)
            .Count("extensions", extensions?.Length ?? 0)
            .Count("include_ignored", includeIgnored?.Length ?? 0)
            .Flag("case_sensitive", caseSensitive);
}