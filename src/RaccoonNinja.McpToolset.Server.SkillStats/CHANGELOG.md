## v1.0.0
- Initial release. A read-only stdio MCP server over the local skill-usage store with three tools:
  `top_skills` (most-used skills over an optional recent window), `skill_usage` (newest invocations of
  one skill, with the recorded args), and `usage_summary` (store totals plus ingestion-health fields).
  Every connection is opened read-only (`Mode=ReadOnly`, `query_only`); the server never creates or
  migrates the store and returns `StoreUnavailable` until the `skill-usage` hook has written one.
