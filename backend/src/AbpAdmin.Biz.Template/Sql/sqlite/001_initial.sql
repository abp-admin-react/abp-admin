-- BizTemplate SQLite 基线。后续改表追加 002_*.sql，由 BizTemplateDbSchemaMigrator 按文件名顺序执行一次。

CREATE TABLE IF NOT EXISTS "BizProjects" (
    "Id" TEXT NOT NULL CONSTRAINT "PK_BizProjects" PRIMARY KEY,
    "Name" TEXT NOT NULL,
    "Description" TEXT NULL,
    "IsActive" INTEGER NOT NULL,
    "ExtraProperties" TEXT NOT NULL,
    "ConcurrencyStamp" TEXT NOT NULL,
    "CreationTime" TEXT NOT NULL,
    "CreatorId" TEXT NULL,
    "LastModificationTime" TEXT NULL,
    "LastModifierId" TEXT NULL
);

CREATE INDEX IF NOT EXISTS "IX_BizProjects_IsActive" ON "BizProjects" ("IsActive");
