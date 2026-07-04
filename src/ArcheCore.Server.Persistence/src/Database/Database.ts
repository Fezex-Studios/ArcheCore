import { Database } from "bun:sqlite";

export const db =
    new Database(
        "persistence.db");

db.exec(`
    CREATE TABLE IF NOT EXISTS characters
    (
        character_id INTEGER PRIMARY KEY AUTOINCREMENT,
        account_id   INTEGER NOT NULL,
        name         TEXT    NOT NULL,
        level        INTEGER NOT NULL DEFAULT 1,
        pos_x        REAL    NOT NULL DEFAULT 0,
        pos_y        REAL    NOT NULL DEFAULT 2,
        pos_z        REAL    NOT NULL DEFAULT 0
    )
`);

console.log(
    "Database Ready");