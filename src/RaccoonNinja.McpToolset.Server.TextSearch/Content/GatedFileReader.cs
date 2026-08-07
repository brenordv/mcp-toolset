using RaccoonNinja.McpToolset.Files.Security;
using RaccoonNinja.McpToolset.Files.Selection;

namespace RaccoonNinja.McpToolset.Server.TextSearch.Content;

/// <summary>
/// The one open-boundary read gate for file content. Every content read (inspect, search, read_lines)
/// goes through it, so confinement and the denylist hold even when enumeration is skipped (a
/// <c>paths[]</c> or a single-path read). The size is checked before any bytes are read (an oversized
/// file is refused unread) and the read is bounded to the cap, and every filesystem exception is caught
/// and mapped to a typed outcome so no absolute path from a .NET exception message can escape into
/// model context.
/// </summary>
public sealed class GatedFileReader(IRootResolver root, ISecretDenylist denylist, long maxBytes, IRootResolver anchor = null, string prefix = null, ISecretContentScanner scanner = null)
{
    private readonly IRootResolver _root = root ?? throw new ArgumentNullException(nameof(root));
    private readonly ISecretDenylist _denylist = denylist ?? throw new ArgumentNullException(nameof(denylist));
    private readonly IRootResolver _anchor = anchor ?? root;
    private readonly string _prefix = prefix ?? string.Empty;
    private readonly ISecretContentScanner _scanner = scanner ?? NullSecretContentScanner.Instance;

    /// <summary>Confine, denylist-check, size-cap, and read <paramref name="relativePath"/>.</summary>
    /// <param name="relativePath">A root-relative or in-root path to read.</param>
    /// <returns>The typed read outcome; the absolute path is never surfaced.</returns>
    public ReadOutcome Read(string relativePath)
    {
        ArgumentNullException.ThrowIfNull(relativePath);

        ConfinedPath confined;
        try
        {
            confined = _root.Confine(relativePath, "path");
        }
        catch (PathConfinementException)
        {
            return ReadOutcome.OutOfRoot();
        }

        if (!confined.Exists)
        {
            return ReadOutcome.NotFound();
        }

        if (_denylist.IsDeniedFile(confined.RelativePath))
        {
            return ReadOutcome.Denied(confined.RelativePath);
        }

        // The project ignore boundary is anchored at the base root (not the effective scope), so a scoped
        // read still honors ancestor .gitignore/.mcpignore rules and no caller can read an ignored file.
        if (PathIgnoreEvaluator.IsIgnored(_anchor.CanonicalRoot, ToBaseRelative(confined.RelativePath)))
        {
            return ReadOutcome.Ignored(confined.RelativePath);
        }

        var outcome = ReadBytes(confined.RealPath, confined.RelativePath);

        // Content scan runs last, on the bytes actually read, so a file whose content looks like a secret is
        // withheld even when its name and location are innocuous. Ignored/denylisted files never reach here.
        if (outcome.IsOk && _scanner.Scan(outcome.Bytes).IsSecret)
        {
            return ReadOutcome.SecretContent(confined.RelativePath);
        }

        return outcome;
    }

    /// <summary>Rebase a scope-relative path onto the base anchor for ignore evaluation.</summary>
    private string ToBaseRelative(string relativePath)
        => _prefix.Length == 0 ? relativePath : string.Concat(_prefix, "/", relativePath);

    // Only reached after Read() confirmed confined.Exists, i.e. Path.Exists(RealPath), which is false for
    // a null, empty, or whitespace path; so realPath here is always a real, non-empty absolute path.
    // RootConfinement never yields an empty one anyway, and any stray fault is caught and mapped to a
    // path-free InternalError by ToolCommon.WrapAsync.
    private ReadOutcome ReadBytes(string realPath, string relativePath)
    {
        try
        {
            if (Directory.Exists(realPath))
            {
                return ReadOutcome.IsDirectory();
            }

            using var stream = new FileStream(
                realPath!,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete);

            var length = stream.Length;
            if (length > maxBytes)
            {
                return ReadOutcome.TooLarge(relativePath, length);
            }

            var bytes = new byte[length];
            stream.ReadExactly(bytes, 0, bytes.Length);
            return ReadOutcome.Ok(bytes, relativePath);
        }
        catch (FileNotFoundException)
        {
            return ReadOutcome.NotFound();
        }
        catch (DirectoryNotFoundException)
        {
            return ReadOutcome.NotFound();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            // Never let a path-bearing filesystem exception escape; the tool maps this to a safe outcome.
            return ReadOutcome.IoError();
        }
    }
}