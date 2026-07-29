# Toolset Changelog

# v4
- Added new MCP: `text-search` (read-only, root-confined file search and inspection: `describe_scope`, `find_files`, `inspect_files`, `search_text`, `read_lines`). Supports multiple named roots, opt-in package roots (cached dependency sources, targeted with `@packages`/`@all`), and a file-extension filter.
- Added shared `RaccoonNinja.McpToolset.Files` library backing it: encoding detection, root confinement with full symlink resolution, a non-overridable secret denylist, glob/regex selection, atomic writer, and a content-addressed blob store.

# v3
- Updated MCP: `file-vault` to v2.1.0.

# v2
- Added new MCP: `file-vault`.


# v1
- Initial release of `git-ops`.