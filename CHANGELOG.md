# Toolset Changelog

## v11
- Updated `ModelContextProtocol` NuGet package to version 2.1.0.

## v10
- Extended that ignore boundary in `text-search` and `text-edit` to honor common AI-agent ignore files (`.claudeignore`, `.cursorignore`, `.aiexclude`, `.aiignore`, `.codeiumignore`, `.continueignore`, `.aiderignore`, `.geminiignore`) alongside `.gitignore`/`.mcpignore`, ranked between them so `.mcpignore` still wins. A path listed in one is pruned from listings and refused by the read and write gates exactly as a `.gitignore` path is, so a secret that only an agent's own ignore file names is no longer surfaced by default, on a direct `read_lines`/`inspect_files` as well as during discovery. Index-only files (`.cursorindexingignore`) and tool-scope ignores (`.npmignore`, `.dockerignore`, ...) are deliberately not honored. The honored kinds are disclosed in `describe_scope`'s `ignore_files`.

## v9
- Made `.gitignore`/`.mcpignore` an un-overridable boundary in `text-search` and `text-edit`: an ignored file is never returned or edited, enforced at the read gate, the `paths[]` gate, listing, and the write gate. `include_ignored` now re-includes only the built-in default tier (`node_modules`, `bin`, `obj`, ...) and can never re-include a `.gitignore`/`.mcpignore` path, and a scoped `cwd` call now also honors ignore rules in directories above the `cwd`.
- Added content-based secret detection to `text-search`: a file whose content matches a known secret shape (private keys; AWS/Azure/GCP/Google keys; Slack/GitHub/Stripe/SendGrid tokens; URL-userinfo credentials) is withheld from `read_lines`, `search_text`, and `inspect_files` regardless of its name, while it still appears in `find_files` listings. On by default; `MCP_TEXTSEARCH_SECRET_SCAN=off` disables it and `=aggressive` adds higher-false-positive detectors (JWTs, generic password assignments). Disclosed in `describe_scope`.
- Closed a secret-exposure gap in `text-search` and `text-edit`: `local.settings.json` (the Azure Functions local config, which by convention holds connection strings and access keys) is now on the non-overridable secret denylist. `include_ignored` can no longer surface the file or leak its content through a search context window.
- Fixed the server log files, which were written as UTF-8 with a byte order mark. The rolling file sink now writes BOM-less UTF-8. This affects newly created and rolled log files; existing logs keep their BOM.
- Added per-platform publish convenience wrappers under `eng/`: `publish-windows`, `publish-linux`, `publish-macos` (osx-arm64), and `publish-macos-x64` (osx-x64), each in both `.ps1` and `.sh`. They delegate to `eng/publish.ps1` / `eng/publish.sh` with a fixed RID.

## v8
- Improved `text-edit` MCP server to handle multiple projects more efficiently.

## v7
- Improved `text-search` MCP server to handle multiple projects more efficiently.

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
