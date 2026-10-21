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
