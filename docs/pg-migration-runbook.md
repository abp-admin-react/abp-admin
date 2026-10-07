# PostgreSQL 迁移与运行手册（A-block 迁移机制重构后）

> 适用范围：A-block 把框架从"内嵌 SQL 脚本迁移 + SQLite/PostgreSQL 双提供程序"切换为
> **PostgreSQL 单提供程序 + EF Core 迁移**之后的构建、建库、测试与部署。

## 1. 机制速览

| 事项 | 机制 | 记账表 |
| --- | --- | --- |
| 框架库 schema | `AbpAdminDbContext.Database.MigrateAsync()`（`EntityFrameworkCoreAbpAdminDbSchemaMigrator`） | `__EFMigrationsHistory` |
| BizTemplate 业务模块 | 模块自带 `Migrations/`，同上 | `__BizTemplate_EFMigrationsHistory` |
| 全自动建库 | 宿主/租户库缺失时经维护库 `postgres` 执行 `CREATE DATABASE`（要求登录角色具备 CREATEDB 且 pg_hba 放行 postgres 库） | — |

- **SQLite 支持已移除**：EF 模块不再有 `UseSqlite()` 分支，`Database:Provider` 配置键已废弃。
- **改框架表**：改 `AbpAdminDbContext`（或实体扩展）后，在仓库根执行
  ```bash
  dotnet ef migrations add <Name> \
    --project backend/src/AbpAdmin.EntityFrameworkCore \
    --startup-project backend/src/AbpAdmin.DbMigrator \
    --context AbpAdminDbContext
  ```
  业务模块同法（`--project src/AbpAdmin.Biz.Template --context BizTemplateDbContext`）。
  设计期连接串由各工程的 `*DbContextFactory` 从 Host appsettings 分层读取，无需启动宿主。
- **已有旧 schema 的库**（SQL 脚本时代建表、`__EFMigrationsHistory` 里没有 EF 迁移记录）：
  直接启动会撞 "relation already exists"。两条路：
  1. **schema 隔离（零破坏，推荐）**：连接串追加 `;SearchPath=<新schema>`，先
     `CREATE SCHEMA IF NOT EXISTS <新schema>`，再跑 DbMigrator——新旧表同库不同 schema 共存；
  2. 丢弃旧 schema 重建（需人工确认数据可弃）。

## 2. 本机/测试环境运行手册（192.168.10.250 实例）

1. **凭据**：只放 `src/AbpAdmin.HttpApi.Host/appsettings.secrets.json` 的
   `ConnectionStrings:Default`（不入库）。基底 appsettings.json 的 Default 已无意义，
   会被 secrets 覆盖。
2. **schema 隔离运行**（不动 public 旧表）：
   ```bash
   # 一次性：CREATE SCHEMA IF NOT EXISTS abp_admin_efm
   export ConnectionStrings__Default="Host=...;Port=5432;Database=abp_test2;Username=...;Password=...;SearchPath=abp_admin_efm"
   cd backend/src/AbpAdmin.DbMigrator && dotnet run   # 建表+种子（幂等）
   cd backend/src/AbpAdmin.HttpApi.Host && dotnet run
   ```
3. **pg_hba 注意**：实例只放行 `abp_test2` 库给当前用户——**新建租户库/换库名会被
   `no pg_hba.conf entry` 拒绝**。要多库租户（独立库模式），需服务器侧为用户加放行条目。

## 3. 测试环境的数据库策略

| 测试域 | 策略 |
| --- | --- |
| Application/Domain/BizTemplate/EFCore 多数 | 内存 SQLite（`AbpUnitTestSqliteDatabase`）+ provider 覆盖；**受上下文级 `UseNpgsql` 影响的上下文须在同层再 `UseSqlite()` 覆盖**（见 `BizTemplateTestModule` 注释） |
| 权限定义动态化 | `PermissionDefinitionManagementTestModule` 重开 `IsDynamicPermissionStoreEnabled`（被测能力本身） |
| 实体回滚前滚闭环 | `EntityRestoreTestModule` 开 `EntityHistorySelectors.AddAllEntities()`（Host 同款） |
| Saas 租户库接线 | `SaasTestsModule` 用记录式 Fake 替换 `ITenantDatabaseCreator`（Npgsql 建库语义不进被测面） |
| Host 级（匿名端点扫查） | 真实 PG：专用 schema `abp_admin_hosttest`（测试自建+宿主自举迁移），连接串读宿主同一分层源（appsettings.json → secrets）；**本组测试需要可达的 PG** |

## 4. 遗留与迁移点

- `appsettings.json` 的 `ConnectionStrings:Default` 仍是 SQLite 字面量（历史残留，
  实际一律被 secrets 覆盖）；首发前建议改为 PG 占位串，避免误导。
- 生产部署：多实例必须配 Redis（分布式锁/DataProtection/SignalR 背板）；
  Redis 关闭时分布式锁退化为进程内（`LocalInProcessDistributedLockProvider`），仅单实例有效。
