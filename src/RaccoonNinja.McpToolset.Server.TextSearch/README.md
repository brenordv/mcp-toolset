# text-search MCP server

A local stdio MCP server that gives an agent read-only, confined text search and inspection. It replaces
the `find`/`grep`/`cat` habit with a handful of narrow, typed tools you can blanket-approve once and then
stop reviewing. It never writes, never leaves its configured base root, and never reads a secret.

It searches under a single **base root**: point it at a directory that holds your projects, and a per-call
`cwd` argument picks which project to scope to (or omit `cwd` to search the whole base at once). This is the
git-ops working-directory pattern applied to search: the same knob set tight per-project or wide across all
of them, without configuring each project separately. Serve a second, disjoint tree by running a second
instance.

The whole design serves one property: **it stays safe when nobody is reading the calls anymore.** Two
controls hold that line and no flag can turn them off:

- **Base-root confinement.** Every path, including the per-call `cwd`, is resolved through every symbolic
  link and junction and refused if its real target escapes the base root.
- **A secret denylist.** `.env`, private keys, `.git/`, `.ssh/`, cloud credentials, and the like are never
  read, however, the path is spelled or symlinked. A `cwd` that points at or inside a denylisted directory is
  refused too, so it cannot become an effective root that sheds the protected segment.

## Why use it

| Without it                                                                                                              | With it                                                                                                                 |
|-------------------------------------------------------------------------------------------------------------------------|-------------------------------------------------------------------------------------------------------------------------|
| An agent shells out to `grep -r` with a loose path; every call needs a human to confirm it won't reach `~/.ssh/id_rsa`. | Five typed tools, base-root-confined, that a client can auto-approve because they structurally cannot.                  |
| Ad-hoc encoding guesses corrupt a BOM-less UTF-16 file the moment it is read back.                                      | Encoding is detected (BOM, then a NUL scan before any UTF-8 attempt) and reported with a confidence.                    |
| A regex from the model hangs the shell.                                                                                 | Every regex is culture-invariant and timeout-guarded, with the pattern length and repetition capped before it compiles. |
| Absolute paths from your machine leak into the model's context.                                                         | Paths are scope-relative, in and out. Nothing carries a drive letter or home directory.                                 |

## Tools

Call `describe_scope` first to learn the base root, how `cwd` scoping works, the "ignore" tiers, the denylist,
and the caps. The three multi-file tools share one selector: give exactly one of `glob` (primary), `regex`,
or `paths`, or none to mean "everything in the scope", optionally narrowed by `extensions`. A glob with no
`/` matches the basename at any depth, so `*.cs` is recursive.

**Scoping a call.** The optional `cwd` argument is an absolute working directory inside the base root. Pass
it to scope the call to one project: input and output paths are then relative to that `cwd`. Omit it 
searching the whole base root, which is the heavy path; paths are then relative to the base root, so a hit
still says which project it is in. A `cwd` that escapes the base, is not a directory, or lands on or inside a
protected directory is refused with a path-free error.

| Tool             | Purpose                                                                 | Key parameters                                                                                                   |
|------------------|-------------------------------------------------------------------------|------------------------------------------------------------------------------------------------------------------|
| `describe_scope` | Report the base root, scope model, ignore tiers, denylist, and caps.    | (none)                                                                                                           |
| `find_files`     | List scope-relative files with size and last-modified.                  | `glob` / `regex` / `paths`, `cwd`, `extensions`, `include_ignored`, `case_sensitive`, `max_files`, `cursor`      |
| `inspect_files`  | Report encoding, BOM, line endings, final newline, and counts per file. | same selector                                                                                                    |
| `search_text`    | Grep file contents line by line (literal or regex).                     | selector + `pattern`, `is_regex`, `context_lines`, `max_matches_per_file`, `max_results`, `files_only`, `cursor` |
| `read_lines`     | Return a numbered, span-capped slice of one file.                       | `path`, `cwd`, `start_line`, `end_line`                                                                          |

### Notes that save a round trip

- **Column is 1-based and counts UTF-16 code units** (matching .NET and most editors). An emoji before a
  match counts as two units.
- **`search_text` matches per line.** A pattern that spans a newline will not match; `match_start`/
  `match_end` are offsets into the line.
- **`include_ignored` takes globs, not a boolean.** Pass globs (for example `["node_modules/**"]`) to
  re-include otherwise-ignored paths for one call; omit or pass an empty list to keep every ignore tier in
  force. It never bypasses the secret denylist, which runs first and independently. It is the clearest
  erosion of the auto-approval property because ignored files are exactly where local secrets live, so the
  denylist carries the whole load when it is on.
- **Denylisted files are omitted, not flagged.** Reporting that a credential file exists is itself a useful
  recon, so the tools stay silent about it.
