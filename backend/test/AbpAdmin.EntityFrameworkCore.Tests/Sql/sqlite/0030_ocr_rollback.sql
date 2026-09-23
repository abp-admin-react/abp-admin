-- OCR 契约测试脚本：故意失败的脚本——第二条语句炸掉后，建表与记账必须整体回滚为「未应用」
CREATE TABLE "rollback_probe" (
    "id" INTEGER NOT NULL
);
INSERT INTO "no_such_table_ocr" ("id") VALUES (1);
