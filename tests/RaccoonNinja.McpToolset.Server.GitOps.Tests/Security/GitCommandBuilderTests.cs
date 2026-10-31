using RaccoonNinja.McpToolset.Server.GitOps.Errors.GitCheckExceptions;
using RaccoonNinja.McpToolset.Server.GitOps.Security;

namespace RaccoonNinja.McpToolset.Server.GitOps.Tests.Security;

public class GitCommandBuilderTests
{
    private const string AutoRefreshIndexPin = "diff.autoRefreshIndex=false";
    private const string ShowRootPin = "log.showRoot=true";

    private static readonly string GitExecutable = Path.Combine(Path.GetTempPath(), "git");

    private static GitIntent BasicIntent(string subcommand = "log")
        => new()
        {
            Subcommand = subcommand,
            RepoRoot = Path.GetTempPath(),
        };

    [Fact]
    public void Build_PrependsFixedConfigAndGlobalFlags()
    {
        // Act
        var (argv, _) = GitCommandBuilder.Build(BasicIntent(), GitExecutable);

        // Assert
        Assert.Equal(GitExecutable, argv[0]);
        Assert.Contains("-c", argv);
        Assert.Contains("core.fsmonitor=", argv);
        Assert.Contains("core.hooksPath=/nonexistent/git-check-hooks-disabled", argv);
        Assert.Contains("--no-pager", argv);
        Assert.DoesNotContain("--literal-pathspecs", argv);
        Assert.Contains("log.showSignature=false", argv);
        Assert.Contains("log.follow=false", argv);
        Assert.Contains("fetch.bundleURI=", argv);
        Assert.Contains(AutoRefreshIndexPin, argv);
        Assert.Contains(ShowRootPin, argv);
    }

    [Fact]
    public void Build_AddsDashCBlockWithRepoRoot()
    {
        // Arrange
        var intent = BasicIntent();

        // Act
        var (argv, _) = GitCommandBuilder.Build(intent, GitExecutable);

        // Assert
        var index = argv.IndexOf("-C");
        Assert.Equal(intent.RepoRoot, argv[index + 1]);
    }

    [Fact]
    public void Build_AddsDiffHardeningForDiffBearingSubcommands()
    {
        // Act
        var (argv, _) = GitCommandBuilder.Build(BasicIntent("diff"), GitExecutable);

        // Assert
        Assert.Contains("--no-ext-diff", argv);
        Assert.Contains("--no-textconv", argv);
    }

    [Fact]
    public void Build_OmitsExtDiffForGrep()
    {
        // Act
        var (argv, _) = GitCommandBuilder.Build(BasicIntent("grep"), GitExecutable);

        // Assert
        Assert.DoesNotContain("--no-ext-diff", argv);
        Assert.Contains("--no-textconv", argv);
    }

    [Fact]
    public void Build_EmitsAttachedOptionsWithEqualsForLongFlags()
    {
        // Arrange
        var intent = BasicIntent();
        intent.AttachedOptions.Add(new AttachedOption("--author", "alice"));

        // Act
        var (argv, _) = GitCommandBuilder.Build(intent, GitExecutable);

        // Assert
        Assert.Contains("--author=alice", argv);
    }

    [Fact]
    public void Build_EmitsAttachedOptionsGluedForSingleCharFlags()
    {
        // Arrange
        var intent = BasicIntent();
        intent.AttachedOptions.Add(new AttachedOption("-S", "ROOT"));

        // Act
        var (argv, _) = GitCommandBuilder.Build(intent, GitExecutable);

        // Assert
        Assert.Contains("-SROOT", argv);
    }

    [Fact]
    public void Build_RejectsAttachedOptionWhoseValueStartsWithDash()
    {
        // Arrange
        var intent = BasicIntent();
        intent.AttachedOptions.Add(new AttachedOption("--author", "-evil"));

        // Act & Assert
        Assert.Throws<RejectedArgumentException>(() => GitCommandBuilder.Build(intent, GitExecutable));
    }

    [Fact]
    public void Build_Inserts_EndOfOptions_Barrier_Before_VerifiedRefs_For_NonGrep_RefBearing_Command()
    {
        // Arrange
        var intent = BasicIntent();
        intent.VerifiedRefs.Add("abc1234567");

        // Act
        var (argv, _) = GitCommandBuilder.Build(intent, GitExecutable);

        // Assert
        var barrierIdx = argv.IndexOf("--end-of-options");
        var refIdx = argv.IndexOf("abc1234567");
        Assert.True(barrierIdx >= 0);
        Assert.True(barrierIdx < refIdx);
    }

