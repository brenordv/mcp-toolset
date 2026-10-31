# skill-stats-cli

A tiny command-line hook that records which Claude Code skills you use. Wire it on two hook events and it
appends one row per skill use to a local SQLite database: `UserPromptExpansion` records each skill or slash
command you type, and `PostToolUse` records each skill the model invokes through the Skill tool. The
companion [skill-stats](../RaccoonNinja.McpToolset.Server.SkillStats/README.md) MCP server reads that database back so you can ask which skills you actually use.

It is not an MCP server. It is a hook binary: Claude Code runs it, it reads one JSON object from
stdin, writes one row, and exits.

## Never breaks your session

The `UserPromptExpansion` entry runs when a typed command expands, before the prompt reaches Claude. On
that event an exit code of `2` would block the prompt, so `skill-stats-cli` never returns `2`. A recording
failure appends a line to `ingest-errors.log`, prints one line to stderr naming that log, and exits `1`.
Claude Code reports that as a non-blocking `hook error` notice showing the stderr line, and the prompt goes
through. An oversized payload, or one with no usable command name, is skipped with a log line and exit
`0`. A locked store is given up on after about a second, so a wedged store never holds a prompt for long.
The CLI never writes to stdout.

## Install

Publish the binary (see the repo's `eng/publish` scripts), then add both hook entries to your
`~/.claude/settings.json`. The wiring differs by platform.

On macOS and Linux, point `command` straight at the published binary:

```json
{
  "hooks": {
    "UserPromptExpansion": [
      {
        "matcher": ".*",
        "hooks": [
          {
            "type": "command",
            "command": "<path-to>/skill-stats-cli",
            "timeout": 10
          }
        ]
      }
    ],
    "PostToolUse": [
      {
        "matcher": "Skill",
        "hooks": [
          {
            "type": "command",
            "command": "<path-to>/skill-stats-cli",
            "timeout": 10
          }
        ]
      }
    ]
  }
}
```

On Windows, do not name the `.exe` as `command` directly. Claude Code does not reliably start a bare
`.exe` given as `command`, and when it fails to start the process never runs at all: no row is
written and nothing lands in `ingest-errors.log`, because the binary is never reached. Wrap it in
`cmd.exe /c`, which Claude Code does start and which in turn launches the CLI with stdin inherited, so
the JSON payload still arrives:

```json
{
  "hooks": {
    "UserPromptExpansion": [
      {
        "matcher": ".*",
        "hooks": [
          {
            "type": "command",
            "command": "cmd.exe",
            "args": ["/c", "C:\\path\\to\\skill-stats-cli.exe"],
            "timeout": 10
          }
        ]
      }
    ],
    "PostToolUse": [
      {
        "matcher": "Skill",
        "hooks": [
          {
            "type": "command",
            "command": "cmd.exe",
            "args": ["/c", "C:\\path\\to\\skill-stats-cli.exe"],
            "timeout": 10
          }
        ]
      }
    ]
  }
}
```

`matcher: ".*"` records every command expansion; a skill added later is recorded without a settings
change. `matcher: "Skill"` limits the `PostToolUse` entry to the Skill tool, so other tool calls never
start the CLI. Settings edits are picked up by Claude Code's file watcher; restart if the first check below
shows nothing. On Unix the `command` is the absolute path to the published binary and runs through a
shell, so a bare path starts on its own. On Windows that same bare-path form is silently never started,
which is why the binary goes through `cmd.exe /c` instead. `powershell.exe` with the binary in a
`-Command` block works too, but `cmd.exe /c` is the shortest form that hands the child process stdin
unchanged. The `timeout` is a backstop: the CLI gives up on a locked store and exits on its own well
before it, and a hook killed by its timeout cannot log anything.

Version 1.0.0 documented only the `PostToolUse` entry. It records skills the model invokes, but a skill
you type expands into the prompt without a tool call, so typed commands were never recorded. Keep that
entry and add the `UserPromptExpansion` one next to it, after installing 1.1.0: on `UserPromptExpansion`,
1.0.0 exits `2` on a recording failure, which blocks the prompt.

## Where the data lives

Everything sits under `~/.skill-stats` (Unix mode `0700`):

- `skill-stats.db`: the SQLite database. Delete it to start history over.
- `ingest-errors.log`: one line per failure or skip, no payload content. Self-rotates to `ingest-errors.old` past 5 MiB.

Set `SKILL_STATS_HOME` to relocate the store. Point the hook and the `skill-stats` server at the same home, or they
read and write different databases. Keep it on a local disk: SQLite's cross-process locking is unreliable on network or
synced volumes, and the store can hold prompt-derived skill input.
On Windows the store relies on your profile's default ACLs rather than a `0700` mode.

## What gets recorded

Per invocation: the skill name (`command_name` for a typed command, the Skill tool's `tool_input` for a
model call; leading `/` stripped, so `csharp` and `/csharp` are one skill), its arguments (`command_args`,
or the tool call's arguments), the session id, the workspace directory, and a UTC timestamp.
`command_args` is everything you typed after the command name, never the full prompt line. Arguments are
stored as plain text, capped at 8192 characters, in a local user-only directory (`0700` on Unix; your
profile's ACLs on Windows), and the `skill-stats` server's `skill_usage` tool returns them verbatim into
the model's context. Do not pass tokens, keys, or personal data as skill arguments; delete
`skill-stats.db` to purge history.

## Nothing is being recorded?

- Both entries are in `~/.claude/settings.json`: `UserPromptExpansion` with `matcher` `.*`, and `PostToolUse`
  with `matcher` `Skill`.
- The `command` path is absolute and points at the executable (and is executable on Unix).
- On Windows, `command` is `cmd.exe` with the binary passed as `["/c", "<path>\\skill-stats-cli.exe"]`, not the bare
  `.exe` path. Claude Code does not start a bare `.exe`, so the store stays empty with no error line at all.

Then compare against a known-good baseline. Ask the agent to call the `skill-stats` server's `usage_summary` and note
`total_records` (before the first row lands it returns `StoreUnavailable`; read that as 0), type any skill
(`/csharp x`), and call `usage_summary` again:

- `total_records` is one higher, `last_used` is within the last minute, and `error_log_last_write` is `null` or older
  than the command: it is recording.
- `total_records` did not move and `error_log_last_write` is just now: open `ingest-errors.log`. A
  `skipped reason=unrecognized_shape root_keys=[...]` line means the payload shape changed, and the listed keys are the
  real field names to compare against `command_name` and `command_args`. A `<stage> <Exception>: <message>` line means
  the store.
- `total_records` did not move and nothing was written to the error log: the hook never ran. Check the event name, the
  Windows bare-`.exe` form, and the hook `timeout`, since a timed-out hook is killed before it can log.

That checks the `UserPromptExpansion` entry. To check the `PostToolUse` entry, ask the agent to use a skill
without typing its command; the count goes up by one the same way.

If `home_overridden` is `true`, the hook's `SKILL_STATS_HOME` must name the same directory. If it is `false` and you
set the variable for the hook, the two use different stores.
