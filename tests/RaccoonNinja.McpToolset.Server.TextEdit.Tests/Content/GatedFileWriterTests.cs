using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using RaccoonNinja.McpToolset.Common.Files.Security;
using RaccoonNinja.McpToolset.Common.Files.Selection;
using RaccoonNinja.McpToolset.Server.TextEdit.Content;
using RaccoonNinja.McpToolset.Server.TextEdit.Errors;
using RaccoonNinja.McpToolset.Server.TextEdit.Tests.TestSupport;

namespace RaccoonNinja.McpToolset.Server.TextEdit.Tests.Content;

public sealed class GatedFileWriterTests : IDisposable
{
    private readonly TextEditHarness _harness = new();

    public void Dispose() => _harness.Dispose();

    [Fact]
    public void Apply_DenylistedFiles_AreRefusedAndNeverWritten()
    {
        // Arrange
        _harness.WriteText(".env", "SECRET=1");
        _harness.WriteText(".git/config", "[core]");
        _harness.WriteText(".ssh/id_rsa", "-----BEGIN KEY-----");
        var replacer = Replace("=", "-");

        // Act
        var outcome = _harness.Apply("replace_text", replacer, ".env", ".git/config", ".ssh/id_rsa");

        // Assert
        Assert.Equal(0, outcome.Changed);
        Assert.All(outcome.Files, file => Assert.Equal(RefusalReason.Denied, file.Reason));
        Assert.Equal("SECRET=1", _harness.ReadText(".env"));
    }

    [Fact]
    public void Apply_GitHookFile_IsRefusedForWrite()
    {
        // Arrange
        _harness.WriteText(".git/hooks/pre-commit", "#!/bin/sh");
        var replacer = Replace("sh", "bash");

        // Act
        var outcome = _harness.Apply("replace_text", replacer, ".git/hooks/pre-commit");

        // Assert
        Assert.Equal(RefusalReason.Denied, outcome.Files[0].Reason);
    }

    [Fact]
    public void Apply_ClientConfigOptOutValue_RefusesEachFileAsDenied()
    {
        // Arrange
        const string original = "{}";
        const string mcpConfig = ".mcp.json";
        const string userConfig = "user/.claude.json";
        const string projectSettings = "proj/.claude/settings.json";
        const string localSettings = "proj/.claude/settings.local.json";
        using var harness = new TextEditHarness(extraDeny: ".mcp.json;.claude.json;**/.claude/settings*.json");
        harness.WriteText(mcpConfig, original);
        harness.WriteText(userConfig, original);
        harness.WriteText(projectSettings, original);
        harness.WriteText(localSettings, original);
        var replacer = new Replacer(original, "{ }", false, false, harness.Config);

        // Act
        var outcome = harness.Apply("replace_text", replacer, mcpConfig, userConfig, projectSettings, localSettings);

        // Assert
        Assert.Equal(4, outcome.Refused);
        Assert.All(outcome.Files, file => Assert.Equal(RefusalReason.Denied, file.Reason));
        Assert.Equal(original, harness.ReadText(projectSettings));
    }

    [Fact]
    public void Apply_PathEscapingRoot_IsRefusedOutOfRoot()
    {
        // Arrange
        var replacer = Replace("a", "b");

        // Act
        var outcome = _harness.Apply("replace_text", replacer, "../outside.txt");

        // Assert
        Assert.Equal(RefusalReason.OutOfRoot, outcome.Files[0].Reason);
    }

    [Fact]
    public void Apply_BinaryFile_IsRefusedBinary()
    {
        // Arrange
        _harness.WriteBytes("blob.bin", [0x00, 0x41, 0x42]);
        var replacer = Replace("A", "B");

        // Act
        var outcome = _harness.Apply("replace_text", replacer, "blob.bin");

        // Assert
        Assert.Equal(RefusalReason.Binary, outcome.Files[0].Reason);
    }

    [Fact]
    public void Apply_FileOverSizeCap_IsRefusedTooLarge()
    {
        // Arrange
        using var harness = new TextEditHarness(TextEditHarness.DefaultConfig() with { MaxFileBytes = 8 });
        harness.WriteText("big.txt", "this is more than eight bytes");

        // Act
        var outcome = harness.Apply("replace_text", new Replacer("is", "was", false, false, harness.Config), "big.txt");

        // Assert
        Assert.Equal(RefusalReason.TooLarge, outcome.Files[0].Reason);
    }

