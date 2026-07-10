-- Vault MCP: initial schema (migration 0001).
--
-- SQLite is the source of truth for mapping/metadata/versions. Content lives as
-- immutable plain-text snapshots on disk; `versions.rel_path` points at each one.

CREATE TABLE IF NOT EXISTS files (
    id              INTEGER PRIMARY KEY,
    project         TEXT    NOT NULL,
    name            TEXT    NOT NULL,
    current_version INTEGER NOT NULL,
    format          TEXT    NOT NULL,                  -- text|markdown|json|yaml
    summary         TEXT    NOT NULL,
    state           TEXT    NOT NULL DEFAULT 'active',  -- active|archived
    created_at      INTEGER NOT NULL,
    updated_at      INTEGER NOT NULL,
    UNIQUE(project, name)
);

CREATE TABLE IF NOT EXISTS versions (
    id           INTEGER PRIMARY KEY,
    file_id      INTEGER NOT NULL REFERENCES files(id) ON DELETE CASCADE,
    version      INTEGER NOT NULL,                     -- monotonic 1..N
    content_hash TEXT    NOT NULL,                     -- blake3 of snapshot bytes
    rel_path     TEXT    NOT NULL,                     -- path under files/
    byte_size    INTEGER NOT NULL,
    summary      TEXT    NOT NULL,
    op           TEXT    NOT NULL,                     -- save|append|edit_section|edit_key|restore
    created_at   INTEGER NOT NULL,
    UNIQUE(file_id, version)
);

CREATE TABLE IF NOT EXISTS tags (
    file_id INTEGER NOT NULL REFERENCES files(id) ON DELETE CASCADE,
    tag     TEXT    NOT NULL,
    PRIMARY KEY (file_id, tag)
);

-- Keyword search over name/summary/tags. A standard (non-contentless) FTS5 table is used rather
-- than `content=''`: the duplicated text is negligible at this scale and DELETE/INSERT upkeep is
-- far simpler and less error-prone than the contentless special-command protocol. The `rowid`
-- always equals `files.id`, so the two stay joinable.
CREATE VIRTUAL TABLE IF NOT EXISTS files_fts USING fts5(
    name,
    summary,
    tags,
    tokenize = 'unicode61'
);

CREATE TABLE IF NOT EXISTS meta (
    key   TEXT PRIMARY KEY,
    value TEXT NOT NULL
);
