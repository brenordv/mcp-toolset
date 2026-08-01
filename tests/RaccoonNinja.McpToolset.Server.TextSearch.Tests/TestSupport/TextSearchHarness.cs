using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using RaccoonNinja.McpToolset.Files.Text;
using RaccoonNinja.McpToolset.Server.TextSearch.Configuration;
using RaccoonNinja.McpToolset.Server.TextSearch.Envelope;
using RaccoonNinja.McpToolset.Server.TextSearch.Metrics;
using RaccoonNinja.McpToolset.Server.TextSearch.Models;
using RaccoonNinja.McpToolset.Server.TextSearch.Tools;

namespace RaccoonNinja.McpToolset.Server.TextSearch.Tests.TestSupport;

/// <summary>
/// A disposable text-search server over one fresh temp base root, with all five tools wired to real
/// collaborators through a real <see cref="ScopeResolver"/> (built env-free). Tools are exercised
/// in-process: write files under the base root (or a subdirectory), optionally pass a <c>cwd</c>, call
/// <c>InvokeAsync</c>, assert on the envelope.
/// </summary>
internal sealed class TextSearchHarness : IDisposable
{
    private readonly List<string> _cleanup = [];

    public TextSearchHarness(
        int? regexTimeoutMs = null,
        int? operationBudgetMs = null,
        string extraDeny = null,
        string defaultIgnore = null)
    {
        Root = NewTempDirectory("base");
        Config = DefaultConfig(regexTimeoutMs, operationBudgetMs);
        var detector = new EncodingDetector();
        Resolver = ScopeResolver.Create(Config, Root, defaultIgnore, extraDeny);
        Metrics = new SessionMetrics();
        var common = new ToolCommon(Metrics, NullLoggerFactory.Instance);

        Describe = new DescribeScopeTool(common, Config, Resolver);
        Find = new FindFilesTool(common, Config, Resolver);
        Inspect = new InspectFilesTool(common, Config, Resolver, detector);
        Search = new SearchTextTool(common, Config, Resolver, detector);
        ReadLines = new ReadLinesTool(common, Config, Resolver, detector);
    }

    /// <summary>The absolute temp directory backing the base root.</summary>
    public string Root { get; }

    public SearchConfig Config { get; }

    public ScopeResolver Resolver { get; }

    public SessionMetrics Metrics { get; }

    public DescribeScopeTool Describe { get; }

    public FindFilesTool Find { get; }

    public InspectFilesTool Inspect { get; }

    public SearchTextTool Search { get; }

    public ReadLinesTool ReadLines { get; }

    /// <summary>The absolute path of a subdirectory under the base root, created if needed. Pass it as a <c>cwd</c>.</summary>
    public string Dir(string relativePath)
    {
        var full = Path.Combine(Root, relativePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(full);
        return full;
    }

    /// <summary>Write a UTF-8 text file under the base root.</summary>
    public void Write(string relativePath, string content)
        => WriteBytes(relativePath, Encoding.UTF8.GetBytes(content));

    /// <summary>Write raw bytes under the base root.</summary>
    public void WriteBytes(string relativePath, byte[] content)
    {
        var full = Path.Combine(Root, relativePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(full));
        File.WriteAllBytes(full, content);
    }

    /// <summary>The scope-relative paths of every result item exposing a <c>Path</c> property.</summary>
    public static string[] Paths(ResultEnvelope envelope)
        => envelope.Results.Select(item => ((IHasPath)item).Path).ToArray();

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