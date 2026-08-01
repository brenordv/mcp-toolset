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
