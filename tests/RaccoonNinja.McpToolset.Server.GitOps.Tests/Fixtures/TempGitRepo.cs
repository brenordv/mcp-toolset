using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;

namespace RaccoonNinja.McpToolset.Server.GitOps.Tests.Fixtures;

/// <summary>
/// A fresh, empty temp git repository (branch <c>main</c>, local identity, no signing). A test that needs
/// a history of its own builds it with <see cref="WriteFile"/>, <see cref="WriteBytes"/>, and
/// <see cref="Run"/>; <see cref="GitRepoFixture"/> builds the shared linear history on top of it.
/// </summary>
internal sealed class TempGitRepo : IDisposable
{
    /// <summary>The key that sets the encoding <c>git log</c> and <c>git show</c> print commit records in.</summary>
    public const string LogOutputEncodingKey = "i18n.logOutputEncoding";

    /// <summary>The key git uses for <see cref="LogOutputEncodingKey"/> when that key is unset.</summary>
    public const string CommitEncodingKey = "i18n.commitEncoding";

    /// <summary>The message of the commit that makes <c>a.txt</c> executable and changes nothing else.</summary>
    public const string ModeChangeMessage = "make a.txt executable";

    /// <summary>The author and committer name the repository is configured with.</summary>
    public const string IdentityName = "Test User";

    /// <summary>The name <see cref="CommitMailmap"/> maps <see cref="IdentityName"/>'s email to.</summary>
    public const string MailmapName = "Mapped Name";

    /// <summary>The id the record <see cref="CommitWithForgedLogRecord"/> forges claims; no commit has it.</summary>
    public const string ForgedRecordCommitId = "0123456789abcdef0123456789abcdef01234567";

    /// <summary>
    /// The body <see cref="CommitWithForgedLogRecord"/> commits: text, then a whole <c>git log</c> record in 0x1E/0x1F
    /// framing (id, short id, no parents, author, email, two times, subject, body).
    /// </summary>
    public const string ForgedRecordBody =
        "real body\u001e" + ForgedRecordCommitId
        + "\u001f0123456\u001f\u001fForged Author\u001fforged@example.com\u001f1000000000\u001f1000000000"
        + "\u001fforged subject\u001fforged body";

    /// <summary>The email <see cref="CommitWithAuthorName"/> commits under.</summary>
    public const string SeparatorAuthorEmail = "evil@example.com";

    /// <summary>A commit subject holding 0x1F, then digits an old parser would read as the entry's time.</summary>
    public const string ShiftingSubject = "shift\u001f1000000000";

    /// <summary>How long one git invocation may run before the helper kills it and fails the test.</summary>
    private const int GitTimeoutMilliseconds = 15_000;

    /// <summary>The author and committer email the repository is configured with.</summary>
    private const string IdentityEmail = "tester@example.com";

    /// <summary>Where <see cref="CommitWithForgedSignature"/> writes the commit text it hashes; inside <c>.git</c>, so the worktree stays clean.</summary>
    private const string ForgedCommitFile = ".git/forged-commit";

    /// <summary>An armor body that is not a real signature. git hands it to the verifier all the same.</summary>
    private const string ForgedSignatureBody = "Zm9yZ2VkIHNpZ25hdHVyZQ==";

    /// <summary>Where <see cref="ConfigureSignatureProgramTrap"/> writes the script <c>gpg.program</c> names.</summary>
    private const string TrapScriptFile = ".git/fake-gpg";

    /// <summary>Where <see cref="ConfigureLazyFetchTrap"/> writes the script <c>remote.origin.uploadpack</c> names.</summary>
    private const string UploadPackTrapFile = ".git/fake-upload-pack";

    /// <summary>Where <see cref="ArmFilterDriverTrap"/> writes the script a filter driver's <c>clean</c> names, under a repository's <c>.git</c>.</summary>
    private const string FilterTrapFile = ".git/fake-filter";

    /// <summary>The one file <see cref="AddSubmoduleWithFilterDriverTrap"/> commits in the submodule.</summary>
    private const string SubmoduleFile = "s.txt";

    /// <summary>The message of the commit that records a submodule's first gitlink.</summary>
    private const string SubmoduleAddMessage = "add submodule";

    /// <summary>The file <see cref="StageIgnoredSubmoduleMove"/> declares its submodule in.</summary>
    private const string GitmodulesFile = ".gitmodules";

    /// <summary>The index mode of a gitlink, the entry a submodule occupies in its superproject.</summary>
    private const string GitlinkMode = "160000";

    /// <summary>
    /// The commit <see cref="StageIgnoredSubmoduleMove"/> first records for its gitlink. A gitlink names a commit in
    /// another repository, so any 40-hex value works.
    /// </summary>
    private const string SubmoduleOldCommit = "1111111111111111111111111111111111111111";

    /// <summary>The commit <see cref="StageIgnoredSubmoduleMove"/> stages for its gitlink.</summary>
    private const string SubmoduleNewCommit = "2222222222222222222222222222222222222222";

    /// <summary>An encoding that is not ASCII-compatible, so a record git re-encodes into it holds no readable commit id.</summary>
    private const string Utf16Encoding = "UTF-16";

    /// <summary>The file git reads a mailmap from at the top of the working tree.</summary>
    private const string MailmapFile = ".mailmap";

    /// <summary>The message of the commit <see cref="CommitMailmap"/> makes.</summary>
    private const string MailmapCommitMessage = "add mailmap";

    /// <summary>Where <see cref="ReplaceBlob"/> writes the replacement content it hashes; inside <c>.git</c>, so the worktree stays clean.</summary>
    private const string ReplacementBlobFile = ".git/replacement-blob";

    /// <summary>The file git reads grafts from when <c>GIT_GRAFT_FILE</c> is unset.</summary>
    private const string GraftsFile = ".git/info/grafts";

    /// <summary>The subject of the commit <see cref="CommitWithForgedLogRecord"/> makes.</summary>
    private const string ForgedRecordSubject = "forged record";

