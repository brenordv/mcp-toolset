# FileVault MCP Server

A personal, cross-conversation file vault for the [Model Context Protocol](https://modelcontextprotocol.io). Save
content by name with a one-line summary; retrieve, list, version, and edit it from any chat. Everything lives in a
local store on your machine: a SQLite database for metadata plus one immutable plain-text snapshot per version.

This server is a **drop-in C# port of the Rust `vault-mcp` server (v1.1.0)**: same tool names, same argument and
result shapes, same error codes, same environment variables, and the **same on-disk store format**. Point your
existing `vault` MCP registration at this binary and every note, version, and tag keeps working. The eight deliberate
behavioral deviations are documented [below](#deviations-from-the-rust-server).

---

## Why use it?

| Without this server                              | With this server                                             |
|--------------------------------------------------|--------------------------------------------------------------|
| Notes live inside one conversation and evaporate | Notes persist across conversations and projects              |
| "Remember this" relies on the model's memory     | Content is stored verbatim, versioned, and hash-verified     |
| Concurrent edits silently overwrite each other   | Optimistic concurrency rejects stale writes with a diff hint |
| Everything-in-one-note sprawl                    | Parent/child links split large notes into related ones       |
| Rewriting a whole document to change one field   | Structure-aware section (markdown) and key (JSON/YAML) edits |

## Tool catalog

| Tool                 | Purpose                                                                    | Key parameters                                                                      |
|----------------------|----------------------------------------------------------------------------|-------------------------------------------------------------------------------------|
| `vault_save`         | Save content as a new immutable version (first save omits `base_version`)  | `name`, `content`, `summary`, `base_version`, `project`, `tags`, `format`, `parent` |
| `vault_get`          | Fetch content + metadata (returns `current_version`, `parent`, `children`) | `name`, `project`, `version`                                                        |
| `vault_list`         | List active files; filter by project, tags, or full-text query             | `project`, `tags`, `query`                                                          |
| `vault_append`       | Append to the current content as a new version                             | `name`, `content`, `base_version`, `project`                                        |
| `vault_edit_section` | Replace one markdown section by heading, rest byte-for-byte intact         | `name`, `heading`, `content`, `base_version`, `project`                             |
| `vault_edit_key`     | Set a value at a dotted key path in JSON/YAML                              | `name`, `key_path`, `value`, `base_version`, `project`                              |
| `vault_set_meta`     | Change summary/tags/parent without a new version                           | `name`, `summary`, `tags`, `parent`, `clear_parent`, `base_version`, `project`      |
| `vault_history`      | Full version history, newest first                                         | `name`, `project`                                                                   |
| `vault_archive`      | Soft delete (hidden from listings, restorable)                             | `name`, `project`                                                                   |
| `vault_restore`      | Restore an archived file                                                   | `name`, `project`                                                                   |
| `vault_purge`        | Permanently delete all versions (requires `confirm: true`)                 | `name`, `project`, `confirm`                                                        |

All tools return **structured content** (typed JSON). Two prompts ship alongside: `continue-draft` (load a draft and
keep writing) and `summarize-vault` (one-line inventory). Every active note is also exposed as an MCP resource under
`vault://{project}/{name}`.

Write results (`vault_save`, `vault_append`, `vault_edit_section`, `vault_edit_key`) additionally carry an advisory
`hint` field when the committed content grows past `VAULT_MCP_SPLIT_HINT_CHARS`, nudging the caller to keep the note
as a summary + index and move detail into child notes (linked via `parent`). It is advisory only and never blocks a
write (D7).

## Project resolution

`project` namespaces files; the same name may exist under different projects. When you omit it, the server resolves
one deterministically:

1. explicit `project` argument (trimmed; empty falls through),
2. `VAULT_MCP_PROJECT` environment variable,
3. derived from the server's working directory: `<repo>/<folder>` inside a monorepo or git repo, else the bare
   folder name,
4. otherwise the call fails with `ambiguous_project` rather than guessing.

**Exception:** `vault_list` and the `summarize-vault` prompt treat an omitted `project` as "across ALL projects" and
run no inference. That is load-bearing for cross-project namespaces (shared archives like `lessons`).

## Storage model

The store defaults to `~/.vault-mcp` (override with `VAULT_MCP_HOME`):

```
~/.vault-mcp/
├─ vault.db                          # SQLite (WAL): files, versions, tags, FTS5 index
└─ files/<project>/<name>/
   └─ v0007-3f9c2ab81c44.md          # one immutable snapshot per version: v{NNNN}-{blake3-12}.{ext}
```

- **Versioning** is 1-based and monotonic; every write is a new snapshot, never an edit in place.
- **Optimistic concurrency**: pass the `base_version` you last read; a stale write is rejected as `conflict` with
  the current version and, where possible, a base-to-current line diff (capped at 200 lines).
- **Crash safety**: snapshots are written to a temp file, flushed, and atomically renamed before the database row
  commits. A crash in between leaves at most an orphan snapshot, never a dangling pointer.
- **Integrity**: each version stores a blake3 content hash, also embedded in the snapshot filename.
- **Purge retention**: by default, purged snapshots are renamed with a `DELETED_` prefix and kept for manual
  recovery. Set `VAULT_MCP_PURGE_DELETE_FILES=true` to delete them outright.

## Configuration

| Variable                       | Default        | Notes                                                                                                                                                                                                                |
|--------------------------------|----------------|----------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------|
| `VAULT_MCP_HOME`               | `~/.vault-mcp` | Store root directory.                                                                                                                                                                                                |
| `VAULT_MCP_PROJECT`            | unset          | Explicit project namespace; whitespace-only counts as unset.                                                                                                                                                         |
| `VAULT_MCP_MAX_BYTES`          | `10485760`     | Per-call content limit. An invalid value is a **fatal startup error**.                                                                                                                                               |
| `VAULT_MCP_BUSY_TIMEOUT_MS`    | `5000`         | SQLite busy timeout. Invalid value is fatal.                                                                                                                                                                         |
| `VAULT_MCP_SPLIT_HINT_CHARS`   | `14000`        | Committed-content length (UTF-16 code units, i.e. `string.Length`, not bytes; distinct from `VAULT_MCP_MAX_BYTES`) above which write results carry the advisory split `hint`. `0` disables. Invalid value is fatal. |
| `VAULT_MCP_PURGE_DELETE_FILES` | `false`        | Accepts `1/0/true/false/yes/no/on/off` (case-insensitive); anything else is fatal.                                                                                                                                   |
| `VAULT_MCP_LOG`                | `info`         | Log level; falls back to `RUST_LOG` for drop-in parity.                                                                                                                                                              |
| `VAULT_MCP_LOG_FILE`           | unset          | Opt-in log file path (additive in this port); default sink is stderr.                                                                                                                                                |

## Errors

Domain failures come back as a **failed tool call** whose text is a JSON body with a stable `code` and typed payload
fields:

```jsonc
// The MCP SDK prefixes the text with "An error occurred invoking '<tool>': "; the JSON object follows.
{
  "error": {
    "code": "conflict",
    "message": "version conflict: the file has changed (current version is 7); ...",
    "current_version": 7,
    "base_version": 5,
    "diff": "-old line\n+new line"
  }
}
```

| Code                                                                                                                                                                          | Extra payload fields                                               |
|-------------------------------------------------------------------------------------------------------------------------------------------------------------------------------|--------------------------------------------------------------------|
| `not_found`                                                                                                                                                                   | `project`, `name`                                                  |
| `conflict`                                                                                                                                                                    | `current_version`; plus `base_version` + `diff` when a hint exists |
| `archived`                                                                                                                                                                    | `project`, `name`                                                  |
| `ambiguous_project`                                                                                                                                                           | `tried` (array of rejected candidates)                             |
| `parent_not_found`                                                                                                                                                            | `project`, `parent`                                                |
| `invalid_name`, `invalid_format`, `too_large`, `heading_not_found`, `ambiguous_heading`, `key_path_not_found`, `confirmation_required`, `invalid_parent`, `nothing_to_update` | code + message only                                                |

Archived files still answer `vault_get`, `vault_history`, and `vault_purge`; every mutating tool rejects them with
`archived` until you `vault_restore`.

> **Note:** unlike the GitOps server, this server intentionally does **not** use the toolset's `ResultEnvelope`
> convention. Its wire contract is pinned to the Rust `vault-mcp` server for drop-in compatibility.

## Security model

- **Path traversal, twice defended**: names are validated to a strict charset (single segment, `[A-Za-z0-9._-]`,
  no `.`/`..`), and the file store independently re-checks every component and asserts the resolved path stays
  under the store root.
- **Windows-hostile names rejected**: trailing dots and reserved device names (`CON`, `NUL`, `COM1`…) are rejected
  so a snapshot can never silently land in the bit bucket.
- **SQL is parameterized everywhere** (Dapper); the only dynamic SQL is integer IN-list expansion, also bound as
  parameters.
- **FTS queries are neutralized**: every whitespace token is quoted, so user punctuation cannot become FTS5 syntax.
- **Purge is gated** behind `confirm: true`, checked before any deletion, and defaults to retaining bytes on disk.
- **The git probe is hardened**: project inference may run `git rev-parse --show-toplevel` once per process (the
  derivation is computed on first use and cached), with the git binary resolved from PATH explicitly (never the
  working directory), an allowlisted environment, no shell, asynchronously drained pipes, and a kill-on-timeout
  guard.
- **Logs never contain content**: an allowlist formatter admits only validated identifiers and system data
  (project, name, versions, sizes, durations, error codes). Summaries, tags, content, diffs, headings, and queries
  are never logged; search terms appear only as hashes.

> **Plaintext store caveat:** the vault stores content as plain text on disk. It is not a secret store; do not save
> credentials or other secrets in it.

## Deviations from the Rust server

Everything not listed here matches the Rust implementation, which remains the normative spec.
(If you're just downloading the this MCP server for the first time, you can ignore this section.)

| #  | Deviation                                                                                                                                                                                                                          | Effect                                                                                                                                                                                             |
|----|------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------|----------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------|
| D1 | Error transport: domain failures are failed tool calls carrying the JSON error body above, instead of JSON-RPC protocol errors with `data`. The SDK adds an `An error occurred invoking '<tool>': ` prefix before the JSON object. | Same codes and fields, different framing.                                                                                                                                                          |
| D2 | Conflict diff hints are skipped when `baseLines × currentLines` exceeds 4,000,000 (the Rust LCS table is unbounded).                                                                                                               | Oversized conflicts degrade to hint-less, a shape the contract already allows.                                                                                                                     |
| D3 | Windows-hostile names (trailing dots, reserved device names) are rejected as `invalid_name`.                                                                                                                                       | Prevents silent snapshot loss on Windows.                                                                                                                                                          |
| D4 | Input caps: name ≤ 128 chars; project ≤ 8 segments / 512 chars; summary ≤ 16 KiB; ≤ 64 tags of ≤ 128 chars.                                                                                                                        | Oversizes fail fast (`invalid_name` / `too_large`).                                                                                                                                                |
| D5 | Purge skips deleting any snapshot path still referenced by another live version row (case-insensitive).                                                                                                                            | A filesystem-collision purge can't orphan a sibling's only snapshot.                                                                                                                               |
| D6 | The startup git probe runs with an allowlisted environment, explicit PATH resolution, and a hard timeout.                                                                                                                          | Hardening only; same fallback behavior.                                                                                                                                                            |
| D7 | Write results gain an advisory `hint` field when committed content exceeds `VAULT_MCP_SPLIT_HINT_CHARS` (default 14,000 chars; `0` disables). The field is omitted otherwise.                                                      | Additive response-shape change; nudges agents to split large notes into a summary + child notes.                                                                                                   |
| D8 | A `vault_save` update (`base_version` present) that omits `format` keeps the note's stored format; only the first-ever save defaults to `text`. (The Rust server resets omitted formats to `text` on every save.)                  | A markdown/json/yaml note can no longer be silently downgraded (losing section/key editability) by a later save that leaves the optional argument out. Passing `format` explicitly still converts. |

## Switching over from the Rust server

1. Stop clients using the Rust `vault-mcp`.
2. Point your MCP registration's `command` at this binary (same env vars, same store, no migration step; the port
   introduces no schema changes).
3. Restart your client. All notes, versions, tags, hierarchy links, and archived files are served as before.

Replace the Rust server rather than running both against one store long-term; transiently they coexist safely (WAL +
write locks), but that is a switchover convenience, not a supported topology.

## Requirements

- **No external runtime dependencies** for the released binaries. Each release ships a self-contained, single-file
  executable per platform ([Releases and verification](../../README.md#releases-and-verification)); SQLite is bundled
  in, so there's nothing extra to install to run it.
- **`git`**: *optional*. When present on `PATH`, it's invoked once per process to derive the default `project` from
  the enclosing repository (see [Project resolution](#project-resolution)). Without it, project inference falls back
  to the working-directory folder name; nothing fails.
- **Filesystem access** to the store root (`~/.vault-mcp` by default, override with `VAULT_MCP_HOME`); the server
  creates it on first run.
- **Building from source**: the [.NET 10 SDK](https://dotnet.microsoft.com/download).

## Adding it to an MCP client

The server is a .NET console app speaking MCP over stdio. Add an entry to your MCP configuration (e.g. `.mcp.json`,
or via `claude mcp add`). On Windows:

```jsonc
{
  "mcpServers": {
    "vault": {
      "command": "c:\\path\\to\\file-vault.exe",
      "args": [],
      "env": {
        "VAULT_MCP_LOG": "info",
        "VAULT_MCP_LOG_FILE": "Z:\\logs\\mcp-filevault.log"
      }
    }
  }
}
```

The server identifies itself as `vault-mcp` in the MCP handshake, matching the Rust server it replaces.
