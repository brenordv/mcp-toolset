using RaccoonNinja.McpToolset.Server.GitOps.Errors.GitCheckExceptions;
using RaccoonNinja.McpToolset.Server.GitOps.Security;

namespace RaccoonNinja.McpToolset.Server.GitOps.Tests.Security;

public class GitCommandBuilderTests
{
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
        var (argv, _) = GitCommandBuilder.Build(BasicIntent());

        // Assert
        Assert.Equal("git", argv[0]);
        Assert.Contains("-c", argv);
        Assert.Contains("core.fsmonitor=", argv);
        Assert.Contains("core.hooksPath=/nonexistent/git-check-hooks-disabled", argv);
        Assert.Contains("--no-pager", argv);
        Assert.Contains("--literal-pathspecs", argv);
    }

    [Fact]
    public void Build_AddsDashCBlockWithRepoRoot()
    {
        // Arrange
        var intent = BasicIntent();

        // Act
        var (argv, _) = GitCommandBuilder.Build(intent);

        // Assert
        var index = argv.IndexOf("-C");
        Assert.Equal(intent.RepoRoot, argv[index + 1]);
    }

    [Fact]
    public void Build_AddsDiffHardeningForDiffBearingSubcommands()
    {
        // Act
        var (argv, _) = GitCommandBuilder.Build(BasicIntent("diff"));

        // Assert
        Assert.Contains("--no-ext-diff", argv);
        Assert.Contains("--no-textconv", argv);
    }

    [Fact]
    public void Build_OmitsExtDiffForGrep()
    {
        // Act
        var (argv, _) = GitCommandBuilder.Build(BasicIntent("grep"));

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
        var (argv, _) = GitCommandBuilder.Build(intent);

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
        var (argv, _) = GitCommandBuilder.Build(intent);

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
        Assert.Throws<RejectedArgumentException>(() => GitCommandBuilder.Build(intent));
    }

    [Fact]
    public void Build_AlwaysInsertsEndOfOptionsBarrierBeforeVerifiedRefs()
    {
        // Arrange
        var intent = BasicIntent();
        intent.VerifiedRefs.Add("abc1234567");

        // Act
        var (argv, _) = GitCommandBuilder.Build(intent);

        // Assert
        var barrierIdx = argv.IndexOf("--end-of-options");
        var refIdx = argv.IndexOf("abc1234567");
        Assert.True(barrierIdx < refIdx);
    }

    [Fact]
    public void Build_SeparatesPathspecsWithDoubleDash()
    {
        // Arrange
        var intent = BasicIntent();
        intent.Pathspecs.Add("src/foo.cs");

        // Act
        var (argv, _) = GitCommandBuilder.Build(intent);

        // Assert
        var dashIdx = argv.IndexOf("--");
        var pathIdx = argv.IndexOf("src/foo.cs");
        Assert.True(dashIdx > 0 && pathIdx > dashIdx);
    }

    [Fact]
    public void Build_RejectsPathspecThatStartsWithDash()
    {
        // Arrange
        var intent = BasicIntent();
        intent.Pathspecs.Add("-rm-rf");

        // Act & Assert
        Assert.Throws<RejectedArgumentException>(() => GitCommandBuilder.Build(intent));
    }

    [Fact]
    public void Build_ReturnsEnvWithFixedGitNeutralizersAndAllowlistedPATH()
    {
        // Act
        var (_, env) = GitCommandBuilder.Build(BasicIntent());

        // Assert
        Assert.Equal("0", env["GIT_TERMINAL_PROMPT"]);
        Assert.Equal("1", env["GIT_CONFIG_NOSYSTEM"]);
        Assert.Equal("0", env["GIT_OPTIONAL_LOCKS"]);
        Assert.Equal("1", env["GIT_LITERAL_PATHSPECS"]);
        Assert.True(env.ContainsKey("PATH"));
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
    }

    [Fact]
    public void MaskedForLog_ReplacesUserOriginatingTokens()
    {
        // Arrange
        var intent = BasicIntent();
        intent.AttachedOptions.Add(new AttachedOption("--author", "alice"));
        intent.Pathspecs.Add("src/foo.cs");
        var (argv, _) = GitCommandBuilder.Build(intent);

        // Act
        var masked = GitCommandBuilder.MaskedForLog(argv, intent);

        // Assert
        Assert.DoesNotContain("alice", string.Join(' ', masked));
        Assert.DoesNotContain("src/foo.cs", string.Join(' ', masked));
        Assert.Contains("--author=<author>", masked);
        Assert.Contains("<path>", masked);
    }
}