    /// <summary>Where <see cref="CommitWithForgedLogRecord"/> writes the message it commits; inside <c>.git</c>, so the worktree stays clean.</summary>
    private const string ForgedRecordMessageFile = ".git/forged-record-message";

    /// <summary>The subject of the commit <see cref="CommitWithAuthorName"/> makes.</summary>
    private const string AuthorNameSubject = "author name";

    /// <summary>The message of the commit <see cref="CommitBinaryChange"/> makes.</summary>
    private const string BinaryChangeMessage = "change binary file";

    /// <summary>How <c>git diff --numstat</c> starts the line of a file it reads as binary: <c>-</c> for both counts.</summary>
    private const string BinaryNumstatPrefix = "-\t-\t";

    /// <summary>The full name of the branch the repository starts on.</summary>
    private const string MainBranchRef = "refs/heads/main";

    /// <summary>The <c>git for-each-ref</c> format that prints a branch's upstream, shortened as <c>git_branch_list</c> asks for it.</summary>
    private const string UpstreamShortFormat = "--format=%(upstream:short)";

    /// <summary>Appended to the script's path to name the marker file the script creates when it runs.</summary>
    private const string TrapMarkerSuffix = ".ran";

    /// <summary>Where <see cref="CommitTreeWithEntryName"/> writes the blob content it hashes; inside <c>.git</c>, so the worktree stays clean.</summary>
    private const string EntryBlobFile = ".git/entry-blob";

    /// <summary>Where <see cref="CommitTreeWithEntryName"/> writes the raw tree object it hashes.</summary>
    private const string EntryTreeFile = ".git/entry-tree";

    /// <summary>The tree-entry mode of a regular, non-executable file.</summary>
    private const string RegularFileTreeMode = "100644";

    /// <summary>The message of the commit <see cref="CommitTreeWithEntryName"/> makes.</summary>
    private const string EntryCommitMessage = "tree with one crafted entry";

    /// <summary>The key that turns on <c>git grep</c>'s column field by default.</summary>
    private const string GrepColumnKey = "grep.column";

    /// <summary>How many NULs a <c>git grep -n -z</c> match carries with a column field: after the path, the line number, and the column.</summary>
    private const int GrepColumnNulCount = 3;

    /// <summary>The message of the commit <see cref="CommitBinaryFileHolding"/> makes.</summary>
    private const string BinaryMatchMessage = "add binary file";

    /// <summary>What <c>git grep</c> prints before a binary file's name when it matches.</summary>
    private const string BinaryLinePrefix = "Binary file ";

    /// <summary>What <c>git grep</c> prints after a binary file's name when it matches.</summary>
    private const string BinaryLineSuffix = " matches";

    /// <summary>The key that sets a repository's work tree.</summary>
    private const string WorkTreeKey = "core.worktree";

    /// <summary>The name of the file or directory git discovers a repository by.</summary>
    private const string GitFileName = ".git";

    /// <summary>
    /// The script a trap points git at: it creates its own path plus <see cref="TrapMarkerSuffix"/>, writes one line to
    /// stderr, and exits 1. LF endings, so the shebang line reads <c>/bin/sh</c>.
    /// </summary>
    private const string TrapScript = $"#!/bin/sh\n: > \"$0{TrapMarkerSuffix}\"\necho \"trap program ran\" >&2\nexit 1\n";

    /// <summary>
    /// How far <see cref="ArmFilterDriverTrap"/> and <see cref="MakeStatDirty"/> move a tracked file's modification
    /// time, so it no longer matches the time the index recorded and git hashes the file again.
    /// </summary>
    private static readonly TimeSpan StatDirtyShift = TimeSpan.FromMinutes(-10);

    /// <summary>The bytes <see cref="CommitBinaryChange"/> writes: a NUL makes git read the file as binary.</summary>
    private static readonly byte[] BinaryChangeContent = [0, 4, 5, 0, 6];

    public TempGitRepo()
    {
        var created = Path.Combine(Path.GetTempPath(), "mcp-gitops-temp-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(created);
        RepoPath = RealPath(created);

        Run("init", "-b", "main");
        Run("config", "user.email", IdentityEmail);
        Run("config", "user.name", IdentityName);
        Run("config", "commit.gpgsign", "false");
    }

    /// <summary>The canonical absolute path of the repository root, as git reports it.</summary>
    public string RepoPath { get; }

    /// <summary>Run git with <paramref name="args"/> in the repository.</summary>
    /// <param name="args">The git arguments, one per element.</param>
    /// <returns>git's standard output without its trailing newline. It is read after git exits, so this suits commands with short output.</returns>
    /// <exception cref="InvalidOperationException">Thrown when git cannot start or exits non-zero; the message carries git's stderr and stdout.</exception>
    /// <exception cref="TimeoutException">Thrown when git does not exit within the timeout; the process is killed first.</exception>
    public string Run(params string[] args)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "git",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = RepoPath,
        };
        foreach (var arg in args)
        {
            psi.ArgumentList.Add(arg);
        }

        using var process = Process.Start(psi) ?? throw new InvalidOperationException("failed to start git");
        process.StandardInput.Close();
        if (!process.WaitForExit(GitTimeoutMilliseconds))
        {
            TryKill(process);
            throw new TimeoutException($"git {string.Join(' ', args)} did not exit within {GitTimeoutMilliseconds} ms");
        }

        if (process.ExitCode != 0)
        {
            var stderr = process.StandardError.ReadToEnd();
            var stdout = process.StandardOutput.ReadToEnd();
            throw new InvalidOperationException($"git {string.Join(' ', args)} failed ({process.ExitCode}): {stderr} {stdout}");
        }

