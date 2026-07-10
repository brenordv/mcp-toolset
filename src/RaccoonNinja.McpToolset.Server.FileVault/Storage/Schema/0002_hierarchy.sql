-- Vault MCP — single-parent hierarchy (migration 0002).
--
-- Each file may reference one parent file. `parent_id` is nullable (top-level notes have no parent);
-- existing rows migrate forward as top-level. Same-project integrity is enforced in the service
-- layer, since a SQLite foreign key cannot express the "same project" predicate. ON DELETE SET NULL
-- means purging a parent orphans its children rather than cascade-deleting them.

ALTER TABLE files ADD COLUMN parent_id INTEGER REFERENCES files(id) ON DELETE SET NULL;

CREATE INDEX IF NOT EXISTS idx_files_parent ON files(parent_id);
