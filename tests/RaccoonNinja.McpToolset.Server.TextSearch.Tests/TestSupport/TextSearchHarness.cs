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
    private readonly Dictionary<string, string> _packageDirs = new(StringComparer.OrdinalIgnoreCase);

    public TextSearchHarness(
        int? regexTimeoutMs = null,
        int? operationBudgetMs = null,
        string extraDeny = null,
        string defaultIgnore = null,
        IReadOnlyList<string> packageRoots = null,
        bool secretScan = true)
    {
        Root = NewTempDirectory("base");
        Config = DefaultConfig(regexTimeoutMs, operationBudgetMs, secretScan);
        var detector = new EncodingDetector();

        // Each package root is a fresh temp directory alongside (never under) the base, so overlap is
        // impossible; they are registered by an explicit name=path alias.
        string packageRootsValue = null;
        if (packageRoots is { Count: > 0 })
        {
            var entries = new List<string>(packageRoots.Count);
            foreach (var name in packageRoots)
            {
                var dir = NewTempDirectory($"pkg-{name}");
                _packageDirs[name] = dir;
                entries.Add($"{name}={dir}");
            }

            packageRootsValue = string.Join(';', entries);
        }

        Resolver = ScopeResolver.Create(Config, Root, defaultIgnore, extraDeny, packageRootsValue);
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

    /// <summary>The absolute temp directory backing the named package root (from the constructor's <c>packageRoots</c>).</summary>
    public string PackageDir(string name) => _packageDirs[name];

    /// <summary>Write a UTF-8 text file under the named package root.</summary>
    public void WritePackage(string name, string relativePath, string content)
    {
        var full = Path.Combine(_packageDirs[name], relativePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(full));
        File.WriteAllBytes(full, Encoding.UTF8.GetBytes(content));
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

    private static SearchConfig DefaultConfig(int? regexTimeoutMs, int? operationBudgetMs, bool secretScan)
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
            SecretScanEnabled = secretScan,
        };
}