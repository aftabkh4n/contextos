namespace ContextOS.Storage;

// Each string is a single DDL statement executed in order inside one transaction.
//
// Version history:
//   1 — base schema (workspaces, memories, FTS5, hydration_log)
//   2 — sqlite-vec memories_vec table (DROPPED before v1; slot permanently skipped)
//   3 — embedding BLOB column on memories (v1 vector storage strategy)
//   4 — decay tracking columns (last_recalled_at, decay_days); backfills existing rows
//   5 — repair for v0.2.0 upgrade: restores memories decay-archived due to NULL last_recalled_at
//
// MigrateAsync jumps from 1 directly to 3, leaving slot 2 vacant. This avoids
// misapplying the embedding migration to any database that was at version 2
// during the development period.
internal static class Migrations
{
    internal static readonly string[] V1 =
    [
        """
        CREATE TABLE workspaces (
          id          TEXT PRIMARY KEY,
          root_path   TEXT NOT NULL UNIQUE,
          name        TEXT NOT NULL,
          repo_url    TEXT,
          created_at  INTEGER NOT NULL
        )
        """,

        """
        CREATE TABLE memories (
          id            TEXT PRIMARY KEY,
          workspace_id  TEXT NOT NULL REFERENCES workspaces(id),
          type          TEXT NOT NULL,
          content       TEXT NOT NULL,
          source        TEXT,
          tags          TEXT,
          importance    REAL NOT NULL DEFAULT 0.5,
          created_at    INTEGER NOT NULL,
          archived_at   INTEGER
        )
        """,

        "CREATE INDEX idx_memories_workspace_created ON memories(workspace_id, created_at DESC)",

        "CREATE INDEX idx_memories_type ON memories(workspace_id, type)",

        """
        CREATE VIRTUAL TABLE memories_fts USING fts5(
          content, tags, content=memories, content_rowid=rowid
        )
        """,

        // Keep FTS index in sync with the base table via triggers.
        """
        CREATE TRIGGER memories_ai AFTER INSERT ON memories BEGIN
          INSERT INTO memories_fts(rowid, content, tags) VALUES (new.rowid, new.content, new.tags);
        END
        """,

        """
        CREATE TRIGGER memories_ad AFTER DELETE ON memories BEGIN
          INSERT INTO memories_fts(memories_fts, rowid, content, tags)
            VALUES ('delete', old.rowid, old.content, old.tags);
        END
        """,

        """
        CREATE TRIGGER memories_au AFTER UPDATE ON memories BEGIN
          INSERT INTO memories_fts(memories_fts, rowid, content, tags)
            VALUES ('delete', old.rowid, old.content, old.tags);
          INSERT INTO memories_fts(rowid, content, tags) VALUES (new.rowid, new.content, new.tags);
        END
        """,

        """
        CREATE TABLE hydration_log (
          workspace_id  TEXT NOT NULL,
          session_id    TEXT NOT NULL,
          hydrated_at   INTEGER NOT NULL,
          context_hash  TEXT NOT NULL,
          PRIMARY KEY (workspace_id, session_id)
        )
        """,
    ];

    // Stored as version 3 (not 2) because slot 2 was sqlite-vec. See header comment.
    internal static readonly string[] V2 =
    [
        "ALTER TABLE memories ADD COLUMN embedding BLOB",
    ];

    // Version 4: decay tracking columns. The backfill UPDATE prevents the DecayService
    // from treating every pre-existing row as "never recalled" and archiving everything
    // older than the global decay window on first startup after upgrading to v0.2.0.
    internal static readonly string[] V3 =
    [
        "ALTER TABLE memories ADD COLUMN last_recalled_at TEXT",
        "ALTER TABLE memories ADD COLUMN decay_days INTEGER NOT NULL DEFAULT 90",
        "UPDATE memories SET last_recalled_at = strftime('%Y-%m-%dT%H:%M:%fZ','now') WHERE last_recalled_at IS NULL",
    ];

    // Version 5: repair for databases that already ran the broken V3 and had rows
    // archived by the first v0.2.0 decay pass. There is no manual archive tool,
    // so any archived row with NULL last_recalled_at was archived by decay. Restore
    // and reset the recall clock. Executed via SqliteStore so the affected count
    // can be logged.
    internal const string V4RepairSql =
        "UPDATE memories SET archived_at = NULL, " +
        "last_recalled_at = strftime('%Y-%m-%dT%H:%M:%fZ','now') " +
        "WHERE archived_at IS NOT NULL AND last_recalled_at IS NULL";
}
