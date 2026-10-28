## v2.15.0
- Fixed: a log event carrying an exception (a crash, or a record the host or the MCP SDK logged with an exception
  attached) wrote the last 512 bytes of the exception's message and stack trace to `stderr_tail`, which can name a
  file by its absolute path. It now writes the exception's type chain only, outer to inner, at most eight entries.
  `stderr_tail` on `subprocess_exit` (git's own stderr) is unchanged.
- Fixed: the generic host logged `Content root path: <working directory>` at Information on every startup; the
  host's lifetime category is now held at Warning. Log change: the content of `stderr_tail` on exception events, and
  the host's four status lines (`Application started`, `Hosting environment`, `Content root path`,
  `Application is shutting down`) are gone.
- Fixed: at a `TRACE` log level the MCP SDK wrote every JSON-RPC frame verbatim (the `cwd`, refs, paths, patterns,
  file content) to the log; the SDK's categories are now held at Information or the configured level, whichever is
  higher. Log change: at `DEBUG` and `TRACE`, the SDK's own lines below Information are gone.
- Fixed: the `suspicious_filter_invocation` Warning put the failed filter's command, which can hold an absolute path,
  in `driver_name`; it now carries no text from the command. It also missed a command containing `'`, such as a
  quoted path (`'<path>' %f`), and now fires whatever characters the command holds.
- Log change: `suspicious_filter_invocation` lines no longer have `driver_name`, and no line carries that field; the
  event keeps `event`, `tool`, `call_id`, and `message`. It can now appear for a failed filter whose command contains
  `'`, which wrote no line before.

## v2.14.0
- Fixed: `submodule.<name>.ignore=all`, which a committed `.gitmodules` can carry, `diff.ignoreSubmodules=all` in the
  user's or the repository's config, or a `.gitmodules` left unmerged in the index made a staged or commit-to-commit
  `git_diff`, `git_show`, and `git_stash_show` leave out a submodule whose recorded commit changed, and made `git_log`
  skip the commits that moved it when called with `pickaxe`, with `follow`, or with one `paths` entry in a repository
  whose config sets `log.follow=true`. Every pass of the three diff tools and every `git_log` call now passes
  `--ignore-submodules=dirty`, which overrides those settings, as `git_status` and a `git_diff` against the working
  tree already did. A diff against the working tree still never runs git inside a checked-out submodule.
- Fixed: with `log.follow=true` in the user's or the repository's config, `git_log` with one `paths` entry followed
  renames although `follow` was false, so it listed commits from before the file was renamed. Every git call now
  passes `-c log.follow=false`, and only `follow` turns rename following on.
