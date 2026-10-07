# Migration {旧版本} - {新版本}

> 发布日期:{YYYY-MM-DD}。派生项目升级步骤:合并 upstream → 按下表核对依赖与配置 → 跑 DbMigrator(或宿主自动迁移)→ 按"数据库迁移"表核对 History 表 → 全量构建 + 测试。

## 模块变更

<!-- 按模块/项目前缀列表,一条一行。破坏性变更加 **[破坏性]** 前缀并写迁移动作。 -->

- `AbpAdmin.HttpApi.Host`:示例条目——说明行为变化与影响面。

## 依赖项变更

<!-- 只有 Directory.Packages.props 主动调整的才记录;传递升级不记。 -->

| 库 | 原版本 | 现版本 |
| --- | --- | --- |
| Volo.Abp.* | 10.6.0 | 10.6.1 |

## 数据库迁移

<!-- 与迁移机制对齐:框架库走 EF 迁移(__EFMigrationsHistory),业务模块各自 History 表;派生项目照此核对。 -->

| 项目 | 迁移/脚本 | 说明 |
| --- | --- | --- |
| AbpAdmin.EntityFrameworkCore | Migrations/Initial | 框架库初始迁移(示例) |

## 配置变更

<!-- appsettings 新增/删除/改名;默认值;是否必填。启动即炸的问题 90% 在这节。 -->

| 键 | 变更 | 默认值 | 必填 |
| --- | --- | --- | --- |
| `Redis:IsEnabled` | 新增 | `true`(键缺失视为启用) | 否 |
