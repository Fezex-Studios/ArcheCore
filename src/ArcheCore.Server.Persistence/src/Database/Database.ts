import { Database } from "bun:sqlite";

export const db =
    new Database(
        "persistence.db");

db.exec(`
    CREATE TABLE IF NOT EXISTS characters
    (
        character_id INTEGER PRIMARY KEY,

        name TEXT NOT NULL,

        level INTEGER NOT NULL,

        pos_x REAL NOT NULL,

        pos_y REAL NOT NULL,

        pos_z REAL NOT NULL
    )
`);

console.log(
    "Database Ready");