        return process.StandardOutput.ReadToEnd().TrimEnd('\n');
    }

    /// <summary>Write a text file at the <c>/</c>-separated <paramref name="relativePath"/>, creating parent directories.</summary>
    public void WriteFile(string relativePath, string content)
        => File.WriteAllText(PrepareTarget(relativePath), content);

    /// <summary>Write raw bytes at the <c>/</c>-separated <paramref name="relativePath"/>, creating parent directories.</summary>
    public void WriteBytes(string relativePath, byte[] content)
        => File.WriteAllBytes(PrepareTarget(relativePath), content);

    /// <summary>
    /// Merge <paramref name="branch"/> into the current branch where the merge is expected to stop on a conflict, leaving
    /// the conflicted paths unmerged in the index and marked up in the working tree.
    /// </summary>
    /// <param name="branch">The branch to merge.</param>
    /// <exception cref="InvalidOperationException">Thrown when the merge leaves no unmerged path.</exception>
    /// <exception cref="TimeoutException">Thrown when git does not exit within the timeout.</exception>
    public void MergeLeavingConflicts(string branch)
    {
        try
        {
            Run("merge", "--no-edit", branch);
        }
        catch (InvalidOperationException)
        {
            // git merge exits 1 when it stops on a conflict.
        }

        if (Run("ls-files", "--unmerged").Length == 0)
        {
            throw new InvalidOperationException($"merging {branch} left no unmerged path");
        }
    }

    /// <summary>
    /// Move <paramref name="trackedPath"/>'s modification time by <see cref="StatDirtyShift"/>, so the stat data the
    /// index recorded no longer matches the file while its content still does, then check that <c>git diff-files</c>
    /// lists the file. <c>git diff-files</c> reports such a file as changed and never writes the index, so the check
    /// leaves the file stat-dirty for the call under test.
    /// </summary>
    /// <param name="trackedPath">A <c>/</c>-separated path committed at <c>HEAD</c>, unchanged in the worktree.</param>
    /// <exception cref="InvalidOperationException">Thrown when git fails or does not list the file.</exception>
    /// <exception cref="TimeoutException">Thrown when git does not exit within the timeout.</exception>
    public void MakeStatDirty(string trackedPath)
    {
        var file = PrepareTarget(trackedPath);
        File.SetLastWriteTimeUtc(file, File.GetLastWriteTimeUtc(file) + StatDirtyShift);
        if (Run("diff-files", "--name-only", "--", trackedPath) != trackedPath)
        {
            throw new InvalidOperationException($"git diff-files does not list {trackedPath} after its modification time moved");
        }
    }

    /// <summary>
    /// Set <c>log.showRoot=false</c> in the repository's config, as a hostile repository or a user's own config can,
    /// then check that <c>git show</c> without the server's pin prints no diff for <c>HEAD</c>'s root commit, so a test
    /// built on it never passes on a git that ignores the key. Call it once <c>HEAD</c> has exactly one root commit.
    /// </summary>
    /// <returns>The id of <c>HEAD</c>'s root commit.</returns>
    /// <exception cref="InvalidOperationException">Thrown when git fails or still prints the root commit's diff.</exception>
    /// <exception cref="TimeoutException">Thrown when git does not exit within the timeout.</exception>
    public string ConfigureShowRootOff()
    {
        var root = Run("rev-list", "--max-parents=0", "HEAD");
        Run("config", "log.showRoot", "false");
        if (Run("show", "--format=", "--name-only", root).Length != 0)
        {
            throw new InvalidOperationException("git show still prints the root commit's diff with log.showRoot=false");
        }

        return root;
    }

    /// <summary>
    /// Set <paramref name="configKey"/> to <c>UTF-16</c> in the repository's config, as a hostile repository or a user's
    /// own config can, then check that <c>git log</c> without the server's pin no longer prints exactly <c>HEAD</c>'s id,
    /// so a test built on it never passes on a git that ignores the key. The check keeps <c>--format=%H</c>: git writes
    /// that format's newline after the re-encoded record, as one byte the UTF-16 decoder cannot pair, so even a reader
    /// that detects the byte-order mark gets more than the id. Call it once every commit the test needs exists: a commit
    /// made under <see cref="CommitEncodingKey"/> records that encoding.
    /// </summary>
    /// <param name="configKey"><see cref="LogOutputEncodingKey"/> or <see cref="CommitEncodingKey"/>.</param>
    /// <returns>The id of <c>HEAD</c>.</returns>
    /// <exception cref="InvalidOperationException">Thrown when git fails or still prints exactly <c>HEAD</c>'s id.</exception>
    /// <exception cref="TimeoutException">Thrown when git does not exit within the timeout.</exception>
    public string ConfigureUtf16LogEncoding(string configKey)
    {
        var head = Run("rev-parse", "HEAD");
        Run("config", configKey, Utf16Encoding);
        if (Run("log", "--max-count=1", "--format=%H") == head)
        {
            throw new InvalidOperationException($"git log still prints exactly the commit id with {configKey}={Utf16Encoding}");
        }

        return head;
    }

    /// <summary>
    /// Commit a <c>.mailmap</c> that maps <see cref="IdentityName"/>'s email to <see cref="MailmapName"/>, as a hostile
    /// repository can, then check that <c>git log --author</c> without the server's pin no longer matches
    /// <see cref="IdentityName"/>, so a test built on it never passes on a git that does not apply the mailmap.
    /// </summary>
    /// <exception cref="InvalidOperationException">Thrown when git fails or still matches the recorded name.</exception>
    /// <exception cref="TimeoutException">Thrown when git does not exit within the timeout.</exception>
    public void CommitMailmap()
    {
        WriteFile(MailmapFile, $"{MailmapName} <{IdentityEmail}>\n");
        Run("add", MailmapFile);
        Run("commit", "-m", MailmapCommitMessage);
        if (Run("log", "--format=%H", $"--author={IdentityName}").Length != 0)
        {
            throw new InvalidOperationException("git log still matches the recorded author with a .mailmap that renames it");
        }
    }

    /// <summary>
    /// Make the blob <paramref name="trackedPath"/> has at <c>HEAD</c> read as <paramref name="replacementContent"/>
    /// through a replace ref, as a hostile repository's <c>.git</c> can carry one, then check that <c>git cat-file -p</c>
    /// without the server's environment returns the replacement, so a test built on it never passes on a git that
    /// ignores replace refs.
    /// </summary>
    /// <param name="trackedPath">A <c>/</c>-separated path committed at <c>HEAD</c>.</param>
    /// <param name="replacementContent">The content the blob should read as.</param>
    /// <returns>The replaced blob's id, the one <c>HEAD</c>'s tree records.</returns>
    /// <exception cref="InvalidOperationException">Thrown when git fails or does not return the replacement.</exception>
    /// <exception cref="TimeoutException">Thrown when git does not exit within the timeout.</exception>
    public string ReplaceBlob(string trackedPath, string replacementContent)
    {
        var blob = Run("rev-parse", $"HEAD:{trackedPath}");
        WriteFile(ReplacementBlobFile, replacementContent);
        var replacement = Run("hash-object", "-w", ReplacementBlobFile);
        Run("replace", blob, replacement);
        if (Run("cat-file", "-p", blob) != replacementContent.TrimEnd('\n'))
        {
            throw new InvalidOperationException("git cat-file does not return the replacement blob through the replace ref");
        }

        return blob;
    }

    /// <summary>
    /// Make <c>HEAD</c>'s commit read as a parentless commit whose message is <paramref name="forgedSubject"/>, through a
    /// replace ref, then check that <c>git log</c> without the server's environment prints the forged subject.
    /// </summary>
    /// <param name="forgedSubject">The message of the replacement commit.</param>
    /// <returns>The id of <c>HEAD</c>'s commit, the one the replace ref covers.</returns>
    /// <exception cref="InvalidOperationException">Thrown when git fails or does not show the replacement.</exception>
    /// <exception cref="TimeoutException">Thrown when git does not exit within the timeout.</exception>
    public string ReplaceHeadCommit(string forgedSubject)
    {
        var head = Run("rev-parse", "HEAD");
        var forged = Run("commit-tree", "HEAD^{tree}", "-m", forgedSubject);
        Run("replace", head, forged);
        if (Run("log", "--max-count=1", "--format=%s") != forgedSubject)
        {
            throw new InvalidOperationException("git log does not show the replacement commit through the replace ref");
        }

        return head;
    }

    /// <summary>
    /// Write a <c>.git/info/grafts</c> line that gives <c>HEAD</c>'s commit no parents, as a hostile repository's
    /// <c>.git</c> can carry one, then check that <c>git rev-list --count HEAD</c> without the server's environment counts
    /// one commit. Call it once <c>HEAD</c> has a parent.
    /// </summary>
    /// <exception cref="InvalidOperationException">Thrown when git fails or still walks past <c>HEAD</c>.</exception>
    /// <exception cref="TimeoutException">Thrown when git does not exit within the timeout.</exception>
    public void GraftHeadAsRoot()
    {
        WriteFile(GraftsFile, Run("rev-parse", "HEAD") + "\n");
        if (Run("rev-list", "--count", "HEAD") != "1")
        {
            throw new InvalidOperationException("git rev-list still walks past HEAD with a graft that gives it no parents");
        }
    }

    /// <summary>
    /// Commit, with whatever is staged, a commit whose body is <see cref="ForgedRecordBody"/>, as a hostile repository's
    /// history can carry one, then check that <c>git log</c> prints that body with its 0x1E and 0x1F bytes, so a test built
    /// on it never passes on a git that drops them.
    /// </summary>
    /// <returns>The new commit's id.</returns>
    /// <exception cref="InvalidOperationException">Thrown when git fails or prints another body.</exception>
    /// <exception cref="TimeoutException">Thrown when git does not exit within the timeout.</exception>
    public string CommitWithForgedLogRecord()
    {
        WriteFile(ForgedRecordMessageFile, $"{ForgedRecordSubject}\n\n{ForgedRecordBody}\n");
        Run("commit", "--allow-empty", "-F", ForgedRecordMessageFile);
        if (Run("log", "--max-count=1", "--format=%b") != ForgedRecordBody)
        {
            throw new InvalidOperationException("git log does not print the forged record's separator bytes in the body");
        }

        return Run("rev-parse", "HEAD");
    }

    /// <summary>
    /// Commit, with whatever is staged, a commit authored as <paramref name="authorName"/> and
    /// <see cref="SeparatorAuthorEmail"/>, then check that <c>git log</c> prints the name unchanged, so a test that passes
    /// a name holding 0x1E or 0x1F never passes on a git that strips those bytes.
    /// </summary>
    /// <param name="authorName">The author name to record.</param>
    /// <returns>The new commit's id.</returns>
    /// <exception cref="InvalidOperationException">Thrown when git fails or prints another name.</exception>
    /// <exception cref="TimeoutException">Thrown when git does not exit within the timeout.</exception>
    public string CommitWithAuthorName(string authorName)
    {
        Run("commit", "--allow-empty", "-m", AuthorNameSubject, $"--author={authorName} <{SeparatorAuthorEmail}>");
        if (Run("log", "--max-count=1", "--format=%an") != authorName)
        {
            throw new InvalidOperationException("git log does not print the author name as committed");
        }

        return Run("rev-parse", "HEAD");
    }

    /// <summary>
    /// Commit an empty commit whose subject is <paramref name="subject"/>, then check that the <c>HEAD</c> reflog records
    /// <c>commit: </c> plus that subject unchanged, so a test that passes a subject holding 0x1F never passes on a git that
    /// strips the byte.
    /// </summary>
    /// <param name="subject">The commit subject.</param>
    /// <returns>The commit's author time, in seconds since the Unix epoch, as <c>git log</c> prints it.</returns>
    /// <exception cref="InvalidOperationException">Thrown when git fails or the reflog records another message.</exception>
    /// <exception cref="TimeoutException">Thrown when git does not exit within the timeout.</exception>
    public long CommitWithReflogSubject(string subject)
    {
        Run("commit", "--allow-empty", "-m", subject);
        if (Run("reflog", "show", "--max-count=1", "--format=%gs") != $"commit: {subject}")
        {
            throw new InvalidOperationException("the HEAD reflog does not record the commit subject as committed");
        }

        return long.Parse(Run("log", "--max-count=1", "--format=%at"), CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// Overwrite <paramref name="trackedPath"/> with other bytes holding a NUL and commit it with whatever else is staged,
    /// then check that <c>git diff --numstat HEAD~1 HEAD</c> prints git's binary mark (<c>-</c> for both counts) for it,
    /// so a test built on it never passes on a git that reads the file as text.
    /// </summary>
    /// <param name="trackedPath">A <c>/</c>-separated path committed at <c>HEAD</c>.</param>
    /// <exception cref="InvalidOperationException">Thrown when git fails or does not mark the file binary.</exception>
    /// <exception cref="TimeoutException">Thrown when git does not exit within the timeout.</exception>
    public void CommitBinaryChange(string trackedPath)
    {
        WriteBytes(trackedPath, BinaryChangeContent);
        Run("add", trackedPath);
        Run("commit", "-m", BinaryChangeMessage);
        if (!Run("diff", "--numstat", "HEAD~1", "HEAD", "--", trackedPath).StartsWith(BinaryNumstatPrefix, StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"git diff --numstat does not mark {trackedPath} binary");
        }
    }

    /// <summary>
    /// Make <c>main</c> track a branch of this repository, as a repository's own config can: <c>branch.main.remote</c>
    /// is <c>.</c> and <c>branch.main.merge</c> is <paramref name="merge"/>. Then check that <c>git for-each-ref</c>
    /// prints <paramref name="merge"/> unchanged as <c>main</c>'s <c>%(upstream:short)</c>, so a test that passes a
    /// value holding a newline or 0x1F never passes on a git that checks or rewrites it.
    /// </summary>
    /// <param name="merge">The value; one git prints unshortened, so not starting with <c>refs/</c>.</param>
    /// <exception cref="InvalidOperationException">
    /// Thrown when git fails or prints another upstream; the message carries what git printed, JSON-escaped.
    /// </exception>
    /// <exception cref="TimeoutException">Thrown when git does not exit within the timeout.</exception>
    public void ConfigureLocalUpstream(string merge)
    {
        Run("config", "branch.main.remote", ".");
        Run("config", "branch.main.merge", merge);
        var printed = Run("for-each-ref", UpstreamShortFormat, MainBranchRef);
        if (printed != merge)
        {
            throw new InvalidOperationException(
                $"git for-each-ref printed {JsonSerializer.Serialize(printed)} as main's upstream, not branch.main.merge unchanged");
        }
    }

    /// <summary>
    /// Point the current branch at a new commit on top of <c>HEAD</c>, with the same tree, whose <c>gpgsig</c> header holds
    /// a PGP-armored block that is not a real signature. git treats any commit with that header as signed and hands the
    /// block to the verifier when it checks signatures, so no signing program is needed. SHA-1 repositories only; a
    /// SHA-256 one reads <c>gpgsig-sha256</c>.
    /// </summary>
    /// <param name="message">The commit message, also used as the reflog message for the branch update.</param>
    /// <returns>The new commit's id.</returns>
    public string CommitWithForgedSignature(string message)
    {
        var tree = Run("rev-parse", "HEAD^{tree}");
        var parent = Run("rev-parse", "HEAD");
        var identity = $"{IdentityName} <{IdentityEmail}> {DateTimeOffset.UtcNow.ToUnixTimeSeconds()} +0000";
        WriteFile(
            ForgedCommitFile,
            $"tree {tree}\nparent {parent}\nauthor {identity}\ncommitter {identity}\n"
            + $"gpgsig -----BEGIN PGP SIGNATURE-----\n \n {ForgedSignatureBody}\n -----END PGP SIGNATURE-----\n"
            + $"\n{message}\n");
        var commit = Run("hash-object", "-t", "commit", "-w", ForgedCommitFile);
        Run("update-ref", "-m", message, "HEAD", commit);
        return commit;
    }

    /// <summary>
    /// Configure the repository as a hostile one could: <c>log.showSignature=true</c>, and <c>gpg.program</c> naming a
    /// script that creates a marker file and writes one line to stderr whenever anything runs it. Then check that the trap
    /// fires by running <c>git log</c> once without the server's pin, so a trap test never passes on a machine where git
    /// cannot start the script: the calling test is skipped on Windows, where that depends on how Git for Windows was
    /// started, and fails anywhere else. Call it after <see cref="CommitWithForgedSignature"/>: the check logs
    /// <c>HEAD</c>.
    /// </summary>
    /// <returns>The marker file's path, with the file deleted again. It exists afterwards only if git ran the script.</returns>
    public string ConfigureSignatureProgramTrap()
    {
        var script = WriteTrapScript(TrapScriptFile);
        var marker = script + TrapMarkerSuffix;
        Run("config", "gpg.program", script);
        Run("config", "log.showSignature", "true");
        Run("log", "--max-count=1", "--format=%H");
        RequireTrapFired(marker, "git did not run the gpg.program script on this machine, so the signature trap cannot fire");
        File.Delete(marker);
        return marker;
    }

    /// <summary>
    /// Configure the repository as a hostile partial clone could: <c>origin</c> is a promisor remote at a local path whose
    /// <c>uploadpack</c> is a script that creates a marker file whenever anything runs it, and the blob
    /// <paramref name="trackedPath"/> has at <c>HEAD</c> is deleted from the object store, so the next read of that blob
    /// makes git start a lazy fetch from <c>origin</c>. Then check that the trap fires by listing <c>HEAD</c> with sizes
    /// once, without the server's environment, so a trap test never passes on a machine where git cannot start the script:
    /// the calling test is skipped on Windows, where git needs <c>sh</c> on its <c>PATH</c> to start it, and fails anywhere
    /// else.
    /// </summary>
    /// <param name="trackedPath">A <c>/</c>-separated path committed at <c>HEAD</c> whose blob is still a loose object.</param>
    /// <returns>The marker file's path, with the file deleted again. It exists afterwards only if git ran the script.</returns>
    public string ConfigureLazyFetchTrap(string trackedPath)
    {
        var blob = Run("rev-parse", $"HEAD:{trackedPath}");
        var script = WriteTrapScript(UploadPackTrapFile);
        var marker = script + TrapMarkerSuffix;
        Run("config", "remote.origin.url", RepoPath);
        Run("config", "remote.origin.promisor", "true");
        Run("config", "remote.origin.uploadpack", $"'{script.Replace('\\', '/')}'");

        var looseObject = Path.Combine(RepoPath, ".git", "objects", blob[..2], blob[2..]);
        File.SetAttributes(looseObject, FileAttributes.Normal);
        File.Delete(looseObject);

        Run("ls-tree", "-r", "-l", "HEAD");
        RequireTrapFired(marker, "git did not run the remote.origin.uploadpack script on this machine, so the lazy-fetch trap cannot fire");
        File.Delete(marker);
        return marker;
    }

    /// <summary>
    /// Configure the repository as a hostile one could: <c>filter.trap.clean</c> names a script that creates a marker file
    /// whenever anything runs it, <c>.git/info/attributes</c> selects the driver for every path, and
    /// <paramref name="trackedPath"/> gets a modification time the index did not record, so the next status hashes it
    /// through the filter. Then check that the trap fires by running <c>git --no-optional-locks status</c> once, without
    /// the server's pins (with optional locks, that status would write the refreshed index and the file would stop being
    /// stat-dirty), so a trap test never passes on a machine where git cannot start the script: the calling test is
    /// skipped on Windows, where git needs <c>sh</c> on its <c>PATH</c> to start it, and fails anywhere else. The check
    /// fires through <c>git status</c>, and a worktree <c>git diff</c> or a <c>git blame</c> without a revision reach the
    /// same clean conversion, so it gates those callers as well.
    /// </summary>
    /// <param name="trackedPath">A <c>/</c>-separated path committed at <c>HEAD</c>, unchanged in the worktree.</param>
    /// <returns>The marker file's path, with the file deleted again. It exists afterwards only if git ran the script.</returns>
    public string ConfigureFilterDriverTrap(string trackedPath)
    {
        var marker = ArmFilterDriverTrap(string.Empty, "trap", trackedPath);
        Run("--no-optional-locks", "status", "--porcelain");
        RequireTrapFired(marker, "git did not run the filter.trap.clean script on this machine, so the filter-driver trap cannot fire");
        File.Delete(marker);
        return marker;
    }

    /// <summary>
    /// Add a checked-out submodule at <paramref name="submodulePath"/> as a hostile repository could: a repository of its
    /// own with one committed file, recorded in this repository as a committed gitlink, whose own config and
    /// <c>.git/info/attributes</c> select a <c>filter.subtrap.clean</c> script that creates a marker file, and whose file
    /// gets a modification time the submodule's index did not record. A status here then runs <c>git status</c> inside the
    /// submodule, which hashes that file through the submodule's filter. Check that the trap fires the same way
    /// <see cref="ConfigureFilterDriverTrap"/> does; a <c>git diff</c> against the working tree starts the submodule's
    /// <c>git status</c> from the same check, so the status-based self-check gates diff callers as well.
    /// </summary>
    /// <param name="submodulePath">A <c>/</c>-separated path that does not exist yet.</param>
    /// <returns>The marker file's path, with the file deleted again. It exists afterwards only if git ran the script.</returns>
    public string AddSubmoduleWithFilterDriverTrap(string submodulePath)
    {
        Run("init", "-b", "main", submodulePath);
        Run("-C", submodulePath, "config", "user.email", IdentityEmail);
        Run("-C", submodulePath, "config", "user.name", IdentityName);
        Run("-C", submodulePath, "config", "commit.gpgsign", "false");
        WriteFile($"{submodulePath}/{SubmoduleFile}", "sub\n");
        Run("-C", submodulePath, "add", SubmoduleFile);
        Run("-C", submodulePath, "commit", "-m", "submodule");
        Run("add", submodulePath);
        Run("commit", "-m", SubmoduleAddMessage);

        var marker = ArmFilterDriverTrap($"{submodulePath}/", "subtrap", SubmoduleFile);
        Run("--no-optional-locks", "status", "--porcelain");
        RequireTrapFired(marker, "git did not run the submodule's filter.subtrap.clean script on this machine, so the submodule filter-driver trap cannot fire");
        File.Delete(marker);
        return marker;
    }

    /// <summary>
    /// Set up a submodule the way a hostile repository can hide one: an empty directory at <paramref name="path"/>, as a
    /// clone whose submodules were never initialized has; a committed <c>.gitmodules</c> that declares the submodule there
    /// with <c>ignore = all</c>; and a committed gitlink there at <see cref="SubmoduleOldCommit"/>. Then stage the gitlink
    /// at <see cref="SubmoduleNewCommit"/>, so the next commit moves the submodule. No submodule repository exists:
    /// <c>git update-index --cacheinfo</c> records a gitlink without checking the commit it names.
    /// </summary>
    /// <param name="path">A <c>/</c>-separated path that does not exist yet; also the submodule's name.</param>
    /// <returns>The commit id the staged gitlink names.</returns>
    public string StageIgnoredSubmoduleMove(string path)
    {
        Directory.CreateDirectory(PrepareTarget(path));
        WriteFile(GitmodulesFile, $"[submodule \"{path}\"]\n\tpath = {path}\n\tignore = all\n");
        Run("add", GitmodulesFile);
        Run("update-index", "--add", "--cacheinfo", $"{GitlinkMode},{SubmoduleOldCommit},{path}");
        Run("commit", "-m", SubmoduleAddMessage);
        Run("update-index", "--cacheinfo", $"{GitlinkMode},{SubmoduleNewCommit},{path}");
        return SubmoduleNewCommit;
    }

    /// <summary>
    /// Point <c>HEAD</c>'s branch at a new commit whose tree holds one file named <paramref name="entryName"/> holding
    /// <paramref name="content"/>, as a hostile repository's history can: a tree entry's name may hold a newline on any
    /// OS, so the tree is hashed as a raw object, not built from the worktree. Then check that <c>git ls-tree -z</c> lists
    /// exactly that name, so a test built on it never passes on a git that rejects or rewrites it.
    /// </summary>
    /// <param name="entryName">The entry's name: no <c>/</c> and no NUL.</param>
    /// <param name="content">The file's content.</param>
    /// <exception cref="InvalidOperationException">Thrown when git fails or lists another name.</exception>
    /// <exception cref="TimeoutException">Thrown when git does not exit within the timeout.</exception>
    public void CommitTreeWithEntryName(string entryName, string content)
    {
        WriteFile(EntryBlobFile, content);
        var blob = Run("hash-object", "-w", EntryBlobFile);
        WriteBytes(EntryTreeFile, [.. Encoding.UTF8.GetBytes($"{RegularFileTreeMode} {entryName}\0"), .. Convert.FromHexString(blob)]);
        var tree = Run("hash-object", "-t", "tree", "-w", EntryTreeFile);
        Run("update-ref", "HEAD", Run("commit-tree", tree, "-m", EntryCommitMessage));
        if (Run("ls-tree", "-z", "--name-only", "HEAD") != entryName + "\0")
        {
            throw new InvalidOperationException("git ls-tree does not list the crafted entry name unchanged");
        }
    }

    /// <summary>
    /// Set <c>grep.column=true</c> in the repository's config, as a hostile repository or a user's own config can, then
    /// check that <c>git grep -n -z</c> without the server's pin prints a column field (three NULs on the first match in
    /// <paramref name="trackedPath"/>), so a test built on it never passes on a git that does not read the key.
    /// </summary>
    /// <param name="pattern">A fixed string that matches a line of <paramref name="trackedPath"/>.</param>
    /// <param name="trackedPath">A <c>/</c>-separated path committed at <c>HEAD</c>.</param>
    /// <exception cref="InvalidOperationException">Thrown when git fails or prints no column field.</exception>
    /// <exception cref="TimeoutException">Thrown when git does not exit within the timeout.</exception>
    public void ConfigureGrepColumnOn(string pattern, string trackedPath)
    {
        Run("config", GrepColumnKey, "true");
        var firstMatch = Run("grep", "-n", "-z", "--no-color", "-F", "-e", pattern, "--", trackedPath).Split('\n')[0];
        if (firstMatch.Count(character => character == '\0') != GrepColumnNulCount)
        {
            throw new InvalidOperationException($"git grep -n -z prints no column field with {GrepColumnKey}=true");
        }
    }

    /// <summary>
    /// Commit <paramref name="path"/> as bytes git reads as binary (a NUL first) holding <paramref name="pattern"/> on a
    /// line, then check that <c>git grep -n -z</c> without the server's <c>-I</c> prints git's binary match line for it, so
    /// a test built on it never passes on a git that reads the file as text.
    /// </summary>
    /// <param name="path">A <c>/</c>-separated path that does not exist yet.</param>
    /// <param name="pattern">A fixed string the file holds on a line of its own.</param>
    /// <exception cref="InvalidOperationException">Thrown when git fails or prints no binary match line.</exception>
    /// <exception cref="TimeoutException">Thrown when git does not exit within the timeout.</exception>
    public void CommitBinaryFileHolding(string path, string pattern)
    {
        WriteBytes(path, [0, .. Encoding.UTF8.GetBytes(pattern + "\n")]);
        Run("add", path);
        Run("commit", "-m", BinaryMatchMessage);
        if (Run("grep", "-n", "-z", "--no-color", "-F", "-e", pattern, "--", path) != BinaryLinePrefix + path + BinaryLineSuffix)
        {
            throw new InvalidOperationException($"git grep does not print a binary match line for {path}");
        }
    }

    /// <summary>
    /// Set <c>core.worktree</c> to <paramref name="workTree"/> in the config of the repository at
    /// <paramref name="repositoryPath"/>, as a repository that arrives with its <c>.git</c> can, then check that
    /// <c>git rev-parse --show-toplevel</c> there reports that directory, so a test built on it never passes on a git that
    /// ignores the key.
    /// </summary>
    /// <param name="repositoryPath"><c>.</c> for this repository, or a <c>/</c>-separated path of one inside it.</param>
    /// <param name="workTree">The absolute directory to name.</param>
    /// <exception cref="InvalidOperationException">Thrown when git fails or reports another top level.</exception>
    /// <exception cref="TimeoutException">Thrown when git does not exit within the timeout.</exception>
    public void ConfigureWorkTree(string repositoryPath, string workTree)
    {
        Run("-C", repositoryPath, "config", WorkTreeKey, workTree);
        if (Path.GetFullPath(Run("-C", repositoryPath, "rev-parse", "--show-toplevel")) != Path.GetFullPath(workTree))
        {
            throw new InvalidOperationException($"git rev-parse --show-toplevel does not report the directory {WorkTreeKey} names");
        }
    }

    /// <summary>
    /// Add the repository at <paramref name="sourceRepoPath"/> as a submodule at <paramref name="submodulePath"/> with
    /// <c>git submodule add</c> and commit it. git keeps the submodule's git directory under <c>.git/modules/</c> and
    /// points it back at the checkout with <c>core.worktree</c>; check that the submodule's config sets that key, so a test
    /// built on it covers that layout. <c>protocol.file.allow=always</c> lets this one command clone from a local path.
    /// </summary>
    /// <param name="sourceRepoPath">The absolute path of a repository with at least one commit.</param>
    /// <param name="submodulePath">A <c>/</c>-separated path that does not exist yet.</param>
    /// <exception cref="InvalidOperationException">Thrown when git fails or the submodule's config sets no <c>core.worktree</c>.</exception>
    /// <exception cref="TimeoutException">Thrown when git does not exit within the timeout.</exception>
    public void AddSubmoduleFrom(string sourceRepoPath, string submodulePath)
    {
        Run("-c", "protocol.file.allow=always", "submodule", "add", sourceRepoPath.Replace('\\', '/'), submodulePath);
        Run("commit", "-m", SubmoduleAddMessage);
        if (Run("-C", submodulePath, "config", WorkTreeKey).Length == 0)
        {
            throw new InvalidOperationException($"the submodule's git directory sets no {WorkTreeKey}");
        }
    }

    /// <summary>
    /// Add a linked worktree at <paramref name="worktreePath"/> on a new branch <paramref name="branch"/> with
    /// <c>git worktree add</c>, then check that its top level holds a <c>.git</c> file, the layout under test.
    /// Call it once <c>HEAD</c> has a commit.
    /// </summary>
    /// <param name="worktreePath">A <c>/</c>-separated path inside this repository that does not exist yet.</param>
    /// <param name="branch">The new branch's name.</param>
    /// <exception cref="InvalidOperationException">Thrown when git fails or leaves no <c>.git</c> file.</exception>
    /// <exception cref="TimeoutException">Thrown when git does not exit within the timeout.</exception>
    public void AddLinkedWorktree(string worktreePath, string branch)
    {
        Run("worktree", "add", "-b", branch, worktreePath);
        if (!File.Exists(Path.Combine(RepoPath, worktreePath.Replace('/', Path.DirectorySeparatorChar), GitFileName)))
        {
            throw new InvalidOperationException($"git worktree add left no {GitFileName} file in the new worktree");
        }
    }

    public void Dispose()
    {
        if (!Directory.Exists(RepoPath))
        {
            return;
        }

        try
        {
            foreach (var file in Directory.EnumerateFiles(RepoPath, "*", SearchOption.AllDirectories))
            {
                File.SetAttributes(file, FileAttributes.Normal);
            }

            Directory.Delete(RepoPath, recursive: true);
        }
        catch (IOException)
        {
            // Best-effort cleanup; anything left over lives under the OS temp directory.
        }
        catch (UnauthorizedAccessException)
        {
            // Same best-effort contract.
        }
    }

    /// <summary>Kill a git process that outlived the timeout, so the timeout stays the reported failure even if the kill itself fails.</summary>
    private static void TryKill(Process process)
    {
        try
        {
            process.Kill(entireProcessTree: true);
        }
        catch (Win32Exception)
        {
            // The process was already terminating.
        }
        catch (AggregateException)
        {
            // A descendant could not be terminated; the caller still reports the timeout.
        }
    }

    /// <summary>Write <see cref="TrapScript"/> at the <c>/</c>-separated <paramref name="relativePath"/>, executable by its owner off Windows.</summary>
    /// <param name="relativePath">The script's <c>/</c>-separated path under the repository root.</param>
    /// <returns>The script's absolute path.</returns>
    private string WriteTrapScript(string relativePath)
    {
        var script = PrepareTarget(relativePath);
        File.WriteAllText(script, TrapScript);
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(script, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }

        return script;
    }

    /// <summary>Return when <paramref name="marker"/> exists; otherwise skip the calling test on Windows and fail it anywhere else.</summary>
    /// <param name="marker">The marker file the trap script creates when git runs it.</param>
    /// <param name="reason">The skip or failure message.</param>
    private static void RequireTrapFired(string marker, string reason)
    {
        if (File.Exists(marker))
        {
            return;
        }

        if (OperatingSystem.IsWindows())
        {
            Assert.Skip(reason);
        }

        Assert.Fail(reason);
    }

    /// <summary>
    /// In the repository at <paramref name="repoPrefix"/>, point <c>filter.&lt;driver&gt;.clean</c> at a trap script, select
    /// the driver for every path through that repository's <c>.git/info/attributes</c>, and move
    /// <paramref name="trackedPath"/>'s modification time by <see cref="StatDirtyShift"/>.
    /// </summary>
    /// <param name="repoPrefix">Empty for this repository, or a submodule's <c>/</c>-separated path with a trailing <c>/</c>.</param>
    /// <param name="driver">The driver name to configure and select.</param>
    /// <param name="trackedPath">A file committed in that repository, relative to its root.</param>
    /// <returns>The path of the marker file the script creates when git runs it.</returns>
    private string ArmFilterDriverTrap(string repoPrefix, string driver, string trackedPath)
    {
        var script = WriteTrapScript(repoPrefix + FilterTrapFile);
        var repoDirectory = Path.Combine(RepoPath, repoPrefix.Replace('/', Path.DirectorySeparatorChar));
        Run("-C", repoDirectory, "config", $"filter.{driver}.clean", $"'{script.Replace('\\', '/')}'");
        WriteFile($"{repoPrefix}.git/info/attributes", $"* filter={driver}\n");
        var file = PrepareTarget(repoPrefix + trackedPath);
        File.SetLastWriteTimeUtc(file, File.GetLastWriteTimeUtc(file) + StatDirtyShift);
        return script + TrapMarkerSuffix;
    }

    private string PrepareTarget(string relativePath)
    {
        var full = Path.Combine(RepoPath, relativePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(full));
        return full;
    }

    /// <summary>
    /// Resolve every symlinked component of an existing path to its real target, root-downward, so the
    /// result matches the path git reports for the directory (<c>git rev-parse --show-toplevel</c>). On
    /// macOS the temp directory sits under <c>/var</c>, a symlink to <c>/private/var</c>; on Windows and
    /// Linux temp directories this is usually a no-op.
    /// </summary>
    internal static string RealPath(string path)
    {
        var full = Path.GetFullPath(path);
        var root = Path.GetPathRoot(full) ?? string.Empty;
        var current = root;
        foreach (var segment in full[root.Length..]
                     .Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries))
        {
            current = Path.Combine(current, segment);
            if (!Directory.Exists(current))
            {
                continue;
            }

            var resolved = new DirectoryInfo(current).ResolveLinkTarget(returnFinalTarget: true);
            if (resolved is not null)
            {
                current = resolved.FullName;
            }
        }

        return current;
    }
}