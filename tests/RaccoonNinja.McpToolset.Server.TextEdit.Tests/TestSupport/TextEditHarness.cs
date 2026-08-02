using Microsoft.Extensions.Logging.Abstractions;
using RaccoonNinja.McpToolset.Files.Security;
using RaccoonNinja.McpToolset.Files.Text;
using RaccoonNinja.McpToolset.Server.TextEdit.Configuration;
using RaccoonNinja.McpToolset.Server.TextEdit.Content;
using RaccoonNinja.McpToolset.Server.TextEdit.Journal;
using RaccoonNinja.McpToolset.Server.TextEdit.Metrics;
using RaccoonNinja.McpToolset.Server.TextEdit.Tools;

namespace RaccoonNinja.McpToolset.Server.TextEdit.Tests.TestSupport;

/// <summary>
/// A self-contained test rig over a temp base root and a temp journal directory: the real confiner,
/// denylist, encoding detector, journal, write gate, undoer, scope resolver, and the tools, wired the way
/// the server wires them. Tests either drive the write gate and undoer directly, or invoke the tools with a
/// <c>cwd</c>. There is no protocol harness and no mocking.
/// </summary>
public sealed class TextEditHarness : IDisposable
{
    private readonly string _appData;

    public TextEditHarness(EditConfig config = null, string defaultIgnore = null, string extraDeny = null)
    {
        Root = NewTempDir("root");
        _appData = NewTempDir("appdata");
        Config = config ?? DefaultConfig();
        Resolver = ScopeResolver.Create(Root, defaultIgnore, extraDeny);
        Confinement = Resolver.BaseConfinement;
        Denylist = Resolver.Denylist;
        var paths = JournalPaths.Resolve(Confinement, _appData);
        Journal = new JournalStore(paths);
        Journal.EnsureSchema();
        Writer = new GatedFileWriter(Confinement, Denylist, new EncodingDetector(), Journal, Config, Resolver.BaseRootName);
        Undoer = new Undoer(Confinement, Denylist, Journal, Config.MaxFileBytes);

        Metrics = new SessionMetrics();
        var common = new ToolCommon(Metrics, NullLoggerFactory.Instance);
        Replace = new ReplaceTextTool(common, Config, Resolver, Writer);
        Normalize = new NormalizeFilesTool(common, Config, Resolver, Writer);
        Describe = new DescribeScopeTool(common, Config, Resolver);
    }

    public string Root { get; }

    public EditConfig Config { get; }

    public ScopeResolver Resolver { get; }

    public RootConfinement Confinement { get; }

    public SecretDenylist Denylist { get; }

    public JournalStore Journal { get; }

    public GatedFileWriter Writer { get; }

    public Undoer Undoer { get; }

    public SessionMetrics Metrics { get; }

    public ReplaceTextTool Replace { get; }

    public NormalizeFilesTool Normalize { get; }

    public DescribeScopeTool Describe { get; }

    /// <summary>The default config with no environment overrides.</summary>
    /// <returns>The default configuration.</returns>
    public static EditConfig DefaultConfig()
        => new()
        {
            MaxFilesDefault = EditConfig.DefaultMaxFiles,
            MaxFilesCeiling = EditConfig.DefaultMaxFilesCeiling,
            MaxFileBytes = EditConfig.DefaultMaxFileBytes,
            RegexTimeout = TimeSpan.FromMilliseconds(EditConfig.DefaultRegexTimeoutMs),
            OperationBudget = TimeSpan.FromMilliseconds(EditConfig.DefaultOperationBudgetMs),
            RewriteConfidence = EditConfig.DefaultRewriteConfidence,
            PatternLengthCap = EditConfig.DefaultPatternLengthCap,
            JournalRetentionBatches = EditConfig.DefaultJournalRetentionBatches,
            JournalRetentionHours = EditConfig.DefaultJournalRetentionHours,
        };

    /// <summary>Apply a transform to the given base-relative paths over the whole base (no dry run, auto-detect).</summary>
    /// <param name="tool">The tool name to record.</param>
    /// <param name="transform">The transform to apply.</param>
    /// <param name="paths">The base-relative paths to feed the write gate directly.</param>
    /// <returns>The batch outcome.</returns>
    public BatchOutcome Apply(string tool, ITextTransform transform, params string[] paths)
        => Writer.Apply(tool, paths, transform, "test", expectedMatchCount: null, dryRun: false, sourceEncoding: null, skippedSymlinks: 0, truncated: false, Confinement, CancellationToken.None);

    /// <summary>The absolute path of a subdirectory under the base root, created if needed. Pass it as a <c>cwd</c>.</summary>
    /// <param name="relativePath">The base-relative subdirectory path.</param>
    /// <returns>The absolute path of the created subdirectory.</returns>
    public string Dir(string relativePath)
    {
        var full = Full(relativePath);
        Directory.CreateDirectory(full);
        return full;
    }

    /// <summary>Write UTF-8 (no BOM) text to a base-relative path, creating parents.</summary>
    /// <param name="relativePath">The base-relative path.</param>
    /// <param name="text">The text to write.</param>
    public void WriteText(string relativePath, string text)
        => WriteBytes(relativePath, System.Text.Encoding.UTF8.GetBytes(text));

    /// <summary>Write raw bytes to a base-relative path, creating parents.</summary>
    /// <param name="relativePath">The base-relative path.</param>
    /// <param name="bytes">The bytes to write.</param>
    public void WriteBytes(string relativePath, byte[] bytes)
    {
        var full = Full(relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(full));
        File.WriteAllBytes(full, bytes);
    }

    /// <summary>Read the raw bytes of a base-relative path.</summary>
    /// <param name="relativePath">The base-relative path.</param>
    /// <returns>The file bytes.</returns>
    public byte[] ReadBytes(string relativePath) => File.ReadAllBytes(Full(relativePath));

    /// <summary>Read the UTF-8 text of a base-relative path.</summary>
    /// <param name="relativePath">The base-relative path.</param>
    /// <returns>The file text.</returns>
    public string ReadText(string relativePath) => File.ReadAllText(Full(relativePath));

    /// <summary>Delete a base-relative file.</summary>
    /// <param name="relativePath">The base-relative path.</param>
    public void Delete(string relativePath) => File.Delete(Full(relativePath));

    /// <summary>Whether a base-relative file exists.</summary>
    /// <param name="relativePath">The base-relative path.</param>
    /// <returns><c>true</c> when the file exists.</returns>
    public bool Exists(string relativePath) => File.Exists(Full(relativePath));

    public void Dispose()
    {
        SafeDelete(Root);
        SafeDelete(_appData);
    }

    private string Full(string relativePath)
        => Path.Combine(Root, relativePath.Replace('/', Path.DirectorySeparatorChar));

    private static string NewTempDir(string label)
    {
        var dir = Path.Combine(Path.GetTempPath(), $"rnmcp-te-{label}-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static void SafeDelete(string dir)
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