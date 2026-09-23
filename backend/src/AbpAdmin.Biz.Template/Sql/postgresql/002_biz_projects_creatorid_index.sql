-- 002: BizProjects.CreatorId 索引——配额检查（CreateAsync 的 CountAsync(CreatorId)）避免全表扫描。
-- 增量脚本：已应用过 001 的库由 History 表记账，只执行本脚本，不要改动 001_initial.sql。

CREATE INDEX IF NOT EXISTS "IX_BizProjects_CreatorId" ON "BizProjects" ("CreatorId");
