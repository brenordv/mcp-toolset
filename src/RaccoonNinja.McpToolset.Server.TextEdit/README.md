# text-edit MCP server

A local stdio MCP server that gives an agent root-confined text mutation with hash-gated undo. It is the
mutating half of the toolset, the opposite of `text-search`: **search sees more, edit reaches less.** It
points at one repository, its write tools are meant to stay on prompt rather than blanket-approved, and
every write passes a gate whose two rules no flag can disable. Recoverability comes from an append-only
journal, so any batch can be rolled back.

It never leaves its configured root, and it never writes a secret. Two controls hold that line and no flag
can turn them off:

- **Root confinement.** Every path is resolved through every symbolic link and junction and refused if its
  real target escapes the root. Undo re-checks this on every restore, because a journal path is untrusted
  input.
- **A secret denylist.** `.env`, private keys, `.git/`, `.ssh/`, cloud credentials, and the like are never
  written, however the path is spelled or symlinked.

## Why use it

| Without it                                                                                              | With it                                                                                                            |
|---------------------------------------------------------------------------------------------------------|--------------------------------------------------------------------------------------------------------------------|
| An agent shells out to `sed -i` with a loose path; a bad glob can rewrite `.git/hooks/*` or a key file. | Five typed tools, root-confined, that refuse a secret file structurally and journal every change they make.        |
| A regex from the model rewrites the wrong thing and there is no clean way back.                          | Every batch is journaled with a pre-image; `undo_batch` restores it, skipping any file changed since.              |
| An overeager replacement corrupts a BOM-less UTF-16 file or unifies a mixed-ending file's terminators.  | Encoding is detected and round-tripped byte-faithfully; line endings are preserved unless you ask to change them.  |
| Absolute paths from your machine leak into the model's context.                                         | Paths are root-relative, in and out, in every result and in the journal.                                           |

## Tools

Call `describe_scope` first to learn the root and the caps. The two selector tools share one selector:
give exactly one of `glob` (primary), `regex`, or `paths`, or none to mean "everything under the root",
optionally narrowed by `extensions`. A glob with no `/` matches the basename at any depth, so `*.cs` is
recursive. Ignore rules (`.gitignore`/`.mcpignore`) always apply; there is no `include_ignored` on the write
path.

| Tool                                   | Purpose                                                                          | Annotation                          |
|----------------------------------------|----------------------------------------------------------------------------------|-------------------------------------|
| `describe_scope`                       | Report the root, the denylist, the default encoding, the caps, and the journal retention. | ReadOnly                   |
| `normalize_files`                      | Trim trailing whitespace, rewrite line endings, fix the final newline, strip a BOM. | Destructive=false, Idempotent      |
| `replace_text`                         | Replace a literal or regex pattern (regex back-references) across the selection.  | Destructive=true                    |
| `list_recent_batches`                  | List the recent undoable batches, newest first.                                  | ReadOnly                            |
| `undo_batch` / `undo_last_batch`       | Restore a batch, skipping any file changed since.                                | Destructive=false, Idempotent       |

**`replace_text`** substitutes with .NET's native back-references when `is_regex` is set: `$1`, `${name}`,
and `$$` for a literal `$`. Set `expected_match_count` to abort the whole call before any write unless
exactly that many matches would change, counted only in files that will actually be rewritten. Pass
`dry_run` to get a per-file unified diff and write nothing.

**`normalize_files`** takes `trim_trailing_whitespace`, `line_endings` (`preserve`/`lf`/`crlf`),
`final_newline` (`preserve`/`ensure`/`trim`), and `bom` (`preserve`/`strip`). Under `preserve` a mixed-ending
file keeps each physical terminator.

**`undo_batch(batch_id)`** and **`undo_last_batch()`** restore each file whose current content still equals
what the batch wrote; a file changed since is skipped and named (never clobbered), a since-deleted file is
recreated, and a journal row that no longer confines or is now denylisted is skipped as a security measure.

### Notes that save a round trip

