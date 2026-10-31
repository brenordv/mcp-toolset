## v1.9.0
- Fixed: the generic host logged `Content root path: <working directory>` at Information on every startup; the
  host's lifetime category is now held at Warning. Log change: the host's four status lines (`Application started`,
  `Hosting environment`, `Content root path`, `Application is shutting down`) are gone.
- Fixed: at a `TRACE` log level the MCP SDK wrote every JSON-RPC frame verbatim (the `cwd`, paths, patterns, file
  content) to the log; the SDK's categories are now held at Information or the configured level, whichever is
  higher. Log change: at `DEBUG` and `TRACE`, the SDK's own lines below Information are gone.

## v1.8.0
- Fixed: which ignore files a directory holds was decided by `File.Exists` per name, which reports an
  existing file as absent when the server cannot read its attributes or list its directory, so such a
  file's rules were silently not applied. Presence now comes from the directory listing, and ignore-file
  names match case-insensitively on every platform. Behavior change: a `read_lines`, `read_json`,
  `inspect_files`, or `search_text` call that names a path inside a directory the server can enter but not
  list now fails with `InternalError`, as does a walk whose `cwd` is such a directory or lies under one; a
  walk still skips such a subdirectory, and a subdirectory the server can list but whose ignore file it
  cannot open now fails the walk. The `tool_error` log line for the failed call names the directory by its
  root-relative path (`.` for the root) and the filesystem exception type; no new log field. Both servers
  share the fix through `Common.Files`.

## v1.7.0
- Fixed: an extra deny entry containing a `/` (`MCP_TEXTSEARCH_EXTRA_DENY=**/.claude/settings*.json`) did
  not cover a call whose `cwd` was at or inside the directory it names, because the denylist was tested
  against the scope-relative path only. The path relative to the base root (or the package root) is now
  tested too, in the walk, in explicit `paths`, and in the read gate. Scoped calls only; an unscoped call
  is unchanged. No log shape change: a scoped `read_lines` or `read_json` of such a file now emits the
  existing `refusal` line (`refusal_reason=denylisted`) and counts in `refusals_total.denylisted`, where it
  used to succeed.

## v1.6.0
- Fixed: the `exception_tail` log field, written when startup fails or when the host or the MCP SDK logs an
  exception, carried the exception's message and stack trace, which can name a file by its absolute path. It
  now carries the exception's type chain only, outer to inner, at most eight entries. Log change: the
  content of `exception_tail`; the `server_start_failed` line no longer carries the reason text, which stays
  on stderr.

## v1.5.0
- Added: `search_text` `count_only`. It returns one `{path, match_count}` entry per file with at least one
  matching line instead of every match. `match_count` counts matching lines, so a line with two matches
  counts once, and it is exact: `max_matches_per_file` and `context_lines` do not apply. Count results page
  by path like `files_only`, and each page re-reads every selected file to the end. `count_only` together
  with `files_only` is `InvalidArgument`. `filters_applied` reports `count_only`.
- Added: the result envelope's `total_match_count`, the sum of `match_count` over every matching file before
  paging, the same value on every page. It is present only in count mode; every other result omits it.
- No log change.

## v1.4.6
- Fixed: when an ignore file existed but could not be read (locked by another process, or permission
  denied), the server log line for the failed call carried its absolute path. The line now names the
  ignore file by its root-relative path. The call still fails with `InternalError`, and an ignore
  file the server sees but cannot read is never treated as empty.

## v1.4.5
- Fixed: a `cwd`-scoped or `include_ignored` call could return too few files, or none, with
  `truncated: true` when files hidden by an ignore file above the `cwd` (or inside a re-included
  directory) sorted ahead of the wanted ones. The ignore check now runs before the result cap.
- Changed: the `include_ignored` example is `["build/**"]`; a `node_modules/` that `.gitignore` also
  lists stays hidden.

## v1.4.4
- Fixed: a `!` line in a nested `.gitignore` could re-include a path that a shallower agent-ignore file or
  `.mcpignore` hides, and one in a nested agent-ignore file could re-include a path a shallower `.mcpignore`
  hides. Ignore tiers now win by specificity regardless of directory depth, as the README's tier list
  already stated; within one tier a deeper file still overrides a shallower one. The other direction
  changes too: a `!` line in a `.mcpignore` now re-includes a path a nested `.gitignore` or agent-ignore
  file hides, so a base-root `.mcpignore` re-include applies to every project under the base root. The
  secret denylist and content scan are unaffected.

## v1.4.3
- Internal: removed every `ConfigureAwait(false)` call. The stdio host has no synchronization context, so
  continuations run on the same threads as before. No wire or behavior change.

## v1.4.2
- Fixed: an ignore-file line (`.gitignore`, an agent-ignore file, or `.mcpignore`) that translated to an
  invalid .NET regex took every content tool down with `InternalError` (`detail.type: RegexParseException`),
  even a literal `search_text` or an `inspect_files` call. The ignore files above a scoped `cwd` are read
  too, so one bad line anywhere between the base root and the file was enough. Bracket expressions now
  translate without that failure; a line that still cannot compile is skipped and every other line applies.
