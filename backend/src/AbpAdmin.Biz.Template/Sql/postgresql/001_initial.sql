-- BizTemplate PostgreSQL 基线。后续改表追加 002_*.sql，由 BizTemplateDbSchemaMigrator 按文件名顺序执行一次。

CREATE TABLE IF NOT EXISTS "BizProjects" (
    "Id" uuid NOT NULL,
    "Name" character varying(128) NOT NULL,
    "Description" character varying(2048) NULL,
    "IsActive" boolean NOT NULL,
    "ExtraProperties" text NOT NULL,
    "ConcurrencyStamp" character varying(40) NOT NULL,
    "CreationTime" timestamp with time zone NOT NULL,
    "CreatorId" uuid NULL,
    "LastModificationTime" timestamp with time zone NULL,
    "LastModifierId" uuid NULL,
    CONSTRAINT "PK_BizProjects" PRIMARY KEY ("Id")
);

CREATE INDEX IF NOT EXISTS "IX_BizProjects_IsActive" ON "BizProjects" ("IsActive");
