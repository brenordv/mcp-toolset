# text-search MCP server

A local stdio MCP server that gives an agent read-only, root-confined text search and inspection.
It replaces the `find`/`grep`/`cat` habit with a handful of narrow, typed tools you can blanket-approve
once and then stop reviewing. It never writes, never leaves its configured roots, and never reads a
secret.

It searches one or more named roots: your workspace folders, plus, optionally, locally-cached dependency
sources (crates, nuget, npm, ...) exposed as **package roots** so an agent can grep the packages it
depends on without you granting broad filesystem access. Every root is confined and denylisted the same
way.

The whole design serves one property: **it stays safe when nobody is reading the calls anymore.** Two
controls hold that line and no flag can turn them off:

- **Root confinement.** Every path is resolved through every symbolic link and junction and refused if
  its real target escapes its root.
- **A secret denylist.** `.env`, private keys, `.git/`, `.ssh/`, cloud credentials, and the like are
  never read, however, the path is spelled or symlinked.

## Why use it

| Without it                                                                                                              | With it                                                                                                                 |
|-------------------------------------------------------------------------------------------------------------------------|-------------------------------------------------------------------------------------------------------------------------|
| An agent shells out to `grep -r` with a loose path; every call needs a human to confirm it won't reach `~/.ssh/id_rsa`. | Five typed tools, root-confined, that a client can auto-approve because they structurally cannot.                       |
| Ad-hoc encoding guesses corrupt a BOM-less UTF-16 file the moment it is read back.                                      | Encoding is detected (BOM, then a NUL scan before any UTF-8 attempt) and reported with a confidence.                    |
| A regex from the model hangs the shell.                                                                                 | Every regex is culture-invariant and timeout-guarded, with the pattern length and repetition capped before it compiles. |
| Absolute paths from your machine leak into the model's context.                                                         | Paths are root-relative, in and out. Nothing carries a drive letter or home directory.                                  |

## Tools

Call `describe_scope` first to learn the roots (each with a name and a kind) and the caps. The three
multi-file tools share one selector: give exactly one of `glob` (primary), `regex`, or `paths`, or none
to mean "everything under the root", optionally narrowed by `extensions`. A glob with no `/` matches the
basename at any depth, so `*.cs` is recursive.

**Targeting a root.** The `root` argument picks where to search: a root name searches that one, `@packages`
searches every package root, `@all` searches everything, and omitting it searches all workspace roots (the
common case). A search that reaches a package root must be narrowed by `glob`, `regex`, `paths`, or
`extensions`, since package trees are large. Every result carries its `root` name alongside a path relative
to that root, so the same relative path under two roots is never ambiguous.

| Tool             | Purpose                                                                 | Key parameters                                                                                                   |
|------------------|-------------------------------------------------------------------------|------------------------------------------------------------------------------------------------------------------|
| `describe_scope` | Report the roots, denylist, encoding, column unit, and every cap.       | (none)                                                                                                           |
| `find_files`     | List root-relative files with size and last-modified.                   | `glob` / `regex` / `paths`, `root`, `extensions`, `include_ignored`, `case_sensitive`, `max_files`, `cursor`                   |
| `inspect_files`  | Report encoding, BOM, line endings, final newline, and counts per file. | same selector                                                                                                    |
| `search_text`    | Grep file contents line by line (literal or regex).                     | selector + `pattern`, `is_regex`, `context_lines`, `max_matches_per_file`, `max_results`, `files_only`, `cursor` |
| `read_lines`     | Return a numbered, span-capped slice of one file.                       | `path`, `root`, `start_line`, `end_line`                                                                        |

### Notes that save a round trip

- **Column is 1-based and counts UTF-16 code units** (matching .NET and most editors). An emoji before a
  match counts as two units.
- **`search_text` matches per line.** A pattern that spans a newline will not match; `match_start`/
  `match_end` are offsets into the line.
- **`include_ignored` is off by default and never bypasses the denylist.** It is the clearest erosion of
  the auto-approval property because ignored files are exactly where local secrets live, so the denylist
  carries the whole load when it is on.