- **List results paginate.** When `truncated` is true and a `cursor` is returned, pass the cursor back for
  the next page (keep `cwd` stable across pages). When `truncated` is true and `cursor` is null, the
  selection hits its ceiling: narrow it.

## The result envelope

Every tool returns the same shape. Paths inside it are always scope-relative (relative to `cwd`, or to the
base root when `cwd` is omitted).

```jsonc
{
  "results": [ /* the payload: file entries, inspections, matches, or lines */ ],
  "count": 2,
  "truncated": false,
  "cursor": null,               // opaque; pass back to fetch the next page
  "skipped_symlinks": 0,        // on list tools: symlinked entries that were pruned
  "filters_applied": { "cwd": ".", "glob": "<provided>", "case_sensitive": false },
  "error": null                 // set instead of results on failure
}
```

The `cwd` echo is base-relative (`.` for the whole base, `foo` for a scoped call), never your absolute
working directory. A failure sets `error` and leaves `results` an empty list:

```jsonc
{ "results": [], "count": 0, "error": { "code": "SelectorInvalid", "message": "provide exactly one of glob, regex, paths", "detail": {} } }
```

### Error codes

| Code                      | Meaning                                                                                                                                                |
|---------------------------|--------------------------------------------------------------------------------------------------------------------------------------------------------|
| `SelectorInvalid`         | More than one of `glob`, `regex`, `paths` was given.                                                                                                   |
| `PatternInvalid`          | A regex, or an `include_ignored` glob, was too long, over the repetition cap, or not valid.                                                            |
| `PathOutsideRoot`         | A requested path escaped the base root.                                                                                                                |
| `NotFound`                | The path did not exist, resolved to a directory, or was refused (a denylisted single-path read reports this rather than confirming the secret exists). |
| `IsBinary`                | The file is binary and cannot be read as text.                                                                                                         |
| `TooLarge`                | The file is larger than the configured read limit.                                                                                                     |
| `OperationBudgetExceeded` | The operation ran past its wall-clock budget; narrow the selector or pattern.                                                                          |
| `InvalidArgument`         | An argument was missing, malformed, or out of range, including a malformed `cursor` or a `cwd` that escapes, is not a directory, or is denylisted.     |
| `InternalError`           | An unexpected fault; details go to the log, never the client.                                                                                          |

## Security model

**In plain terms:**

- The server can only ever read inside the base root you configured, and no argument, including `cwd`, can
  widen past it.
- It never writes anything.
- It never reads a known secret file, and no option can change that. A `cwd` at or inside a denylisted
  directory is refused before it can become an effective root.
- Symlinks that point outside the base root, or at a secret, are refused by their real target, not their
  name.
- No absolute path from your machine is ever returned to the model.

**The reachable surface is everything under the base root, so keep the base tight.** The base root is the one
boundary now, so the server refuses a dangerously broad base at startup: a filesystem or drive root, your
home directory, a base whose own path carries a denylisted segment (for example, one under `.ssh`), or a base
placed directly on a protected parent directory. The denylist still runs inside the base the same as
anywhere.

**Stating the obvious security limition:** a filename denylist cannot catch a secret hardcoded *inside* an ordinary
source file. `search_text` will return a `const TOKEN = "..."` sitting in a file, the same as reading that
source yourself. The auto-approval case rests on "no worse than reading source the agent may read anyway",
not on the denylist hiding content secrets it cannot see.

## Ignore tiers

Three tiers decide which non-secret files a walk skips, applied least-specific first so a later tier wins
(git last-match-wins semantics):

1. **A built-in default ignore set** (heavy build and dependency directories: `node_modules/`, `bin/`,
   `obj/`, `target/`, `dist/`, `.venv/`, `__pycache__/`, and the like). This covers projects that have no
   "ignore file" (like `.gitignore`, etc.) yet.
2. **`.gitignore`**, honored per directory as git does.
3. **`.mcpignore`**, the most specific tier, which overrides both above. Use a base-root `.mcpignore` to
   *augment* the defaults rather than replace them.

None of these is a security boundary: the secret denylist is independent and always applies. Glob-scoped
`include_ignored` re-includes named ignored paths for one call and still never reaches a denylisted path.

**Scoped-ancestor-ignore caveat.** Because a `cwd` becomes the effective root, ignore files in directories
*between* the base root and `cwd` are not consulted. A scoped call can therefore surface a non-secret file a
parent `.gitignore` would have hidden; the whole-base walk (no `cwd`) does load each project's root ignore
file. The default-ignore tier partially compensates. This exposes no secret, since the denylist is
unaffected.

## Configuration

All configurations are environment variables. Only the base root is required; everything else has a default.

