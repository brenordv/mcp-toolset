-- Skill-usage statistics: initial schema (migration 0001).
--
-- One row per Skill tool invocation, appended by the skill-usage ingest CLI and read by the
-- skill-stats MCP server. Rows are never deleted, so the primary key is a plain rowid.

CREATE TABLE meta (
    key   TEXT PRIMARY KEY,
    value TEXT NOT NULL
);

CREATE TABLE skill_usage (
    id         INTEGER PRIMARY KEY,
    used_at    INTEGER NOT NULL,   -- unix epoch seconds, UTC
    skill      TEXT    NOT NULL,   -- normalized name (leading slashes stripped)
    args       TEXT,               -- skill input as text, capped at 8192 chars
    session_id TEXT,
    cwd        TEXT                -- workspace dir as reported by the hook
);

CREATE INDEX idx_skill_usage_skill_used_at ON skill_usage(skill, used_at);
CREATE INDEX idx_skill_usage_used_at ON skill_usage(used_at);