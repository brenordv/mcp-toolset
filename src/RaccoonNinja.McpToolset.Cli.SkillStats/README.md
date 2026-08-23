# skill-stats-cli

A tiny command-line hook that records which Claude Code skills you use. Wire it as a `PostToolUse`
hook on the `Skill` tool and it appends one row per skill invocation to a local SQLite database. The
companion [skill-stats](../RaccoonNinja.McpToolset.Server.SkillStats/README.md) MCP server reads that database back so you can ask which skills you actually use.

It is not an MCP server. It is a hook binary: Claude Code runs it, it reads one JSON object from
stdin, writes one row, and exits.

## Never breaks your session

The tool call has already run by the time the hook fires, so `skill-stats-cli` can only observe, never
block. Any failure is swallowed: it appends a line to `ingest-errors.log`, prints one line to stderr
naming that log, and exits `2`. On a PostToolUse hook, exit `2` is the code that surfaces stderr back
to Claude, so a persistent problem becomes visible in-session without ever interrupting the work. A
malformed or oversized payload, or a skill it cannot name, is skipped silently with exit `0`.

## Install

Publish the binary (see the repo's `eng/publish` scripts), then add the hook to your
`~/.claude/settings.json`:

```json
{
  "hooks": {
    "PostToolUse": [
      {
        "matcher": "Skill",
        "hooks": [
          {
            "type": "command",
            "command": "<path-to>/skill-stats-cli",
            "timeout": 30
          }
        ]
      }
    ]
  }
}
```

`matcher: "Skill"` scopes the hook to the built-in Skill tool. `command` is the absolute path to the published binary 
(`skill-stats-cli.exe` on Windows); it takes no arguments, so there are no quoting subtleties. The `timeout` guards against
a wedged store; ingest normally finishes in well under a second.

## Where the data lives

Everything sits under `~/.skill-stats` (Unix mode `0700`):

- `skill-stats.db`: the SQLite database. Delete it to start history over.
- `ingest-errors.log`: one line per failure or skip, no payload content. Self-rotates to `ingest-errors.old` past 5 MiB.

Set `SKILL_STATS_HOME` to relocate the store. Point the hook and the `skill-stats` server at the same home, or they 
read and write different databases. Keep it on a local disk: SQLite's cross-process locking is unreliable on network or
synced volumes, and the store can hold prompt-derived skill input. 
On Windows the store relies on your profile's default ACLs rather than a `0700` mode.

## What gets recorded

Per invocation: the skill name (leading `/` stripped, so `csharp` and `/csharp` are one skill), the skill input as text
(capped at 8192 chars), the session id, the workspace directory, and a UTC timestamp. The skill input can contain 
prompt text, which is why the store stays local and user-restricted.

## Nothing is being recorded?

- The hook is in `~/.claude/settings.json` under `PostToolUse` with `matcher` spelled exactly `Skill`.
- The `command` path is absolute and points at the executable (and is executable on Unix).
- Read `~/.skill-stats/ingest-errors.log` for skipped or failed lines.
- Ask the `skill-stats` server's `usage_summary`: it reports the row count and the error-log size and
  last-write time, so "the hook never fired" and "the hook fired but failed" are distinguishable.