- **Denylisted files are omitted, not flagged.** Reporting that a credential file exists is itself a useful
  recon, so the tools stay silent about it.
- **List results paginate.** When `truncated` is true and a `cursor` is returned, pass the cursor back to 
  the next page. When `truncated` is true and `cursor` is null, the selection hits its ceiling: narrow it.

## The result envelope

Every tool returns the same shape. Paths inside it are always root-relative.

```jsonc
{
  "results": [ /* the payload: file entries, inspections, matches, or lines */ ],
  "count": 2,
  "truncated": false,
  "cursor": null,               // opaque; pass back to fetch the next page
  "skipped_symlinks": 0,        // on list tools: symlinked entries that were pruned
  "filters_applied": { "root": "app", "glob": "<provided>", "case_sensitive": false },
  "error": null                 // set instead of results on failure
}
```

A failure sets `error` and leaves `results` an empty list:

```jsonc
{ "results": [], "count": 0, "error": { "code": "SelectorInvalid", "message": "provide exactly one of glob, regex, paths", "detail": {} } }
```

### Error codes

| Code                      | Meaning                                                                                                                                                |
|---------------------------|--------------------------------------------------------------------------------------------------------------------------------------------------------|
| `SelectorInvalid`         | More than one of `glob`, `regex`, `paths` was given.                                                                                                   |
| `PatternInvalid`          | A regex was too long, over the repetition cap, or not valid.                                                                                           |
| `PathOutsideRoot`         | A requested root escaped the confinement root.                                                                                                         |
| `NotFound`                | The path did not exist, resolved to a directory, or was refused (a denylisted single-path read reports this rather than confirming the secret exists). |
| `IsBinary`                | The file is binary and cannot be read as text.                                                                                                         |
| `TooLarge`                | The file is larger than the configured read limit.                                                                                                     |
| `OperationBudgetExceeded` | The operation ran past its wall-clock budget; narrow the selector or pattern.                                                                          |
| `InvalidArgument`         | An argument was missing, malformed, or out of range (including a malformed `cursor`).                                                                  |
| `InternalError`           | An unexpected fault; details go to the log, never the client.                                                                                          |

## Security model

**In plain terms:**

- The server can only ever read inside the roots you configured, and no argument can widen past them.
- It never writes anything.
- It never reads a known secret file, and no option can change that.
- Symlinks that point outside a root, or at a secret, are refused by their real target, not their name.
- No root may contain another, so a cache nested under a workspace folder cannot be swept unnarrowed.
- No absolute path from your machine is ever returned to the model.

**Package roots widen what's readable, so weigh them.** Adding a package root lets the agent grep all the
third-party dependency source you have cached, which can include private-registry or vendored packages,
not only public code. What keeps that safe is not that the code is public: it is that you allowlisted the
root, the denylist runs inside it the same as anywhere, the content-secret limit below is unchanged, and
the bytes were already on your disk.

**The limit, stated plainly:** a filename denylist cannot catch a secret hardcoded *inside* an ordinary
source file. `search_text` will return a `const TOKEN = "..."` sitting in a file, the same as reading that
source yourself. The auto-approval case rests on "no worse than reading source the agent may read anyway",
not on the denylist hiding content secrets it cannot see. Package roots widen the reach of that limit to
whole third-party corpora.

## Configuration

All configurations are environment variables. At least one workspace root is required; everything else
has a default.

