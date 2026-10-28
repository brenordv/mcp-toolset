## v1.3.0
- Fixed: the generic host logged `Content root path: <working directory>` at Information on every startup; the
  host's lifetime category is now held at Warning. Log change: the host's four status lines (`Application started`,
  `Hosting environment`, `Content root path`, `Application is shutting down`) are gone.
- Fixed: at a `TRACE` log level the MCP SDK wrote every JSON-RPC frame verbatim (skill names, recorded invocation
  args, session ids, workspace directories) to the log; the SDK's categories are now held at Information or the
  configured level, whichever is higher. Log change: at `DEBUG` and `TRACE`, the SDK's own lines below Information
  are gone.

## v1.2.0
- Fixed: the `exception_tail` log field, written when startup fails or when the host or the MCP SDK logs an
  exception, carried the exception's message and stack trace, which can name a file by its absolute path. It
  now carries the exception's type chain only, outer to inner, at most eight entries. Log change: the
  content of `exception_tail`; the `server_start_failed` line no longer carries the reason text, which stays
  on stderr.

## v1.1.2
- The `StoreUnavailable` message for a missing store now reads "install the skill-stats-cli hook first"; it
  named only the `PostToolUse` event, which misses skills you type. The README gains a "Recording usage"
  section with the two-event wiring (`UserPromptExpansion` plus `PostToolUse`), the upgrade from a
  `PostToolUse`-only setup, and a check that recording works. No log change.

## v1.1.1
- Internal: removed every `ConfigureAwait(false)` call. The stdio host has no synchronization context, so
  continuations run on the same threads as before. No wire or behavior change.

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
