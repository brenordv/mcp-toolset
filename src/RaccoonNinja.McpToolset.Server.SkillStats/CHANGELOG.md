## v1.1.0
- Argument-shape mistakes now come back as an `InvalidArgument` failure envelope instead of the SDK's bare
  `An error occurred invoking '<tool>'.`. A call whose argument names do not fit a tool's schema is rejected
  before the tool runs, and a known argument of the wrong JSON type is reported with the same code after the
  SDK's binder rejects it. The diagnostics ride in the message (this server's error body is a code and a
  message only): it names each unknown argument, suggests the schema name it likely meant when one is close
  (`limit` for `limitt`), and lists the valid argument names. Shape errors set `IsError = true`; domain
  errors keep `IsError` unset. Behavior change: an unknown argument name alongside valid ones is now rejected
  rather than silently ignored by the SDK. The shared name-validator lives in the new `Common.Mcp` library.

## v1.0.0
- Initial release. A read-only stdio MCP server over the local skill-usage store with three tools:
  `top_skills` (most-used skills over an optional recent window), `skill_usage` (newest invocations of
  one skill, with the recorded args), and `usage_summary` (store totals plus ingestion-health fields).
  Every connection is opened read-only (`Mode=ReadOnly`, `query_only`); the server never creates or
  migrates the store and returns `StoreUnavailable` until the `skill-usage` hook has written one.
