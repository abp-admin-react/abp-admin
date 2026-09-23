-- OCR 契约测试脚本（仅测试程序集嵌入，不随产品发布）：顺序锚点——0020 的索引依赖本表，乱序执行必失败回滚
CREATE TABLE IF NOT EXISTS "ord_probe" (
    "id" INTEGER NOT NULL
);