    [Fact]
    public void Build_Omits_EndOfOptions_For_Grep_Working_Tree()
    {
        var intent = BasicIntent("grep");
        intent.AttachedOptions.Add(new AttachedOption("-e", "needle"));
        var (argv, _) = GitCommandBuilder.Build(intent, GitExecutable);
        Assert.DoesNotContain("--end-of-options", argv);
    }

    [Fact]
    public void Build_Omits_EndOfOptions_For_Grep_With_Ref_And_Emits_Ref_As_Bare_Positional()
    {
        var intent = BasicIntent("grep");
        intent.AttachedOptions.Add(new AttachedOption("-e", "needle"));
        intent.VerifiedRefs.Add("abc1234567");
        var (argv, _) = GitCommandBuilder.Build(intent, GitExecutable);
        Assert.DoesNotContain("--end-of-options", argv);
        Assert.Contains("abc1234567", argv);
    }

    [Fact]
    public void Build_Omits_Trailing_EndOfOptions_When_No_Positionals()
    {
        var (argv, _) = GitCommandBuilder.Build(BasicIntent("status"), GitExecutable);
        Assert.DoesNotContain("--end-of-options", argv);
    }

    [Fact]
    public void Build_Inserts_EndOfOptions_Before_PositionalServerArgs_For_NonGrep()
    {
        var intent = BasicIntent("for-each-ref");
        intent.PositionalServerArgs.Add("refs/heads");
        var (argv, _) = GitCommandBuilder.Build(intent, GitExecutable);
        var barrierIdx = argv.IndexOf("--end-of-options");
        var positionalIdx = argv.IndexOf("refs/heads");
        Assert.True(barrierIdx >= 0);
        Assert.True(barrierIdx < positionalIdx);
    }

    [Fact]
    public void Build_LsTreeWithRefAndPath_EmitsBarrierThenRefThenDashDashPath()
    {
        // Arrange
        const string sha = "0123456789abcdef0123456789abcdef01234567";
        var intent = new GitIntent
        {
            Subcommand = "ls-tree",
            RepoRoot = Path.GetTempPath(),
            Flags = ["-z", "-l"],
            VerifiedRefs = [sha],
            Pathspecs = [Pathspec.Literal("dir/file.txt")],
        };

        // Act
        var (argv, _) = GitCommandBuilder.Build(intent, GitExecutable);

        // Assert
        var refIdx = argv.IndexOf(sha);
        var pathIdx = argv.IndexOf(":(literal)dir/file.txt");
        Assert.Equal("--end-of-options", argv[refIdx - 1]);
        Assert.Equal("--", argv[pathIdx - 1]);
        Assert.True(refIdx < pathIdx);
    }

    [Fact]
    public void Build_SeparatesPathspecsWithDoubleDash()
    {
        // Arrange
        var intent = BasicIntent();
        intent.Pathspecs.Add(Pathspec.Literal("src/foo.cs"));

        // Act
        var (argv, _) = GitCommandBuilder.Build(intent, GitExecutable);

        // Assert
        var dashIdx = argv.IndexOf("--");
        var pathIdx = argv.IndexOf(":(literal)src/foo.cs");
        Assert.True(dashIdx > 0 && pathIdx > dashIdx);
    }

    [Fact]
    public void Build_RejectsPathspecThatStartsWithDash()
    {
        // Arrange
        var intent = BasicIntent();
        intent.Pathspecs.Add(Pathspec.Literal("-rm-rf"));

        // Act & Assert
        Assert.Throws<RejectedArgumentException>(() => GitCommandBuilder.Build(intent, GitExecutable));
    }

    [Fact]
    public void Build_LiteralPathspec_IsPrefixedWithLiteralMagic()
    {
        // Arrange
        var intent = BasicIntent();
        intent.Pathspecs.Add(Pathspec.Literal("src/foo.cs"));

        // Act
        var (argv, _) = GitCommandBuilder.Build(intent, GitExecutable);

        // Assert
        var dashIdx = argv.IndexOf("--");
        Assert.Equal(":(literal)src/foo.cs", argv[dashIdx + 1]);
    }