    [Fact]
    public void Apply_TransformExpandsPastSizeCap_IsRefusedTooLargeAndNotWritten()
    {
        // Arrange
        using var harness = new TextEditHarness(TextEditHarness.DefaultConfig() with { MaxFileBytes = 16 });
        harness.WriteText("grow.txt", "aaaaaaaa");

        // Act
        var outcome = harness.Apply("replace_text", new Replacer("a", "ccc", false, false, harness.Config), "grow.txt");

        // Assert
        Assert.Equal(RefusalReason.TooLarge, outcome.Files[0].Reason);
        Assert.Null(outcome.BatchId);
        Assert.Equal("aaaaaaaa", harness.ReadText("grow.txt"));
    }

    [Fact]
    public void Apply_IgnoredFileViaAncestorRule_IsRefusedWhileUntrackedIsChanged()
    {
        // Arrange
        _harness.WriteText(".gitignore", "bin/\n");
        _harness.WriteText("bin/app.dll", "hello world");
        _harness.WriteText("src/app.cs", "hello world");
        var replacer = Replace("world", "text");

        // Act
        var outcome = _harness.Apply("replace_text", replacer, "bin/app.dll", "src/app.cs");

        // Assert
        Assert.Equal(RefusalReason.Ignored, outcome.Files.Single(file => file.Path == "bin/app.dll").Reason);
        Assert.True(outcome.Files.Single(file => file.Path == "src/app.cs").Changed);
        Assert.Equal("hello text", _harness.ReadText("src/app.cs"));
    }

    [Fact]
    public void Apply_AncestorGitignore_RefusesWriteInScopedCwd()
    {
        // Arrange
        _harness.WriteText(".gitignore", "appsettings.Production.json\n");
        _harness.WriteText("proj/appsettings.Production.json", "hello world");
        var scope = _harness.Resolver.Resolve(_harness.Dir("proj"));
        var replacer = Replace("world", "text");

        // Act
        var outcome = _harness.Writer.Apply(
            "replace_text",
            ["appsettings.Production.json"],
            replacer,
            "test",
            expectedMatchCount: null,
            dryRun: false,
            sourceEncoding: null,
            skippedSymlinks: 0,
            truncated: false,
            scope.Effective,
            CancellationToken.None);

        // Assert
        Assert.Equal(RefusalReason.Ignored, outcome.Files.Single().Reason);
        Assert.Equal("hello world", _harness.ReadText("proj/appsettings.Production.json"));
    }

    [Fact]
    public void Apply_Utf16NoBom_RoundTripsPreservingEncoding()
    {
        // Arrange
        _harness.WriteBytes("u.txt", [0x68, 0x00, 0x69, 0x00]);
        var replacer = Replace("hi", "yo");

        // Act
        var outcome = _harness.Apply("replace_text", replacer, "u.txt");

        // Assert
        Assert.True(outcome.Files[0].Changed);
        Assert.Equal([0x79, 0x00, 0x6F, 0x00], _harness.ReadBytes("u.txt"));
    }

    [Fact]
    public void Apply_LowConfidenceEncoding_RefusedUnlessSourceEncodingSupplied()
    {
        // Arrange
        using var harness = new TextEditHarness(TextEditHarness.DefaultConfig() with { RewriteConfidence = 0.95 });
        harness.WriteBytes("u.txt", [0x68, 0x00, 0x69, 0x00]);

        // Act
        var refused = harness.Writer.Apply("replace_text", ["u.txt"], new Replacer("hi", "yo", false, false, harness.Config), "t", null, false, null, 0, false, harness.Confinement, CancellationToken.None);
        var allowed = harness.Writer.Apply("replace_text", ["u.txt"], new Replacer("hi", "yo", false, false, harness.Config), "t", null, false, "utf-16le", 0, false, harness.Confinement, CancellationToken.None);

        // Assert
        Assert.Equal(RefusalReason.LowConfidenceEncoding, refused.Files[0].Reason);
        Assert.True(allowed.Files[0].Changed);
    }

    [Fact]
    public void Apply_ExpectedMatchCountMismatch_AbortsWithoutWriting()
    {
        // Arrange
        _harness.WriteText("a.txt", "hello world");
        var replacer = Replace("world", "x");

        // Act
        // Assert
        Assert.Throws<TextEditException>(() =>
            _harness.Writer.Apply("replace_text", ["a.txt"], replacer, "t", expectedMatchCount: 5, dryRun: false, sourceEncoding: null, skippedSymlinks: 0, truncated: false, _harness.Confinement, CancellationToken.None));
        Assert.Equal("hello world", _harness.ReadText("a.txt"));
    }

