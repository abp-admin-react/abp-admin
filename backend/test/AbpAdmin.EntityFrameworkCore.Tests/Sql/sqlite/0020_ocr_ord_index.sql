-- OCR 契约测试脚本：依赖 0010 建的表存在（Ordinal 执行顺序契约，0020 先于 0010 即失败回滚）
CREATE INDEX IF NOT EXISTS "IX_ord_probe" ON "ord_probe" ("id");