- Fixed: bracket expressions now follow git's reading. A `]` right after the opening `[` (or after `!`/`^`)
  is a literal member, so `[]ab]` matches `]`, `a`, or `b`, and `[]` or `[!]` with no later `]` no longer
  produces an invalid regex. A `[` inside a class is literal and never starts .NET class subtraction.
  A reversed range such as `[z-a]` matches only its first character, as in git, and no bracket expression
  matches `/`. A backslash inside brackets is a literal character, and POSIX `[:name:]` classes are not
  supported.
- Fixed: a glob argument that cannot compile (a reversed range such as `[z-a]`) now returns
  `PatternInvalid` instead of `InternalError`. The message names the parse error kind
  (`ReversedCharacterRange`), not the translated regex.

## v1.4.1
- Internal: the argument-shape name validator moved to the shared `Common.Mcp` library, shared with the
  other servers that run the same filter. No wire or behavior change.

## v1.4.0
- Argument-shape mistakes now come back as the standard failure envelope with `error.code`
  `InvalidArgument`, not a bare bodiless SDK message. A call whose argument names do not fit a tool's
  schema is rejected before the tool runs: the error names each unknown key, suggests the schema name it
  likely meant when one is close (`isRegex` for `is_regex`, `globs` for `glob`), lists any missing
  required name, and echoes the full `expected_arguments`. A known argument of the wrong JSON type is
  reported with the same code after the SDK's binder rejects it, carrying the SDK's fixed generic text in
  `detail.sdk_error`. Shape errors set `IsError = true` next to the envelope; domain errors keep `IsError`
  unset as before.
- Behavior change: an unknown argument name alongside valid ones is now rejected rather than silently
  ignored. A sloppy call that used to succeed on defaults (a misspelled `globs` that quietly let a search
  sweep the whole scope, say) now fails with a structured, correctable error.
- The shutdown metrics summary gained a `binding_error` outcome bucket, separating a client that flails on
  the argument contract from an ordinary domain error.

## v1.3.0
- Added a `read_json` tool: parse one JSON file in scope and return the whole document or only the value
  at a `json_path` (dot/bracket syntax with Python-style negative indexes, e.g. `items[-1].name`, plus
  quoted keys for names with dots), so agents stop shelling out to python to pluck a property. The read
  goes through the same gate as `read_lines` (confinement, denylist, ignore tiers, content-based secret
  detection, size cap). Comments and trailing commas are tolerated (JSONC); duplicate object keys are
  rejected. Three new error codes: `JsonInvalid` (with a 1-based line in detail), `JsonPathNotFound` (with
  the deepest resolved prefix and capped property-name/array-length hints), and `ValueTooLarge` (a value
  over the new `MCP_TEXTSEARCH_MAX_JSON_VALUE_BYTES` cap, default 1 MiB, with the same hints). Error
  messages stay fixed strings; content-derived data travels only in the error `detail`, never to the log.

## v1.2.0
- Extended the ignore boundary to honor common AI-agent ignore files (`.claudeignore`, `.cursorignore`,
  `.aiexclude`, `.aiignore`, `.codeiumignore`, `.continueignore`, `.aiderignore`, `.geminiignore`)
  alongside `.gitignore`/`.mcpignore`, ranked between them so `.mcpignore` still wins. A path listed in
  one is pruned from listings and refused by the read gate exactly as a `.gitignore` path is, on a direct
  `read_lines`/`inspect_files` as well as during discovery. Index-only files (`.cursorindexingignore`) and
  tool-scope ignores (`.npmignore`, `.dockerignore`, ...) are deliberately not honored. The honored kinds
  are disclosed in `describe_scope`'s `ignore_files`.

## v1.1.0
- Made `.gitignore`/`.mcpignore` an un-overridable boundary: an ignored file is never returned, enforced
  at the read gate, the `paths[]` gate, and listing. `include_ignored` now re-includes only the built-in
  default tier (`node_modules`, `bin`, `obj`, ...) and can never re-include a `.gitignore`/`.mcpignore`
  path, and a scoped `cwd` call now honors ignore rules in directories above the `cwd` too.
- Added content-based secret detection: a file whose content matches a known secret shape (private keys;
  AWS/Azure/GCP/Google keys; Slack/GitHub/Stripe/SendGrid tokens; URL-userinfo credentials) is withheld
  from `read_lines`, `search_text`, and `inspect_files` regardless of its name, while it still appears in
  `find_files` listings. On by default; `MCP_TEXTSEARCH_SECRET_SCAN=off` disables it and `=aggressive`
  adds higher-false-positive detectors (JWTs, generic password assignments). Disclosed in `describe_scope`.
- Added `local.settings.json` (the Azure Functions local config, which by convention holds connection
  strings and access keys) to the non-overridable secret denylist, so `include_ignored` can no longer
  surface it or leak its content through a search context window.
- Fixed the rolling log file, which was written as UTF-8 with a byte order mark; it is now BOM-less UTF-8.
  New and rolled files are affected; existing logs keep their BOM.

## v1.0.0
- Initial release: read-only, base-root-confined text search and inspection with `describe_scope`,
  `find_files`, `inspect_files`, `search_text`, and `read_lines`. Every path (the per-call `cwd` included)
  resolves through symlinks and is refused if its real target escapes the base root, and a non-overridable
  secret denylist keeps `.env`, private keys, `.git/`, and the like unreadable. Optional out-of-tree
  package roots (cached dependency sources) can be registered and searched, and the multi-file tools share
  one selector (glob, regex, or paths) narrowed by a file-extension filter.