    [Fact]
    public void Apply_DryRun_WritesNothingAndReturnsDiff()
    {
        // Arrange
        _harness.WriteText("a.txt", "hello world");
        var replacer = Replace("world", "text-edit");

        // Act
        var outcome = _harness.Writer.Apply("replace_text", ["a.txt"], replacer, "t", null, dryRun: true, null, 0, false, _harness.Confinement, CancellationToken.None);

        // Assert
        Assert.Null(outcome.BatchId);
        Assert.True(outcome.Files[0].Changed);
        Assert.Contains("text-edit", outcome.Files[0].Diff, StringComparison.Ordinal);
        Assert.Equal("hello world", _harness.ReadText("a.txt"));
    }

    [Fact]
    public void Apply_TransformThatChangesNothing_WritesNoBatch()
    {
        // Arrange
        _harness.WriteText("a.txt", "hello world");
        var replacer = Replace("absent", "x");

        // Act
        var outcome = _harness.Apply("replace_text", replacer, "a.txt");

        // Assert
        Assert.Null(outcome.BatchId);
        Assert.Equal(0, outcome.Changed);
        Assert.Equal("hello world", _harness.ReadText("a.txt"));
    }

    [Fact]
    public void Apply_MixedLineEndingsUnderPreserve_KeepsEachTerminator()
    {
        // Arrange
        _harness.WriteText("m.txt", "a \r\nb \nc \r");
        var normalizer = new Normalizer(new NormalizeOptions { TrimTrailingWhitespace = true });

        // Act
        var outcome = _harness.Apply("normalize_files", normalizer, "m.txt");

        // Assert
        Assert.True(outcome.Files[0].Changed);
        Assert.Equal("a\r\nb\nc\r", _harness.ReadText("m.txt"));
    }

    [Fact]
    public void Apply_EffectiveConfinerIsTheWriteGate_RefusesPathEscapingCwd()
    {
        // Arrange
        _harness.WriteText("sibling.txt", "hello world");
        var effective = new RootConfinement(_harness.Dir("proj"));
        var replacer = Replace("world", "text");

        // Act
        var outcome = _harness.Writer.Apply(
            "replace_text", ["../sibling.txt"], replacer, "t", null, false, null, 0, false, effective, CancellationToken.None);

        // Assert
        Assert.Equal(RefusalReason.OutOfRoot, outcome.Files[0].Reason);
        Assert.Equal("hello world", _harness.ReadText("sibling.txt"));
    }

    [Fact]
    public void Apply_ChangedFile_JournalsBaseRelativePathWhenScopedToCwd()
    {
        // Arrange
        _harness.WriteText("proj/a.txt", "hello world");
        var effective = new RootConfinement(_harness.Dir("proj"));
        var replacer = Replace("world", "text");

        // Act
        var outcome = _harness.Writer.Apply(
            "replace_text", ["a.txt"], replacer, "t", null, false, null, 0, false, effective, CancellationToken.None);

        // Assert
        Assert.True(outcome.Files[0].Changed);
        Assert.Equal("proj/a.txt", outcome.Files[0].Path);
        var rows = _harness.Journal.GetBatchFiles(outcome.BatchId.Value);
        Assert.Equal("proj/a.txt", Assert.Single(rows).Path);
    }

    [Theory]
    [MemberData(nameof(IgnoreFileNames))]
    public void Apply_IgnoreFileAtAnyDepth_IsRefusedAndNeverWritten(string fileName)
    {
        // Arrange
        var path = $"sub/{fileName}";
        _harness.WriteText(path, "hello world");
        var replacer = Replace("world", "text");

        // Act
        var outcome = _harness.Apply("replace_text", replacer, path);

        // Assert
        Assert.Equal(RefusalReason.IgnoreFile, Assert.Single(outcome.Files).Reason);
        Assert.Null(outcome.BatchId);
        Assert.Equal("hello world", _harness.ReadText(path));
    }

    [Fact]
    public void Apply_IgnoredIgnoreFile_ReportsIgnoreFileNotIgnored()
    {
        // Arrange
        _harness.WriteText(".gitignore", ".mcpignore\n");
        _harness.WriteText(".mcpignore", "hello world\n");

        // Act
        var outcome = _harness.Apply("replace_text", Replace("world", "text"), ".mcpignore");

        // Assert
        Assert.Equal(RefusalReason.IgnoreFile, Assert.Single(outcome.Files).Reason);
    }