    [Fact]
    public void Build_ExcludePathspec_IsPrefixedWithExcludeMagic()
    {
        // Arrange
        var intent = BasicIntent("grep");
        intent.Pathspecs.Add(Pathspec.Exclude("env"));

        // Act
        var (argv, _) = GitCommandBuilder.Build(intent, GitExecutable);

        // Assert
        Assert.Equal(":(exclude)env", argv.Last());
    }

    [Fact]
    public void Build_GlobPathspec_IsEmittedVerbatim()
    {
        // Arrange
        var intent = BasicIntent("grep");
        intent.Pathspecs.Add(Pathspec.Glob("*.cs"));

        // Act
        var (argv, _) = GitCommandBuilder.Build(intent, GitExecutable);

        // Assert
        Assert.Equal("*.cs", argv.Last());
    }

    [Fact]
    public void Build_GlobPathspecStartingWithColon_IsRejected()
    {
        // Arrange
        var intent = BasicIntent("grep");
        intent.Pathspecs.Add(Pathspec.Glob(":(top)x"));

        // Act & Assert
        var exception = Assert.Throws<RejectedArgumentException>(() => GitCommandBuilder.Build(intent, GitExecutable));
        Assert.Equal("glob", exception.Detail["param"]);
    }

    [Fact]
    public void Build_LiteralPathspecStartingWithColon_IsEmittedAsPatternText()
    {
        // Arrange
        var intent = BasicIntent();
        intent.Pathspecs.Add(Pathspec.Literal(":(top)x"));

        // Act
        var (argv, _) = GitCommandBuilder.Build(intent, GitExecutable);

        // Assert
        Assert.Equal(":(literal):(top)x", argv.Last());
    }

    [Fact]
    public void Build_EmptyPathspecValue_IsRejected()
    {
        // Arrange
        var intent = BasicIntent("grep");
        intent.Pathspecs.Add(Pathspec.Exclude(""));

        // Act & Assert
        var exception = Assert.Throws<RejectedArgumentException>(() => GitCommandBuilder.Build(intent, GitExecutable));
        Assert.Equal("exclude", exception.Detail["param"]);
    }

    [Fact]
    public void Build_ReturnsEnvWithFixedGitNeutralizersAndAllowlistedPATH()
    {
        // Act
        var (_, env) = GitCommandBuilder.Build(BasicIntent(), GitExecutable);

        // Assert
        Assert.Equal("0", env["GIT_TERMINAL_PROMPT"]);
        Assert.Equal("1", env["GIT_CONFIG_NOSYSTEM"]);
        Assert.Equal("0", env["GIT_OPTIONAL_LOCKS"]);
        Assert.Equal("1", env["GIT_NO_LAZY_FETCH"]);
        Assert.Equal("none", env["GIT_ALLOW_PROTOCOL"]);
        Assert.False(env.ContainsKey("GIT_LITERAL_PATHSPECS"));
        Assert.True(env.ContainsKey("PATH"));
    }

    [Fact]
    public void Build_WithFilterDrivers_PinsEachDriverAfterFixedConfig()
    {
        // Act
        var (argv, _) = GitCommandBuilder.Build(BasicIntent("status"), GitExecutable, ["lfs"]);

        // Assert
        var start = argv.IndexOf("filter.lfs.clean=");
        Assert.Equal("-c", argv[start - 1]);
        Assert.Equal(
            ["filter.lfs.clean=", "-c", "filter.lfs.smudge=", "-c", "filter.lfs.process=", "-c", "filter.lfs.required=false"],
            argv.Skip(start).Take(7));
        Assert.True(argv.IndexOf("log.showSignature=false") < start);
        Assert.True(start < argv.IndexOf("--no-pager"));
    }

    [Fact]
    public void Build_WithoutFilterDrivers_EmitsNoFilterPins()
    {
        // Act
        var (argv, _) = GitCommandBuilder.Build(BasicIntent("status"), GitExecutable);

        // Assert
        Assert.DoesNotContain(argv, token => token.StartsWith("filter.", StringComparison.Ordinal));
    }

    [Fact]
    public void Build_WithConfigPins_EmitsEachPinBeforeFixedConfig()
    {
        // Arrange
        var intent = new GitIntent
        {
            Subcommand = "stash",
            RepoRoot = Path.GetTempPath(),
            ConfigPins = [("diff.noprefix", "false"), ("diff.srcPrefix", "a/")],
        };

        // Act
        var (argv, _) = GitCommandBuilder.Build(intent, GitExecutable);

        // Assert
        var start = argv.IndexOf("diff.noprefix=false");
        Assert.Equal("-c", argv[start - 1]);
        Assert.Equal(["diff.noprefix=false", "-c", "diff.srcPrefix=a/"], argv.Skip(start).Take(3));
        Assert.True(start < argv.IndexOf("core.fsmonitor="));
    }

