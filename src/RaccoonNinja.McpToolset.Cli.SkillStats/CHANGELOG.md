## v1.0.0
- Initial release. A `PostToolUse` hook binary that reads the Skill tool's hook JSON from stdin and
  appends one row per skill invocation to a local SQLite store under `~/.skill-stats` (overridable via
  `SKILL_STATS_HOME`). Skill names are normalized by stripping leading slashes so `csharp` and
  `/csharp` collapse to one skill. Failures never block the agent: they append to `ingest-errors.log`
  (self-rotating at 5 MiB) and surface on stderr with exit `2`, while a malformed, oversized, or
  nameless payload is skipped with exit `0`.
