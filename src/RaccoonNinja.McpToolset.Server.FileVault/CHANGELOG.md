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