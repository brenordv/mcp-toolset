using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using RaccoonNinja.McpToolset.Files.Security;
using RaccoonNinja.McpToolset.Files.Text;
using RaccoonNinja.McpToolset.Server.TextSearch.Configuration;
using RaccoonNinja.McpToolset.Server.TextSearch.Envelope;
using RaccoonNinja.McpToolset.Server.TextSearch.Metrics;
using RaccoonNinja.McpToolset.Server.TextSearch.Tools;

namespace RaccoonNinja.McpToolset.Server.TextSearch.Tests.TestSupport;

/// <summary>
/// A disposable text-search server over one or more fresh temp roots, with all five tools wired to real
/// collaborators through a real <see cref="RootRegistry"/> (built env-free). Tools are exercised
/// in-process: write files into a named root, call <c>InvokeAsync</c>, assert on the envelope.
/// </summary>
internal sealed class TextSearchHarness : IDisposable
{
    private readonly List<string> _cleanup = [];
    private readonly Dictionary<string, string> _dirs = new(StringComparer.OrdinalIgnoreCase);

    public TextSearchHarness(int? regexTimeoutMs = null, int? operationBudgetMs = null)
        : this([("root", RootKind.Workspace)], regexTimeoutMs, operationBudgetMs)
    {
    }

    public TextSearchHarness(
        IReadOnlyList<(string Name, RootKind Kind)> roots,
        int? regexTimeoutMs = null,
        int? operationBudgetMs = null)
    {
        var workspace = new List<string>();
        var package = new List<string>();
        foreach (var (name, kind) in roots)
        {
            var dir = NewTempDirectory(name);
            _dirs[name] = dir;
            (kind == RootKind.Workspace ? workspace : package).Add($"{name}={dir}");
        }

        DefaultRoot = roots.First(root => root.Kind == RootKind.Workspace).Name;
        Config = DefaultConfig(regexTimeoutMs, operationBudgetMs);
        var denylist = new SecretDenylist();
        var detector = new EncodingDetector();
        Registry = RootRegistry.Create(
            Config,
            denylist,
            string.Join(';', workspace),
            package.Count > 0 ? string.Join(';', package) : null);
        Metrics = new SessionMetrics();
        var common = new ToolCommon(Metrics, NullLoggerFactory.Instance);

        Describe = new DescribeScopeTool(common, Config, Registry, denylist);
        Find = new FindFilesTool(common, Config, Registry);
        Inspect = new InspectFilesTool(common, Config, Registry, detector);
        Search = new SearchTextTool(common, Config, Registry, detector);
        ReadLines = new ReadLinesTool(common, Config, Registry, detector);
    }

    public string DefaultRoot { get; }

    public string Root => _dirs[DefaultRoot];

    public SearchConfig Config { get; }

    public RootRegistry Registry { get; }

    public SessionMetrics Metrics { get; }

    public DescribeScopeTool Describe { get; }

    public FindFilesTool Find { get; }

    public InspectFilesTool Inspect { get; }

    public SearchTextTool Search { get; }

    public ReadLinesTool ReadLines { get; }

    /// <summary>The absolute temp directory backing a named root.</summary>
    public string RootDir(string root) => _dirs[root];

    /// <summary>Write a UTF-8 text file into the default root.</summary>
    public void Write(string relativePath, string content)
        => WriteBytes(DefaultRoot, relativePath, Encoding.UTF8.GetBytes(content));

    /// <summary>Write a UTF-8 text file into a named root.</summary>
    public void Write(string root, string relativePath, string content)
        => WriteBytes(root, relativePath, Encoding.UTF8.GetBytes(content));

    /// <summary>Write raw bytes into the default root.</summary>
    public void WriteBytes(string relativePath, byte[] content)
        => WriteBytes(DefaultRoot, relativePath, content);

    /// <summary>Write raw bytes into a named root.</summary>
    public void WriteBytes(string root, string relativePath, byte[] content)
    {
        var full = Path.Combine(_dirs[root], relativePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(full));
        File.WriteAllBytes(full, content);
    }

    /// <summary>The root-relative paths of every result item exposing a <c>Path</c> property.</summary>
    public static string[] Paths(ResultEnvelope envelope)
        => envelope.Results.Select(item => (string)item.GetType().GetProperty("Path").GetValue(item)).ToArray();

    /// <summary>The root names of every result item exposing a <c>Root</c> property.</summary>
    public static string[] Roots(ResultEnvelope envelope)
        => envelope.Results.Select(item => (string)item.GetType().GetProperty("Root").GetValue(item)).ToArray();

    /// <summary>Serialize an envelope to JSON, the way the SDK sends it to the client.</summary>
    public static string ToJson(ResultEnvelope envelope)
        => JsonSerializer.Serialize(envelope);

    public void Dispose()
    {
        foreach (var dir in _cleanup)
        {
            try
            {
                Directory.Delete(dir, recursive: true);
            }
            catch (IOException)
            {
                // Best-effort cleanup.
            }
            catch (UnauthorizedAccessException)
            {
                // Best-effort cleanup.
            }
        }
    }

    private string NewTempDirectory(string label)
    {
        var dir = Path.Combine(Path.GetTempPath(), $"rnmcp-ts-{label}-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        _cleanup.Add(dir);
        return dir;
    }

    private static SearchConfig DefaultConfig(int? regexTimeoutMs, int? operationBudgetMs)
        => new()
        {
            MaxFilesDefault = SearchConfig.DefaultMaxFiles,
            MaxFilesCeiling = SearchConfig.DefaultMaxFilesCeiling,
            MaxFileBytes = SearchConfig.DefaultMaxFileBytes,
            MaxResults = SearchConfig.DefaultMaxResults,
            MaxMatchesPerFile = SearchConfig.DefaultMaxMatchesPerFile,
            MaxContextLines = SearchConfig.DefaultMaxContextLines,
            MaxLineSpan = SearchConfig.DefaultMaxLineSpan,
            RegexTimeout = TimeSpan.FromMilliseconds(regexTimeoutMs ?? SearchConfig.DefaultRegexTimeoutMs),
            OperationBudget = TimeSpan.FromMilliseconds(operationBudgetMs ?? SearchConfig.DefaultOperationBudgetMs),
        };
}