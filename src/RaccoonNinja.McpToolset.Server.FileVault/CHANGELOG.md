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