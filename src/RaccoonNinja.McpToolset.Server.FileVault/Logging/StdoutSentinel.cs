using System.Text;

namespace RaccoonNinja.McpToolset.Server.FileVault.Logging;

/// <summary>
/// Forwarding shim that delegates non-write operations to the wrapped <see cref="TextWriter"/>
/// while rejecting every <c>Write*</c>/<c>WriteLine*</c> call. The MCP SDK writes JSON-RPC
/// frames via the underlying <see cref="Console.OpenStandardOutput()"/> stream, which is
/// untouched by this wrapper. A stray <c>Console.WriteLine</c> or default logging
/// <c>StreamHandler</c> aimed at stdout will throw at the call site instead of corrupting
/// the JSON-RPC stream.
/// </summary>
public sealed class StdoutSentinel : TextWriter
{
    private static readonly Lock InstallLock = new();
    private static TextWriter _original;
    private static StdoutSentinel _installed;

    private readonly TextWriter _inner;

    private StdoutSentinel(TextWriter inner)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
    }

    public override Encoding Encoding => _inner.Encoding;
    public override IFormatProvider FormatProvider => _inner.FormatProvider;
    public override string NewLine
    {
        get => _inner.NewLine;
        set => _inner.NewLine = value;
    }

    /// <summary>Replace <see cref="Console.Out"/> with the sentinel. Idempotent.</summary>
    public static void Install()
    {
        lock (InstallLock)
        {
            if (_installed != null) return;
            _original = Console.Out;
            _installed = new StdoutSentinel(_original);
            Console.SetOut(_installed);
        }
    }

    /// <summary>Restore the original <see cref="Console.Out"/>. Tests use this; production does not.</summary>
    public static void Uninstall()
    {
        lock (InstallLock)
        {
            if (_installed == null) return;
            Console.SetOut(_original);
            _installed = null;
            _original = null;
        }
    }

    private static InvalidOperationException Forbidden() => new(
        "Direct write to Console.Out is forbidden in the MCP server "
        + "(JSON-RPC stream protection). Log to stderr or the configured file sink instead.");

    public override void Write(char value) => throw Forbidden();
    public override void Write(string value) => throw Forbidden();
    public override void Write(char[] buffer) => throw Forbidden();
    public override void Write(char[] buffer, int index, int count) => throw Forbidden();
    public override void Write(ReadOnlySpan<char> buffer) => throw Forbidden();
    public override void WriteLine() => throw Forbidden();
    public override void WriteLine(string value) => throw Forbidden();
    public override void WriteLine(char value) => throw Forbidden();
    public override void WriteLine(ReadOnlySpan<char> buffer) => throw Forbidden();

    public override void Flush() => _inner.Flush();
}