    [Fact]
    public void HardeningArgvPrefix_ContainsFixedConfigAndGlobalFlags()
    {
        // Act
        var prefix = GitCommandBuilder.HardeningArgvPrefix();

        // Assert
        Assert.Contains("-c", prefix);
        Assert.Contains("core.pager=cat", prefix);
        Assert.Contains("--no-pager", prefix);
        Assert.Contains("log.showSignature=false", prefix);
        Assert.Contains("fetch.bundleURI=", prefix);
        Assert.Contains(AutoRefreshIndexPin, prefix);
        Assert.Contains(ShowRootPin, prefix);
    }

    [Fact]
    public void MaskedForLog_ReplacesUserOriginatingTokens()
    {
        // Arrange
        var intent = BasicIntent();
        intent.AttachedOptions.Add(new AttachedOption("--author", "alice"));
        intent.Pathspecs.Add(Pathspec.Literal("src/foo.cs"));
        var (argv, _) = GitCommandBuilder.Build(intent, GitExecutable);

        // Act
        var masked = GitCommandBuilder.MaskedForLog(argv, intent);

        // Assert
        Assert.DoesNotContain("alice", string.Join(' ', masked));
        Assert.DoesNotContain("src/foo.cs", string.Join(' ', masked));
        Assert.Contains("--author=<author>", masked);
        Assert.Contains("<path>", masked);
        Assert.Contains("log.showSignature=false", masked);
    }

    [Fact]
    public void MaskedForLog_MasksFilterDriverPins()
    {
        // Arrange
        var intent = BasicIntent("status");
        var (argv, _) = GitCommandBuilder.Build(intent, GitExecutable, ["private-driver"]);

        // Act
        var masked = GitCommandBuilder.MaskedForLog(argv, intent);

        // Assert
        Assert.DoesNotContain("private-driver", string.Join(' ', masked));
        Assert.Equal(4, masked.Count(token => token == "<filter_driver>"));
    }

    [Fact]
    public void MaskedForLog_MasksGlobAndExcludePathspecsByParameter()
    {
        // Arrange
        var intent = BasicIntent("grep");
        intent.Pathspecs.Add(Pathspec.Glob("globbed/*.cs"));
        intent.Pathspecs.Add(Pathspec.Exclude("excluded-dir"));
        var (argv, _) = GitCommandBuilder.Build(intent, GitExecutable);

        // Act
        var masked = GitCommandBuilder.MaskedForLog(argv, intent);

        // Assert
        var joined = string.Join(' ', masked);
        Assert.DoesNotContain("globbed", joined);
        Assert.DoesNotContain("excluded-dir", joined);
        Assert.Contains("<glob>", masked);
        Assert.Contains("<exclude>", masked);
    }

    [Fact]
    public void MaskedForLog_Masks_Reconstructed_Range_Token_Without_Leaking_Shas_Or_Operator()
    {
        const string rangeToken = "aaaa111122223333444455556666777788889999...bbbb0000cccc1111dddd2222eeee3333ffff4444";
        var intent = BasicIntent();
        intent.VerifiedRefs.Add(rangeToken);
        var (argv, _) = GitCommandBuilder.Build(intent, GitExecutable);
        var masked = GitCommandBuilder.MaskedForLog(argv, intent);
        var joined = string.Join(' ', masked);

        Assert.Contains("<ref>", masked);
        Assert.DoesNotContain(rangeToken, masked);
        Assert.DoesNotContain("aaaa111122223333444455556666777788889999", joined);
        Assert.DoesNotContain("bbbb0000cccc1111dddd2222eeee3333ffff4444", joined);
    }

    [Fact]
    public void MaskedForLog_ShowsGitInPlaceOfExecutablePath()
    {
        // Arrange
        var intent = BasicIntent();
        var (argv, _) = GitCommandBuilder.Build(intent, GitExecutable);

        // Act
        var masked = GitCommandBuilder.MaskedForLog(argv, intent);

        // Assert
        Assert.Equal("git", masked[0]);
        Assert.DoesNotContain(GitExecutable, string.Join(' ', masked));
    }
}