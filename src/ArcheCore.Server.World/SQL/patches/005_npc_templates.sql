CREATE TABLE IF NOT EXISTS "NpcTemplates" (
    "Id"        INTEGER NOT NULL,
    "Name"      TEXT    NOT NULL,
    "Level"     INTEGER NOT NULL DEFAULT 1,
    "ModelType" TEXT    NOT NULL,
    CONSTRAINT "PK_NpcTemplates" PRIMARY KEY("Id" AUTOINCREMENT)
);