- Behavior change: the submodule override applies to a setting you made on purpose too. A submodule that
  `submodule.<name>.ignore=all` (in your global or the repository's config, or in `.gitmodules`) or
  `diff.ignoreSubmodules=all` kept out of a staged or commit-to-commit `git_diff`, `git_show`, or `git_stash_show` now
  appears whenever its recorded commit changed, and `git_log` with `pickaxe` or `follow` can return the commits that
  moved it, so a `maxCount` page can hold commits it skipped before. There is no opt-out; run git in your own shell to
  keep the setting's effect. Without an `all` value in either setting, or a `.gitmodules` left unmerged in the index,
  the submodule override changes no result.
- Behavior change: if you set `log.follow=true` so that `git log` follows renames, `git_log` with one `paths` entry no
  longer does, and its history stops at the commit that renamed the file; pass `follow: true`.
- Log change: every `argv_built` debug line shows `-c log.follow=false`, with `argv_len` 2 higher; the bootstrap calls
  carry the same pin but log no argv line. The line also shows `--ignore-submodules=dirty` on every `git_log` call, on
  the `git_show` patch pass, on both `git_stash_show` passes, and on each `git_diff` pass that does not read the working
  tree, so those lines have `argv_len` 3 higher than in v2.13.0 in all; the passes that read the working tree showed the
  flag already. A `git_log` line without `--follow` now means git did not follow renames, whatever `log.follow` says.

## v2.13.0
- Fixed: every git process started from the bare name `git`, which .NET looks up in the server's own directory and
  current directory before `PATH` (on Windows, `CreateProcess` also tries the system directories first). An MCP client
  that started the server inside a repository could make it run a `git` or `git.exe` committed at that repository's
  root: on the first tool call in every earlier release, and at startup in v2.12.0. The server now finds git once at
  startup in the absolute directories of its `PATH` (`git.exe` on Windows; elsewhere a file with an execute bit) and
  starts that path every time.
- Behavior change: empty and relative `PATH` entries are ignored, and a git found only in the server's own directory,
  or on Windows only in a system directory not on `PATH`, is no longer found; tools then fail with `GitNotInstalled`.
  Restart the server after installing or moving git.
- Log change: a `GitNotInstalled` `tool_error` carries `git executable not found on PATH when the server started` when
  no git was found and `git executable could not be started` when the file found fails to start, and the message no
  longer ends with the executable in parentheses (the envelope's `message` carries the same text). No event or field
  changes; the `argv_built` debug line still starts with `git`, and no line carries the path found.

## v2.12.0
- Log change: at startup the server runs `git version` once and logs, right after `server_start`, a `git_version` Info
  line with a new `git_version` field, or a `git_version_unavailable` Warning with `error_code` (`GitNotInstalled`,
  `GitTimeout`, or `GitCommandError`, with `git_exit_code` whenever git exited) when git is missing, takes more than
  5 seconds, exits non-zero, or prints an unexpected line. The server starts in every one of those cases. The process
  is not counted in the session metrics.

## v2.11.0
- Fixed: `git_diff`, `git_show`, and `git_stash_show` reported a changed file under the whole `diff --git` header
  line instead of its path when the user's or the repository's git config set `diff.noprefix`,
  `diff.mnemonicPrefix` (worktree, staged, and `HEAD`-to-worktree diffs), `diff.srcPrefix`, or `diff.dstPrefix`,
  because the parser reads the path after ` b/`; the numstat overlay then missed the file. With `diff.submodule`
  set to `log` or `diff`, a changed submodule was missing from `files`. `git_diff` and `git_show` now pass
  `--src-prefix=a/ --dst-prefix=b/ --submodule=short` next to `--no-color` on every diff pass. `git_stash_show` passes
  `--submodule=short` and pins the prefixes with
  `-c diff.noprefix=false -c diff.mnemonicPrefix=false -c diff.srcPrefix=a/ -c diff.dstPrefix=b/`, because
  `git stash show` in git 2.52.0 through 2.55.x garbles a prefix given on its command line.
- Fixed: with `diff.submodule=diff` in a repository's config, a `git_diff`, `git_show`, or `git_stash_show` whose diff
  included a submodule commit change made git start `git diff` inside the submodule, without `--no-ext-diff` or
  `--no-textconv`, so an external diff command or textconv driver the submodule's own config names could run.
  `--submodule=short` overrides the key, and git starts no process inside the submodule.
- Log change: the `argv_built` debug line shows the three new flags on each `git diff` pass of `git_diff` and on the
  `git_show` patch pass, with `argv_len` 3 higher, and on each `git stash show` pass of `git_stash_show` the four
  `-c diff.*` pairs in front of the fixed `-c` pairs plus `--submodule=short`, with `argv_len` 9 higher.

## v2.10.0
- Fixed: a filter driver that a repository's config names (`filter.<driver>.clean` or `filter.<driver>.process`),
  selected through a `filter` attribute in `.gitattributes` or `.git/info/attributes`, ran when `git_status`, a
  worktree `git_diff`, or `git_blame` without `ref` read a worktree file; for `git_status`, every file whose timestamp
  no longer matched the index, which is every file in a copied repository. Before each `git status`, `git diff`, and
  `git blame`, the server now lists the drivers the config names and pins each one's `clean`, `smudge`, and `process`
  to empty and `required` to `false` with `-c`. A driver name that contains `=` or is not valid UTF-8, or a listing
  longer than the 8 MB output cap, fails the call with `GitCommandError`.
- Fixed: `git_status` and a worktree `git_diff` made git run `git status` inside each checked-out submodule, with the
  submodule's own config and filter drivers. Both now pass `--ignore-submodules=dirty`.
- Behavior change: drivers from your global config are pinned too, so a file under git-lfs, git-crypt, or another
  filter is compared as its raw worktree bytes: `git_status` can list it as modified when only its timestamp changed,
  a worktree `git_diff` shows the stored pointer or ciphertext against the raw content, and `git_blame` without `ref`
  attributes every line to the uncommitted working-tree version. In a repository you trust, running `git status` once
  in your own shell, with the filter installed, refreshes the index's timestamps and clears those files from
  `git_status` and a worktree `git_diff`. A submodule with only uncommitted changes inside it is no longer listed by
  `git_status` or a worktree `git_diff`; one whose commit moved still is, even where `submodule.<name>.ignore` or
  `diff.ignoreSubmodules` would hide it.
- Log change: each `git status`, `git diff`, and `git blame` process is preceded by a `git config` process with its own
  `argv_built` and `subprocess_exit` debug lines under the same `call_id`. That `subprocess_exit` shows
  `git_exit_code` 1 when the config names no filter driver, which is not an error. The pins show in the next
  `argv_built` line as `-c <filter_driver>`, four per driver, with `argv_len` 8 higher per driver, and the `argv_built`
  line for `git_status` and for each `git_diff` pass that reads the working tree shows `--ignore-submodules=dirty`. A
  listing that hits the output cap, or a driver name the server cannot pin, logs only the call's `tool_error` at Error,
  with the fixed message and no `git_exit_code`. `suspicious_filter_invocation` is unchanged, and with the pins in
  place no call should produce it.
- Metrics change: the listing is a git subprocess, so `subprocess_duration_ms_p50` and `subprocess_duration_ms_p95` get
  one more sample per `git_status` and `git_blame` call and two more per `git_diff` call, and are not comparable with an
  earlier version's `server_stop` summary. A listing that hits the 8 MB cap counts in `truncations_total`. The two
  refusals count as `error` in `tool_calls_total` and not in `git_command_errors_total`, which counts only git exits a
  tool does not allow.

## v2.9.0
- Fixed: in a repository whose config names a promisor remote (`remote.<name>.promisor=true`,
  `remote.<name>.partialCloneFilter`, or `extensions.partialClone`), a tool call that read an object git did not have
  made git fetch it from that remote, and the fetch ran the program the repository's config names for the transport,
  for example `remote.<name>.uploadpack` for a remote at a local path or `core.sshCommand` for ssh. Every git process
  the server starts now gets `GIT_NO_LAZY_FETCH=1`, next to `GIT_TERMINAL_PROMPT=0`. git 2.45.1 and later and the May
  2024 security releases for 2.39 to 2.44 have the full check (the README's Requirements lists them); 2.45.0 honors
  the variable only in part, and an older git ignores it.
- Behavior change: in an ordinary partial clone, a call that needs an object the clone has not fetched (an old blob for
  `git_show`, `git_diff`, `git_blame`, `git_file_at_ref`, or `git_grep` with `ref`) fails with `GitCommandError`
  instead of fetching it.

## v2.8.0
- Fixed: a repository's own config could make `git_log`, `git_show`, `git_reflog`, and `git_stash_list` run a program
  it names. With `log.showSignature=true`, git verifies each signed commit it shows by running the program
  `gpg.program`, `gpg.ssh.program`, or `gpg.x509.program` names, and a forged `gpgsig` header is enough. Every git
  call now passes `-c log.showSignature=false`, next to the `core.fsmonitor` and `core.hooksPath` neutralizers. With
  `log.showSignature=true` in the user's own config, `git_log` and the `git_show` metadata pass also put gpg's output
  in front of each signed commit's `hash`, and `git_log`, both `git_show` passes, and `git_reflog` ran gpg once per
  signed commit shown; the same change fixes both.
- Log change: every `argv_built` debug line shows `-c log.showSignature=false`, and its `argv_len` is 2 higher. The
  bootstrap `rev-parse` calls carry the same pin but log no argv line.

## v2.7.0
- Fixed: `git_diff`, `git_show`, and `git_stash_show` returned an empty file list, with no error, when the user's or
  the repository's git config set `color.ui` or `color.diff` to `always`, because git colored the patch and no
  `diff --git` header parsed. The three now always pass `--no-color`, as `git_grep` has since 2.6.0. `statOnly` was not
  affected.
- Log change: a new Warning event, `patch_parse_empty`, when the `--numstat` pass of `git_diff` or `git_stash_show`
  lists files but no `diff --git` header parsed from the patch. The `argv_built` debug line for the three tools shows
  `--no-color` on every pass, the `--numstat` and `--name-status` passes included.

## v2.6.0
- `git_grep` gained `countOnly`. `countOnly: true` adds git's `-c` in place of `-n` and returns one
  `{path, match_count}` entry per file with at least one matching line, instead of one entry per line. `match_count`
  counts matching lines, as `git grep -c` does, so a line with two occurrences counts once. Files with no match are
  left out. `maxCount` together with `countOnly` is `RejectedArgument` (`param: max_count`), rejected before git runs:
  git applies `-m` before counting, so the count would be capped at `maxCount`. `filters_applied` reports
  `count_only`. Without `countOnly` the result is unchanged.
- Fixed: `git_grep` returned an empty success when the user's or the repository's git config set `color.ui` or
  `color.grep` to `always`, because git colored the line numbers and no record parsed; count mode would have
  returned paths wrapped in escape codes. `git_grep` now always passes `--no-color`.
- Log change: a new Warning event, `count_parse_empty`, when a count-mode grep exits 0 (git found a match) but no
  record parsed. The `argv_built` debug line for `git_grep` shows `--no-color`, and `-c` instead of `-n` in count
  mode.

## v2.5.0
- `git_file_at_ref` gained `lineStart`/`lineEnd`, `byteOffset`, and `maxBytes`, so a large or single-line file can be
  read in pieces instead of overflowing the client's per-result limit. `lineStart` and `lineEnd` are 1-based and
  inclusive, both or neither, as in `git_blame`; an end past the last line is clamped, and a start past it is
  `RejectedArgument` (`param: line_range`) with `total_lines` in the detail. `byteOffset` skips bytes of what the
  range kept, moving forward out of a multi-byte character; at or past the end it is `RejectedArgument`
  (`param: byte_offset`) with `content_bytes` in the detail. `maxBytes` caps the content in UTF-8 bytes, backs off to
  a character boundary (a cap below 4 keeps at least one byte), sets the envelope's `truncated`, and reports where to
  continue in `next_byte_offset`. A malformed range, a negative offset, or a cap below 1 is rejected before git runs.
  A binary blob ignores the three. There is no default cap; without them the result is the whole blob, as before.
- The `git_file_at_ref` payload gained `total_lines` and `next_byte_offset` (both `null` for a binary blob).
  `filters_applied` reports `line_range`, `byte_offset`, and `max_bytes` when set.
- No log change. `truncations_total` still counts only the runner's 8 MB output cap; a `maxBytes` cut sets the
  envelope's `truncated` without counting there.

## v2.4.0
- `git_log` gained `perlRegex` and `allRefs`. `perlRegex: true` adds git's `--perl-regexp`, so `grep` matches
  Perl-compatible regular expressions, the dialect `git_grep` uses for `fixedString: false`: `A|B` alternation,
  `\d`, `\s`, `+`, `?`, and grouping work. It applies to `author` as well, since git treats both as limiting
  patterns. Without it, `grep` stays git's default basic regular expression, where `|` is a literal character,
  so an alternation returns no commits; the `grep` description now says so. A git built without PCRE2 returns
  `PcreUnavailable` for `perlRegex: true`, as `git_grep` does, instead of a generic command error.
- `allRefs: true` adds git's `--all` and walks every ref under `refs/` plus `HEAD`: local branches,
  remote-tracking branches, tags, and the stash. Combined with a non-empty `ref` it is `RejectedArgument`
  (`param: all_refs`). `filters_applied` reports `perl_regex` and `all_refs`.
- Behavior change: `git_log` always passes a dialect, `--basic-regexp` unless `perlRegex` is set. A
  `grep.patternType` in the user's or the repository's git config used to change how `grep` and `author`
  matched (`extended` made `A|B` an alternation, `fixed` made `.` a literal); it no longer does. Without that
  config, and without either new flag, the result is unchanged.
- Log change: the `argv_built` debug line for `git_log` shows `--basic-regexp` or `--perl-regexp`, plus `--all`
  when `allRefs` is set.

## v2.3.0
- `git_grep` and `git_ls_files` gained `globs` and `excludePaths`. `globs` takes wildcard pathspecs with git's
  default matching, where `*` and `?` also match `/`, so `*.cs` matches at any depth and `crates/*/*/Cargo.toml`
  works. `excludePaths` drops paths or patterns through git's `:(exclude)` magic: a directory drops its subtree, and
  with no `paths` and no `globs` the call covers every tracked file except the excluded ones. `filters_applied`
  reports `globs_count` and `exclude_paths_count`.
- Behavior change: a `paths` entry containing `*` or `?`, or starting with `:`, is now `RejectedArgument`
  (`param: paths`) on every tool that takes `paths` (`git_grep`, `git_ls_files`, `git_log`, `git_diff`,
  `git_show`). A wildcard entry used to match nothing and return an empty success, and an `:(exclude)` entry was
  ignored. An empty `paths` entry, which used to mean the repository root, is also `RejectedArgument` now.
- The server no longer runs git in literal-pathspec mode: the `--literal-pathspecs` flag and the
  `GIT_LITERAL_PATHSPECS` environment variable are gone, and every pathspec carries a prefix the server chooses,
  `:(literal)` for `paths`, `:(exclude)` for `excludePaths`, and none for `globs`. `paths` matching is unchanged.
- Behavior change, Linux and macOS only (Windows cannot create such names): a tracked file whose name starts with
  `:` can no longer be addressed through `paths` or blamed, and one whose name contains `*` or `?` can no longer be
  addressed through `paths` on any tool.
- Log change: the `argv_built` debug line no longer shows `--literal-pathspecs`, and it masks wildcard and exclude
  pathspecs as `<glob>` and `<exclude>` (literal paths stay `<path>`).

## v2.2.2
- `git_show` now says a rejected `<rev>:<path>` ref names "a file or tree, not a commit". `HEAD:` (the
  root tree) is rejected the same way as `HEAD:README.md`.
- Internal: the tests share one temp-repository helper that reports a hung git as a timeout, instead of
  three copies that surfaced a hang as an unrelated `InvalidOperationException`.

## v2.2.1
- Internal: removed every `ConfigureAwait(false)` call. The stdio host has no synchronization context, so
  continuations run on the same threads as before. No wire or behavior change.

## v2.2.0
- `git_ls_files` gained a `stage` flag. With `stage: true` it runs `git ls-files -z --stage` and returns
  `{path, mode, object_id, stage}` objects instead of path strings, so an agent can tell whether a committed
  script is `100755` before recommending `./script.sh`. Without the flag the result is unchanged.
- Added a `git_file_at_ref` tool (`cwd`, `path`, `ref` defaulting to `HEAD`): one tracked file's content as of a
  ref, the equivalent of `git show <ref>:<path>`, including a file deleted since. It returns
  `{path, ref_sha, mode, object_id, size, is_binary, content}`; a blob with a NUL byte in its first 8000 bytes
  comes back with `is_binary: true` and `content: null`. A missing path is `PathNotFound`; a directory, the
  repository root, or a blob id passed as `ref` is `RejectedArgument`.
- Behavior change: `git_show` now rejects a `ref` in `<rev>:<path>` form with a `RejectedArgument` naming
  `git_file_at_ref`. It used to verify the blob, find no diff in it, and return an empty success
  (`commit: null`, `files: []`) with no hint of why.

## v2.1.0
- Argument-shape mistakes now come back as the standard failure envelope with `error.code`
  `InvalidArgument`, not a bare, bodiless SDK message (`An error occurred invoking '<tool>'.`). A call
  whose argument names do not fit a tool's schema is rejected before the tool runs: the error names each
  unknown key, suggests the schema name it likely meant when one is close, lists any missing required name
  (a `git_status` with no `cwd`, say), and echoes the full `expected_arguments`. A known argument of the
  wrong JSON type is reported with the same code after the SDK's binder rejects it, carrying the SDK's fixed
  generic text in `detail.sdk_error`. Shape errors set `IsError = true`; ordinary domain errors keep
  `IsError` unset as before.
- Behavior change: an unknown argument name alongside valid ones is now rejected rather than silently
  ignored by the SDK on defaults.
- The shutdown metrics summary gained a `binding_error` outcome bucket per tool, separating a client that
  flails on the argument contract from an ordinary command error.

## v2.0.0
- Renamed the assembly to `git-ops`.

## v1.0.2
- Fixed `git_grep` aborting with a fatal `GitCommandError` (git exit 128) on some git builds, in both
  working-tree and `ref` mode, for patterns that were present. The command builder appended
  `--end-of-options` to every subcommand, but `git grep` parses `--` itself and, on builds predating
  its grep-side fix, leaves that token in its arguments, then reads it as an unknown revision and dies.
  `git grep` no longer receives `--end-of-options`: its pattern already goes through `-e`, refs are
  resolved to object names, and pathspecs stay behind `--`. Other subcommands emit the marker only when
  a ref or positional follows it, dropping the pointless trailing marker on ref-less commands.
- `git_grep` now rejects a null or empty `pattern`, or a `pattern` beginning with `-`, with a
  `RejectedArgument` error that names the `pattern` argument. Previously an empty pattern built a bare
  `-e` and a leading `-` surfaced a confusing error about the internal `-e` flag. A whitespace pattern
  is still accepted as a valid fixed-string search. The parameter description states the constraint.
- A git command that exits with a disallowed code now logs its scrubbed stderr tail at Warning (was
  Debug), so git's own `fatal:` message is visible at the default log level. The client envelope still
  carries only the exit code.

## v1.0.1
- `git_log` `ref` and `git_diff` `fromRef`/`toRef` now accept git range expressions `A..B` and `A...B`.
  `git_log ref: "base..HEAD"` lists the commits a branch adds over its base; `git_diff fromRef: "base...HEAD"`
  gives the three-dot (merge-base) diff. Each side of a range is verified to a SHA independently, so the
  existing ref-hardening still holds. Combining a range with a second ref in `git_diff` is rejected.
- Hardened `RefVerifier`: `rev-parse --verify` output is now checked to be a full object name (SHA-1 or
  SHA-256) before it is used as an argument.

## v1.0.0
- Initial release of the project.