    [Fact]
    public void Apply_IgnoreFileAddressedByShortName_IsRefused()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Skip("8.3 short names exist only on Windows.");
        }

        // Arrange
        _harness.WriteText(".mcpignore", "hello world\n");
        var shortName = ShortFileName(Path.Combine(_harness.Root, ".mcpignore"));
        if (shortName is null or ".mcpignore")
        {
            Assert.Skip("This volume generated no 8.3 short name.");
        }

        // Act
        var outcome = _harness.Apply("replace_text", Replace("world", "text"), shortName);

        // Assert
        var file = Assert.Single(outcome.Files);
        Assert.Equal(".mcpignore", file.Path);
        Assert.Equal(RefusalReason.IgnoreFile, file.Reason);
        Assert.Equal("hello world\n", _harness.ReadText(".mcpignore"));
    }

    [Fact]
    public void Apply_SymlinkToIgnoreFile_IsRefusedAsIgnoreFile()
    {
        // Arrange
        _harness.WriteText(".gitignore", "hello world\n");
        if (!TryCreateFileLink(Path.Combine(_harness.Root, "notes.txt"), Path.Combine(_harness.Root, ".gitignore")))
        {
            Assert.Skip("Creating symbolic links is not permitted in this environment.");
        }

        // Act
        var outcome = _harness.Apply("replace_text", Replace("world", "text"), "notes.txt");

        // Assert
        var file = Assert.Single(outcome.Files);
        Assert.Equal(".gitignore", file.Path);
        Assert.Equal(RefusalReason.IgnoreFile, file.Reason);
        Assert.Equal("hello world\n", _harness.ReadText(".gitignore"));
    }

    [Fact]
    public void Apply_HardLinkToIgnoreFile_LeavesIgnoreFileUnchanged()
    {
        // Arrange
        _harness.WriteText(".mcpignore", "seed\n");
        CreateHardLink(Path.Combine(_harness.Root, "rules.txt"), Path.Combine(_harness.Root, ".mcpignore"));
        _harness.WriteText(".mcpignore", "hello world\n");

        // Act
        var outcome = _harness.Apply("replace_text", Replace("world", "text"), "rules.txt");

        // Assert
        var file = Assert.Single(outcome.Files);
        Assert.Equal("rules.txt", file.Path);
        Assert.Null(file.Reason);
        Assert.True(file.Changed);
        Assert.Equal("hello text\n", _harness.ReadText("rules.txt"));
        Assert.Equal("hello world\n", _harness.ReadText(".mcpignore"));
    }

    #region Test Helpers

    public static TheoryData<string> IgnoreFileNames()
    {
        var data = new TheoryData<string>();
        foreach (var name in IgnoreRules.IgnoreFileNames)
        {
            data.Add(name);
        }

        return data;
    }

    private Replacer Replace(string pattern, string replacement)
        => new(pattern, replacement, isRegex: false, caseSensitive: false, _harness.Config);

    [DllImport("kernel32.dll", EntryPoint = "GetShortPathNameW", SetLastError = true, CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern uint GetShortPathName(string longPath, [Out] char[] shortPath, uint bufferLength);

    private static string ShortFileName(string fullPath)
    {
        var buffer = new char[1024];
        var length = GetShortPathName(fullPath, buffer, (uint)buffer.Length);
        return length == 0 || length >= buffer.Length
            ? null
            : Path.GetFileName(new string(buffer, 0, (int)length));
    }

    [DllImport("kernel32.dll", EntryPoint = "CreateHardLinkW", SetLastError = true, CharSet = CharSet.Unicode, ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CreateHardLinkW(string fileName, string existingFileName, IntPtr securityAttributes);

    private static void CreateHardLink(string link, string target)
    {
        if (OperatingSystem.IsWindows())
        {
            if (!CreateHardLinkW(link, target, IntPtr.Zero))
            {
                throw new Win32Exception(Marshal.GetLastPInvokeError());
            }

            return;
        }

        var psi = new ProcessStartInfo
        {
            FileName = "ln",
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        psi.ArgumentList.Add(target);
        psi.ArgumentList.Add(link);

        using var process = Process.Start(psi) ?? throw new InvalidOperationException("failed to start ln");
        var stderr = process.StandardError.ReadToEnd();
        process.WaitForExit();
        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException($"ln failed ({process.ExitCode}): {stderr}");
        }
    }

    private static bool TryCreateFileLink(string link, string target)
    {
        try
        {
            File.CreateSymbolicLink(link, target);
            return true;
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
        catch (PlatformNotSupportedException)
        {
            return false;
        }
    }

    #endregion
}