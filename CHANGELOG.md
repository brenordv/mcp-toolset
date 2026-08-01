# Toolset Changelog

## v6
- Added new MCP: `text-search` (read-only, root-confined file search and inspection: `describe_scope`, `find_files`, `inspect_files`, `search_text`, `read_lines`). Supports multiple named roots, opt-in package roots (cached dependency sources, targeted with `@packages`/`@all`), and a file-extension filter.
- Added new MCP: `text-edit` (root-confined text mutation with hash-gated undo: `describe_scope`, `normalize_files`, `replace_text`, `list_recent_batches`, `undo_batch`/`undo_last_batch`). Points at one repository, keeps its write tools on prompt, refuses secret files via the same non-overridable denylist, and journals every change to an append-only store (SQLite + BLAKE3 pre-images) sited outside the root so a batch can be rolled back even after a mid-batch crash.
- Added shared `RaccoonNinja.McpToolset.Files` library backing them: encoding detection, root confinement with full symlink resolution, a non-overridable secret denylist, glob/regex selection, ancestor-aware ignore evaluation, an atomic writer (with source mode/ACL preservation), and a content-addressed blob store.
- Renamed the `git-ops` server assembly and executable from `RaccoonNinja.McpToolset.Server.GitOps` to `git-ops`, matching the short binary names of the other servers.
- Release binaries are now true single-file executables: the native SQLite (`e_sqlite3`) and BLAKE3 libraries are embedded and self-extracted at first run, so each per-platform zip holds one compressed executable. Publish settings are centralized in `eng/ServerPublish.props`, shared by the release workflow and new `eng/publish.ps1` / `eng/publish.sh` scripts for local single-file builds.

## v5
- Updated MCP: `git-ops` to v1.0.2 (fixes `git_grep` exit 128 from a leftover `--end-of-options` token on some git builds; rejects an empty grep pattern; raises disallowed git exits to Warning).

## v4
- Updated MCP: `git-ops` to v1.0.1 (git range expressions `A..B` / `A...B` in `git_log` and `git_diff`).

## v3
- Updated MCP: `file-vault` to v2.1.0.

## v2
- Added new MCP: `file-vault`.

## v1
- Initial release of `git-ops`.