- **Column is 1-based and counts UTF-16 code units** (matching .NET and most editors).
- **A rewrite requires a confidently-detected encoding.** A file whose encoding is detected below the
  confidence threshold is refused unless you pass an explicit `source_encoding`. This is safe by design: a
  low-confidence guess could corrupt the file on write.
- **Denylisted files are omitted from a selector walk, not flagged.** When you name one explicitly it is
  reported as refused.
- **A batch that changes nothing writes no journal row** and returns a `null` `batch_id`.

## The result envelope

Every tool returns the same envelope. On success the payload sits in `results`; a mutation result is a
single element carrying the batch id, the counts, and the per-file entries.

```jsonc
{
  "results": [
    {
      "batch_id": 7,                 // absent on a dry run or a no-op batch
      "dry_run": false,
      "attempted": 3,
      "changed": 2,
      "refused": 1,
      "files": [
        { "path": "src/app.cs", "outcome": "changed" },
        { "path": ".env", "outcome": "refused", "refusal_reason": "denied" }
      ]
    }
  ],
  "count": 1,
  "filters_applied": { "glob": "<provided>", "case_sensitive": false, "dry_run": false },
  "error": null                       // set instead of results on failure
}
```

A per-file `outcome` is `changed`, `refused`, or `unchanged`; a refused file carries a `refusal_reason`
(`denied`, `out_of_root`, `ignored`, `binary`, `too_large`, `low_confidence_encoding`, `regex_timeout`,
`is_directory`, `write_failed`). An `undo` result carries `restored`, `recreated`, and `skipped` (each with
a `reason`).

### Error codes

| Code                        | Meaning                                                                                         |
|-----------------------------|-------------------------------------------------------------------------------------------------|
| `SelectorInvalid`           | More than one of `glob`, `regex`, `paths` was given.                                             |
| `PatternInvalid`            | A regex was too long, over the repetition cap, or not valid.                                     |
| `PathOutsideRoot`           | A requested path escaped the confinement root.                                                   |
| `NotFound`                  | The path did not exist or resolved to a directory.                                               |
| `ExpectedMatchCountMismatch`| The rewritable-match count did not equal `expected_match_count`; nothing was written.            |
| `BatchNotFound`             | No batch exists for the supplied id.                                                              |
| `OperationBudgetExceeded`   | The operation ran past its wall-clock budget; narrow the selector or pattern.                    |
| `InvalidArgument`           | An argument was missing, malformed, or out of range (including an unknown `source_encoding`).    |
| `InternalError`             | An unexpected fault; details go to the log, never the client.                                    |

## Security model

**In plain terms:**

- The server can only ever write inside the single root you configured, and no argument can widen past it.
- It never writes a known secret file, and no option can change that.
- Symlinks that point outside the root, or at a secret, are refused by their real target, not their name.
- Every write is journaled with a pre-image, so a batch can be undone even after a crash mid-batch.
- Undo is a second write path, so it re-runs confinement and the denylist on every stored journal path
  before restoring; the hash gate is layered on top, not a substitute.
- No absolute path from your machine is ever returned to the model or written into the journal.

**The journal lives outside the root** (in platform app-data, keyed by a hash of the canonical root), so
this server's own write tools cannot alter its pre-images. That siting is enforced at startup: if the
journal directory would resolve inside the root, the server refuses to start.

**The limit, stated plainly:** the boundary is the filename denylist plus confinement, not content
inspection. `replace_text` will happily rewrite a `const TOKEN = "..."` sitting in an ordinary source file,
the same as editing that source yourself. Editing an existing hook or editor-autorun file (`.githooks/*`,
`.vscode/*`, `.idea/*`) that is not under `.git/` runs on the next git or editor action; that is no worse
than editing any source the agent may edit, but keep it in mind when granting the root.

## Configuration

All configuration is environment variables. The root is required; everything else has a default.

