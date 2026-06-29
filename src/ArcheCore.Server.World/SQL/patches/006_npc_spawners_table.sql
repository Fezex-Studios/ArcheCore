CREATE TABLE IF NOT EXISTS "NpcSpawners" (
    "Id"         INTEGER NOT NULL,
    "TemplateId" INTEGER NOT NULL,
    "X"          REAL    NOT NULL,
    "Y"          REAL    NOT NULL,
    "Z"          REAL    NOT NULL,
    "Count"      INTEGER NOT NULL DEFAULT 1,
    "Radius"     REAL    NOT NULL DEFAULT 3.0,
    CONSTRAINT "PK_NpcSpawners" PRIMARY KEY("Id" AUTOINCREMENT)
);