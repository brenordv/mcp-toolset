## v2.1.0
- New deviation D8: a `vault_save` update that omits `format` now keeps the note's stored format instead
  of resetting it to `text`; only the first-ever save defaults to `text`. Previously a re-save without the
  optional `format` argument silently downgraded markdown/json/yaml notes and broke `vault_edit_section` /
  `vault_edit_key` on them. Passing `format` explicitly still converts.

## v2.0.0
Initial release on this repo.

- Ported the [Rust](https://github.com/brenordv/mcp-file-vault) version of this MCP server to C#. 