| Variable                              | Default    | Meaning                                                                                                        |
|---------------------------------------|------------|----------------------------------------------------------------------------------------------------------------|
| `MCP_TEXTSEARCH_ROOTS`                | (required) | Workspace roots: a `;`-separated list of directories, each a bare path or a `name=path` alias. At least one.   |
| `MCP_TEXTSEARCH_PACKAGE_ROOTS`        | (none)     | Optional package roots (same `;`-separated `name=path` syntax): cached dependency sources to expose read-only. |
| `MCP_TEXTSEARCH_MAX_FILES`            | 1000       | Default files returned per page.                                                                               |
| `MCP_TEXTSEARCH_MAX_FILES_CEILING`    | 10000      | Hard ceiling the page size is clamped to.                                                                      |
| `MCP_TEXTSEARCH_MAX_FILE_BYTES`       | 5242880    | Largest file read or inspected (5 MiB).                                                                        |
| `MCP_TEXTSEARCH_MAX_RESULTS`          | 10000      | Ceiling on matches (or files) returned per search page.                                                        |
| `MCP_TEXTSEARCH_MAX_MATCHES_PER_FILE` | 1000       | Ceiling on matches per file.                                                                                   |
| `MCP_TEXTSEARCH_MAX_CONTEXT_LINES`    | 50         | Ceiling on context lines around a match.                                                                       |
| `MCP_TEXTSEARCH_MAX_LINE_SPAN`        | 5000       | Ceiling on lines returned by one `read_lines` call.                                                            |
| `MCP_TEXTSEARCH_REGEX_TIMEOUT_MS`     | 1000       | Per-match regex timeout.                                                                                       |
| `MCP_TEXTSEARCH_OP_BUDGET_MS`         | 30000      | Wall-clock budget for one whole operation.                                                                     |

A root's name defaults to its basename (de-duplicated with a `-2` suffix on collision). Alias a root whose
basename would be unhelpful or machine-identifying, since names reach the model: a root at your home
directory would otherwise surface your username. Names starting with `@` are reserved, and no root may be
nested inside another. Standard package-cache locations to point package roots at: `~/.cargo/registry/src`
(Rust), `~/.nuget/packages` (NuGet), a project's `node_modules` (npm), `~/.m2/repository` (Maven).

### Logging

Structured single-line JSON to a rolling file (`MCP_TEXTSEARCH_LOG_FILE`, default `mcp-text-search.log`
next to the executable), or stderr if the file cannot be opened. Set the level with
`MCP_TEXTSEARCH_LOG_LEVEL` (`TRACE`..`FATAL`, default `INFO`). Never to stdout, which the stdio transport
owns. Log fields pass through a fixed allowlist, so a value can never leak through an unexpected key; the
root is logged only as an 8-character hash, never as a path. A refusal (denylist hit, out-of-root, regex
timeout) is a first-class, counted event, and a shutdown line carries the session metrics.

## Requirements

- The .NET 10 runtime (or run a self-contained published build, which bundles it).

## Adding it to Claude Code

Publish a self-contained build (see the repo release workflow) or `dotnet run` the project, then register
it. List the workspace folders in `MCP_TEXTSEARCH_ROOTS`, and optionally the dependency caches in
`MCP_TEXTSEARCH_PACKAGE_ROOTS`.

```jsonc
{
  "mcpServers": {
    "text-search": {
      "command": "/path/to/text-search",
      "env": {
        "MCP_TEXTSEARCH_ROOTS": "app=/path/to/app;lib=/path/to/lib",
        "MCP_TEXTSEARCH_PACKAGE_ROOTS": "cargo=/path/to/.cargo/registry/src"
      }
    }
  }
}
```

Verify it by asking the agent to call `describe_scope`; it should list the roots by name and kind, and the
caps.

## Project layout

```
Server.TextSearch/
├─ Program.cs            # host spine: logging, config, DI, tool registration
├─ Configuration/        # SearchConfig (caps), RootRegistry (named roots), startup exception
├─ Envelope/             # ResultEnvelope, ErrorEnvelope, filters echo
├─ Errors/               # error codes + the domain exception
├─ Logging/              # StdoutSentinel, allowlist JSON formatter, bootstrap, metrics events
├─ Metrics/              # SessionMetrics
├─ Content/              # gated reader, text document (decode + line split), search, refusal mapping
├─ Paging/               # target-pinned cursor + keyed paginator
├─ Models/               # the wire DTOs each tool returns
└─ Tools/                # the five MCP tools + shared pipeline
```

The security-critical primitives (root confinement, secret denylist, encoding detection, glob/regex
selection) live in the shared `RaccoonNinja.McpToolset.Files` library, so they are unit-tested without a
protocol harness and shared with the other servers in the toolset.
