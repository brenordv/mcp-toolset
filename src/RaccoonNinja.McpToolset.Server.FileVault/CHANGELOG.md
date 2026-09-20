## v3.3.0
- `vault_list` `query` now degrades gracefully on multi-word input: it matches every term first (unchanged for
  queries that already hit) and, only when that matches nothing, falls back to a ranked any-term match. The result's
  new `query_mode` reports which pass ran (`all_terms` or `any_term_fallback`); it is omitted when no query was
  given. At most the first 16 terms are used.
- `vault_list` is now paginated. It gained `limit` (default 50, max 500) and an opaque keyset `cursor`, and its
  result gained `count`, `truncated`, and (when more pages exist) `cursor`. Pass the cursor back with the other
  arguments unchanged to continue; fallback-mode results are a rescue path and do not paginate (they cap and set
  `truncated` without a cursor). Behavior change: an unfiltered `vault_list` now returns at most 50 items per page
  where it previously returned every active note in one response.
- New `vault_search` tool (D11): case-insensitive substring search inside the bodies of active notes, returning per
  note the matched terms and up to three snippets of surrounding context (never whole bodies; each snippet at most
  240 characters). It takes `query` (required), `project` (omitted searches all projects), and `limit` (default 20,
  max 100), and shares `vault_list`'s all-terms-then-fallback query model. Bodies are read one at a time on demand,
  so there is no body index and no schema change; a note whose snapshot cannot be read is skipped and counted in
  `skipped`.
- New deviations D10 (`vault_list` fallback + pagination + `query_mode`) and D11 (`vault_search`). The pagination
  fields intentionally mirror the toolset's `count`/`truncated`/`cursor` naming and opaque-cursor semantics without
  adopting its `ResultEnvelope`, keeping the Rust-compatible `items` shape.
- Cursor and limit misuse, and an empty `vault_search` query, come back as `invalid_argument`.

## v3.2.0
- Argument-shape mistakes now come back as a structured error body with `code` `invalid_argument`, not a
  bare, bodiless SDK message (`An error occurred invoking '<tool>'.`). A call whose argument names do not
  fit a tool's schema is rejected before the tool runs: the error names each unknown key, suggests the
  schema name it likely meant when one is close (`tag` for `tags`), lists any missing required name, and
  echoes the full `expected_arguments`. A known argument of the wrong JSON type, the common one being
  `tags` sent as a comma-joined string instead of an array, is reported with the same code after the SDK's
  binder rejects it, carrying the SDK's fixed generic text in `sdk_error`. Genuine domain errors
  (`conflict`, `not_found`, and the rest) are untouched and keep their own code.
- Behavior change: an unknown argument name alongside valid ones is now rejected rather than silently
  ignored by the SDK.
- The shutdown metrics summary gained a `binding_error` outcome bucket per tool.

## v3.1.0
- New deviation D9: `vault_edit_section` now matches a heading by either its rendered plain text (inline
  markdown stripped, as before) or its verbatim source text with the delimiters kept. A heading that
  contains a code span, emphasis, or a link can now be targeted with the text copied straight from the
  document (e.g. ``Config for `appsettings.json` ``); previously only the backtick-stripped form matched
  and the verbatim form returned `heading_not_found`. Additive and backward-compatible: the rendered form
  still matches, so the accepted set is a strict superset.

## v3.0.0
- Adopted the shared `RaccoonNinja.McpToolset.Files` library. The snapshot store's crash-safe write now
  delegates to the shared `AtomicWriter` (temp file, flush, atomic rename), and a pre-existing same-hash
  target is still treated as satisfied. Internal refactor: the tools, wire contract, error codes, and
  on-disk layout are unchanged.
- Log files are now written as BOM-less UTF-8. The rolling file sink no longer emits a UTF-8 byte order
  mark; newly created and rolled log files are affected, while existing logs keep their BOM.
- Build: the server now publishes as a self-contained, single-file binary via the shared
  `ServerPublish.props` (native SQLite and BLAKE3 embedded).
- Docs: clarified the Deviations section and corrected the sample MCP command to the actual binary name
  (`file-vault.exe`).

## v2.1.0
- New deviation D8: a `vault_save` update that omits `format` now keeps the note's stored format instead
  of resetting it to `text`; only the first-ever save defaults to `text`. Previously a re-save without the
  optional `format` argument silently downgraded markdown/json/yaml notes and broke `vault_edit_section` /
  `vault_edit_key` on them. Passing `format` explicitly still converts.

## v2.0.0
Initial release on this repo.

- Ported the [Rust](https://github.com/brenordv/mcp-file-vault) version of this MCP server to C#. 