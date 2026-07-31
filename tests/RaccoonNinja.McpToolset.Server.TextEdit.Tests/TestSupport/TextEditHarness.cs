using RaccoonNinja.McpToolset.Files.Security;
using RaccoonNinja.McpToolset.Files.Text;
using RaccoonNinja.McpToolset.Server.TextEdit.Configuration;
using RaccoonNinja.McpToolset.Server.TextEdit.Content;
using RaccoonNinja.McpToolset.Server.TextEdit.Journal;

namespace RaccoonNinja.McpToolset.Server.TextEdit.Tests.TestSupport;

/// <summary>
/// A self-contained test rig over a temp root and a temp journal directory: the real confiner, denylist,
/// encoding detector, journal, write gate, and undoer, wired the way the server wires them. Tests drive the
/// write gate and undoer directly with real collaborators; there is no protocol harness and no mocking.
/// </summary>
public sealed class TextEditHarness : IDisposable
{
    private readonly string _appData;

    public TextEditHarness(EditConfig config = null)
    {
        Root = NewTempDir("root");
        _appData = NewTempDir("appdata");
        Config = config ?? DefaultConfig();
        Confinement = new RootConfinement(Root);
        Denylist = new SecretDenylist();
        var paths = JournalPaths.Resolve(Confinement, _appData);
        Journal = new JournalStore(paths);
        Journal.EnsureSchema();
        Writer = new GatedFileWriter(Confinement, Denylist, new EncodingDetector(), Journal, Config, "root");
        Undoer = new Undoer(Confinement, Denylist, Journal, Config.MaxFileBytes);
    }

    public string Root { get; }

    public EditConfig Config { get; }

    public RootConfinement Confinement { get; }

    public SecretDenylist Denylist { get; }

    public JournalStore Journal { get; }

    public GatedFileWriter Writer { get; }

    public Undoer Undoer { get; }

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

    /// <summary>Apply a transform to the given root-relative paths with default options (no dry run, auto-detect).</summary>
    /// <param name="tool">The tool name to record.</param>
    /// <param name="transform">The transform to apply.</param>
    /// <param name="paths">The root-relative paths to feed the write gate directly.</param>
    /// <returns>The batch outcome.</returns>
    public BatchOutcome Apply(string tool, ITextTransform transform, params string[] paths)
        => Writer.Apply(tool, paths, transform, "test", expectedMatchCount: null, dryRun: false, sourceEncoding: null, skippedSymlinks: 0, truncated: false, CancellationToken.None);

    /// <summary>Write UTF-8 (no BOM) text to a root-relative path, creating parents.</summary>
    /// <param name="relativePath">The root-relative path.</param>
    /// <param name="text">The text to write.</param>
    public void WriteText(string relativePath, string text)
        => WriteBytes(relativePath, System.Text.Encoding.UTF8.GetBytes(text));

    /// <summary>Write raw bytes to a root-relative path, creating parents.</summary>
    /// <param name="relativePath">The root-relative path.</param>
    /// <param name="bytes">The bytes to write.</param>
    public void WriteBytes(string relativePath, byte[] bytes)
    {
        var full = Full(relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(full));
        File.WriteAllBytes(full, bytes);
    }

    /// <summary>Read the raw bytes of a root-relative path.</summary>
    /// <param name="relativePath">The root-relative path.</param>
    /// <returns>The file bytes.</returns>
    public byte[] ReadBytes(string relativePath) => File.ReadAllBytes(Full(relativePath));

    /// <summary>Read the UTF-8 text of a root-relative path.</summary>
    /// <param name="relativePath">The root-relative path.</param>
    /// <returns>The file text.</returns>
    public string ReadText(string relativePath) => File.ReadAllText(Full(relativePath));

    /// <summary>Delete a root-relative file.</summary>
    /// <param name="relativePath">The root-relative path.</param>
    public void Delete(string relativePath) => File.Delete(Full(relativePath));

    /// <summary>Whether a root-relative file exists.</summary>
    /// <param name="relativePath">The root-relative path.</param>
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