import { Database } from "bun:sqlite";
import { AuthDatabasePath } from "../ServerConfig";

export const db = new Database(AuthDatabasePath);

db.exec(`
    CREATE TABLE IF NOT EXISTS accounts
    (
        account_id    INTEGER PRIMARY KEY AUTOINCREMENT,
        username      TEXT    UNIQUE NOT NULL,
        password_hash TEXT    NOT NULL
    )
`);

db.exec(`
    CREATE TABLE IF NOT EXISTS sessions
    (
        token      TEXT    PRIMARY KEY,
        account_id INTEGER NOT NULL UNIQUE,   -- one active session per account, enforced by DB
        expires_at TEXT    NOT NULL
    )
`);

// Tracks consecutive failed login attempts per username for lockout
db.exec(`
    CREATE TABLE IF NOT EXISTS failed_logins
    (
        username      TEXT    PRIMARY KEY,
        attempts      INTEGER NOT NULL DEFAULT 0,
        locked_until  TEXT    -- NULL means not locked
    )
`);

// Purge expired sessions on startup and every hour after that
function purgeExpiredSessions(): void {
    const result = db
        .prepare(`DELETE FROM sessions WHERE expires_at < ?`)
        .run(new Date().toISOString());

    if (result.changes > 0)
        console.log(`[DB] Purged ${result.changes} expired session(s)`);
}

purgeExpiredSessions();
setInterval(purgeExpiredSessions, 60 * 60 * 1000);

console.log(`Database Ready (${AuthDatabasePath})`);