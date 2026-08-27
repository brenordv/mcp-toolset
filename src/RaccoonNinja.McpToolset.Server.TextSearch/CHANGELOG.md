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