| Variable                              | Default    | Meaning                                                                                                                                                                                                                                                |
|---------------------------------------|------------|--------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------|
| `MCP_TEXTSEARCH_BASE_ROOT`            | (required) | The single confinement root: a bare absolute path to the directory that holds your projects. Fatal if unset, not an existing directory, or dangerously broad.                                                                                          |
| `MCP_TEXTSEARCH_DEFAULT_IGNORE`       | (built-in) | `off` disables the built-in default ignore set; a file path replaces it with that file's patterns (a missing path is fatal); unset keeps the built-ins.                                                                                                |
| `MCP_TEXTSEARCH_EXTRA_DENY`           | (none)     | `;`-separated additive deny patterns. A trailing `/` denies a bare directory segment at any depth; otherwise it is a file-name glob. It can only tighten the built-in denylist, never remove from it; absolute-looking or malformed entries are fatal. |
| `MCP_TEXTSEARCH_MAX_FILES`            | 1000       | Default files returned per page.                                                                                                                                                                                                                       |
| `MCP_TEXTSEARCH_MAX_FILES_CEILING`    | 10000      | Hard ceiling the page size is clamped to.                                                                                                                                                                                                              |
| `MCP_TEXTSEARCH_MAX_FILE_BYTES`       | 5242880    | Largest file read or inspected (5 MiB).                                                                                                                                                                                                                |
| `MCP_TEXTSEARCH_MAX_RESULTS`          | 10000      | Ceiling on matches (or files) returned per search page.                                                                                                                                                                                                |
| `MCP_TEXTSEARCH_MAX_MATCHES_PER_FILE` | 1000       | Ceiling on matches per file.                                                                                                                                                                                                                           |
| `MCP_TEXTSEARCH_MAX_CONTEXT_LINES`    | 50         | Ceiling on context lines around a match.                                                                                                                                                                                                               |
| `MCP_TEXTSEARCH_MAX_LINE_SPAN`        | 5000       | Ceiling on lines returned by one `read_lines` call.                                                                                                                                                                                                    |
| `MCP_TEXTSEARCH_REGEX_TIMEOUT_MS`     | 1000       | Per-match regex timeout.                                                                                                                                                                                                                               |
| `MCP_TEXTSEARCH_OP_BUDGET_MS`         | 30000      | Wall-clock budget for one whole operation.                                                                                                                                                                                                             |

Point `MCP_TEXTSEARCH_BASE_ROOT` at the directory that contains your projects, for example, a `~/dev/projects`
that holds one folder per repository. An agent then passes `cwd` (the absolute path of one project inside it)
to scope a call or omits `cwd` to search across every project at once. The base basename reaches the model
through `describe_scope`, so avoid a base directory whose name is machine-identifying.

### Logging

Structured single-line JSON to a rolling file (`MCP_TEXTSEARCH_LOG_FILE`, default `mcp-text-search.log`
next to the executable), or stderr if the file cannot be opened. Set the level with
`MCP_TEXTSEARCH_LOG_LEVEL` (`TRACE`..`FATAL`, default `INFO`). Never to stdout, which the stdio transport
owns. Log fields pass through a fixed allowlist, so a value can never leak through an unexpected key; the
base root is logged only as an 8-character hash, never as a path. A refusal (denylist hit, out-of-base
`cwd`, regex timeout) is a first-class, counted event, and a shutdown line carries the session metrics.

## Requirements

- The .NET 10 runtime (or run a self-contained published build, which bundles it).

## Adding it to Claude Code

Publish a self-contained build (see the repo release workflow) or `dotnet run` the project, then register
it. Set `MCP_TEXTSEARCH_BASE_ROOT` to the directory that holds your projects.

```jsonc
{
  "mcpServers": {
    "text-search": {
      "command": "/absolute/path/to/text-search",
      "env": {
        "MCP_TEXTSEARCH_BASE_ROOT": "/absolute/path/to/projects",
        "MCP_TEXTSEARCH_EXTRA_DENY": "*.secret;private/"
      }
    }
  }
}
```

Verify it by asking the agent to call `describe_scope`; it should report the base root by name, the scope
model, the ignore tiers, the denylist, and the caps.

## Project layout

```
Server.TextSearch/
├─ Program.cs            # host spine: logging, config, DI, tool registration
├─ Configuration/        # SearchConfig (caps), ScopeResolver (base root + per-call cwd scope), CallScope, startup exception
├─ Envelope/             # ResultEnvelope, ErrorEnvelope, filters echo
├─ Errors/               # error codes + the domain exception
├─ Logging/              # StdoutSentinel, allowlist JSON formatter, bootstrap, metrics events
├─ Metrics/              # SessionMetrics
├─ Content/              # gated reader, text document (decode + line split), search, refusal mapping
├─ Paging/               # scope-pinned cursor + keyed paginator
├─ Models/               # the wire DTOs each tool returns
└─ Tools/                # the five MCP tools + shared pipeline
```

The security-critical primitives (root confinement, secret denylist, encoding detection, glob/regex
selection) live in the shared `RaccoonNinja.McpToolset.Files` library, so they are unit-tested without a
protocol harness and shared with the other servers in the toolset.
