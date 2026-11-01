## v1.1.0
- The hook now also runs on Claude Code's `UserPromptExpansion` event with `matcher` `.*`, next to the
  `PostToolUse` entry on the `Skill` tool. A skill or slash command you type expands into the prompt without a
  tool call, so the `PostToolUse` entry alone recorded only skills the model invoked. Add the new entry to
  `~/.claude/settings.json` per the README and keep the old one.
- A `UserPromptExpansion` payload's skill name comes from `command_name` and its arguments from
  `command_args`. The `tool_input` fields (`skill` then `name`, `args` then `input`) are still read first, so a
  `PostToolUse` payload records as before. The `prompt` field, the whole typed line, is never read. `command_args` is the argument
  text typed after the command name, stored as before (capped at 8192 characters) and returned by
  `skill_usage`.
- Behavior change: a recording failure now exits `1`, never `2`. On `UserPromptExpansion`, exit `2` blocks
  the user's prompt; exit `1` lets it through and Claude Code shows a non-blocking hook error notice with the
  stderr line. That line's prefix is now `skill-stats-cli:` (was `skill-usage:`).
- A payload with no recognized name field is now skipped with the new reason `unrecognized_shape`; `no_skill`
  now means a name field was present but empty or only slashes. The `unrecognized_shape` and `no_skill` skip
  lines in `ingest-errors.log` carry `root_keys=[...]` (names only, never values) after `tool_input_keys` when
  the root object has properties; the `oversize` skip carries neither. Each key list logs up to 32 names (was 16).
- The ingest writer gives up on a locked store after 1 s: the SQLite busy timeout and the Microsoft.Data.Sqlite
  command timeout are both 1 s (they were 5 s and the library's 30 s default). The README's hook `timeout` is
  now 10 s.

## v1.0.0
- Initial release. A `PostToolUse` hook binary that reads the Skill tool's hook JSON from stdin and
  appends one row per skill invocation to a local SQLite store under `~/.skill-stats` (overridable via
  `SKILL_STATS_HOME`). Skill names are normalized by stripping leading slashes so `csharp` and
  `/csharp` collapse to one skill. Failures never block the agent: they append to `ingest-errors.log`
  (self-rotating at 5 MiB) and surface on stderr with exit `2`, while a malformed, oversized, or
  nameless payload is skipped with exit `0`.