| Variable                                | Default    | Meaning                                                                                   |
|-----------------------------------------|------------|-------------------------------------------------------------------------------------------|
| `MCP_TEXTEDIT_ROOTS`                    | (required) | The single root: a bare path or a `name=path` alias. Exactly one; a second repo is a second server. |
| `MCP_TEXTEDIT_MAX_FILES`                | 1000       | Default number of files a selector call acts on.                                          |
| `MCP_TEXTEDIT_MAX_FILES_CEILING`        | 10000      | Hard ceiling the per-call file count is clamped to.                                       |
| `MCP_TEXTEDIT_MAX_FILE_BYTES`           | 5242880    | Largest file read or rewritten (5 MiB).                                                   |
| `MCP_TEXTEDIT_REGEX_TIMEOUT_MS`         | 1000       | Per-match regex timeout.                                                                  |
| `MCP_TEXTEDIT_OP_BUDGET_MS`             | 30000      | Wall-clock budget for one whole operation.                                                |
| `MCP_TEXTEDIT_REWRITE_CONFIDENCE`       | 0.65       | Minimum detection confidence to rewrite a file without an explicit `source_encoding`.     |
| `MCP_TEXTEDIT_PATTERN_LENGTH_CAP`       | 2048       | Maximum length of an agent-supplied regex pattern.                                        |
| `MCP_TEXTEDIT_JOURNAL_RETENTION_BATCHES`| 50         | Number of most-recent batches the journal keeps.                                          |
| `MCP_TEXTEDIT_JOURNAL_RETENTION_HOURS`  | 48         | Age past which a batch is eligible for pruning.                                           |

A root's name defaults to its basename. Alias a root whose basename would be unhelpful or
machine-identifying, since the name reaches the model. Names starting with `@` are reserved.

### Logging

Structured single-line JSON to a rolling file (`MCP_TEXTEDIT_LOG_FILE`, default `mcp-text-edit.log` next to
the executable), or stderr if the file cannot be opened. Set the level with `MCP_TEXTEDIT_LOG_LEVEL`
(`TRACE`..`FATAL`, default `INFO`). Never to stdout, which the stdio transport owns. Fields pass through a
fixed allowlist; the root is logged only as an 8-character hash, and a replacement or pattern is logged only
as a length and a hash, never verbatim.

## Requirements

- The .NET 10 runtime (or run a self-contained published build, which bundles it plus native SQLite and
  BLAKE3).

## Adding it to Claude Code

Publish a self-contained build (see the repo release workflow) or `dotnet run` the project, then register
it. Set `MCP_TEXTEDIT_ROOTS` to the one repository it may edit.

```jsonc
{
  "mcpServers": {
    "text-edit": {
      "command": "/path/to/text-edit",
      "env": {
        "MCP_TEXTEDIT_ROOTS": "app=/path/to/app"
      }
    }
  }
}
```

Keep the write tools on prompt (do not blanket-approve `replace_text`); their MCP annotations mark
`replace_text` destructive so a client can prompt for it. Verify by asking the agent to call
`describe_scope`; it should report the root, the caps, and the journal retention.

## Project layout

```
Server.TextEdit/
├─ Program.cs            # host spine: logging, config, DI singletons, journal open, tool registration
├─ Configuration/        # EditConfig (caps), RootRegistry (single root), startup exception
├─ Envelope/             # ResultEnvelope, ErrorEnvelope, filters echo
├─ Errors/               # error codes + the domain exception
├─ Logging/              # StdoutSentinel, allowlist JSON formatter, bootstrap, metrics events
├─ Metrics/              # SessionMetrics
├─ Journal/              # JournalPaths (siting + hardening), JournalStore (write-ahead), batch + file models
├─ Content/              # the write gate, normalizer, replacer, unified diff, undoer, codec
├─ Models/               # the wire DTOs each tool returns
└─ Tools/                # the five tools + shared pipeline
```

The security-critical primitives (root confinement, secret denylist, encoding detection, glob/regex
selection, atomic write with metadata preservation, ancestor-aware ignore evaluation) live in the shared
`RaccoonNinja.McpToolset.Files` library, so they are unit-tested without a protocol harness and shared with
the other servers in the toolset.
