using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace RaccoonNinja.McpToolset.Files.Security;

/// <summary>
/// Windows path canonicalization via <c>GetFinalPathNameByHandle</c>. Opening a handle and asking the
/// kernel for the final path collapses the entire reparse-point chain (symlinks and junctions,
/// including intermediate components) and returns the long-name form, which closes the 8.3 short-name
/// bypass in the same call. There is no managed equivalent, so this is a P/Invoke.
/// </summary>
internal static class Win32Path
{
    private const uint FileShareAll = 0x00000001 | 0x00000002 | 0x00000004; // read | write | delete
    private const uint OpenExisting = 3;
    private const uint FileFlagBackupSemantics = 0x02000000; // required to open a directory handle
    private const string ExtendedPrefix = @"\\?\";
    private const string ExtendedUncPrefix = @"\\?\UNC\";

    /// <summary>
    /// Resolve an existing path to its canonical long-form absolute path. The caller resolves the
    /// longest existing ancestor and appends any not-yet-created leaf, so this is only ever asked to
    /// resolve a path that exists on disk.
    /// </summary>
    /// <exception cref="Win32Exception">Thrown when the handle cannot be opened or the final path cannot be read (fail closed).</exception>
    internal static string GetFinalPath(string existingPath)
    {
        using var handle = CreateFile(
            existingPath,
            dwDesiredAccess: 0,
            FileShareAll,
            lpSecurityAttributes: IntPtr.Zero,
            OpenExisting,
            FileFlagBackupSemantics,
            hTemplateFile: IntPtr.Zero);

        if (handle.IsInvalid)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error());
        }

        var buffer = new char[512];
        for (var attempt = 0; attempt < 4; attempt++)
        {
            var length = GetFinalPathNameByHandle(handle, buffer, (uint)buffer.Length, dwFlags: 0);
            if (length == 0)
            {
                throw new Win32Exception(Marshal.GetLastWin32Error());
            }

            if (length < buffer.Length)
            {
                return StripExtendedPrefix(new string(buffer, 0, (int)length));
            }

            buffer = new char[length + 1];
        }

        throw new Win32Exception($"could not resolve the final path of '{existingPath}'");
    }

    /// <summary>Drop the <c>\\?\</c> (or <c>\\?\UNC\</c>) prefix the kernel returns, leaving an ordinary absolute path.</summary>
    private static string StripExtendedPrefix(string path)
    {
        if (path.StartsWith(ExtendedUncPrefix, StringComparison.Ordinal))
        {
            return string.Concat(@"\\", path.AsSpan(ExtendedUncPrefix.Length));
        }

        return path.StartsWith(ExtendedPrefix, StringComparison.Ordinal)
            ? path[ExtendedPrefix.Length..]
            : path;
    }

    [DllImport("kernel32.dll", EntryPoint = "CreateFileW", SetLastError = true, CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern SafeFileHandle CreateFile(
        string lpFileName,
        uint dwDesiredAccess,
        uint dwShareMode,
        IntPtr lpSecurityAttributes,
        uint dwCreationDisposition,
        uint dwFlagsAndAttributes,
        IntPtr hTemplateFile);

    [DllImport("kernel32.dll", EntryPoint = "GetFinalPathNameByHandleW", SetLastError = true, CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern uint GetFinalPathNameByHandle(
        SafeFileHandle hFile,
        [Out] char[] lpszFilePath,
        uint cchFilePath,
        uint dwFlags);
}