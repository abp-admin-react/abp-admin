# PostgreSQL 迁移与运行手册（A-block 迁移机制重构后）

> 适用范围：A-block 把框架从"内嵌 SQL 脚本迁移 + SQLite/PostgreSQL 双提供程序"切换为
> **PostgreSQL 单提供程序 + EF Core 迁移**之后的构建、建库、测试与部署。

## 1. 机制速览

| 事项 | 机制 | 记账表 |
| --- | --- | --- |
| 框架库 schema | `AbpAdminDbContext.Database.MigrateAsync()`（`EntityFrameworkCoreAbpAdminDbSchemaMigrator`） | `__EFMigrationsHistory` |
| BizTemplate 业务模块 | 模块自带 `Migrations/`，同上 | `__BizTemplate_EFMigrationsHistory` |

- **SQLite 支持已移除**：EF 模块不再有 `UseSqlite()` 分支，`Database:Provider` 配置键已移除
  （运行时与测试同为 PostgreSQL 单提供程序；测试走 Testcontainers，见 §3）。
- **启动预检（`PostgresStartupPreflight`）**：宿主与 DbMigrator 启动迁移前先校验 Default 连接串——
  出厂占位串（含 `CHANGE_ME` 标记）、连不上（5 秒探测超时）、目标库不存在（3D000）→ `LogCritical` +
  `AbpInitializationException` 拒绝启动，错误信息含掩码目标（Host/Port/Database/User，密码不回显）与
  对应指引（配置方式/排查清单/CREATE DATABASE 建库语句）；`AutoMigrateOnStartup=false` 时同样拦截。
  本系统不自动建库——建库是部署侧一次性动作（先 CREATE DATABASE 再跑迁移/宿主）。
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
  启动时自动打戳 baseline（`EfCoreLegacySchemaBaseliner`：哨兵表在而 History 无 Initial 记账 →
  建 History 并把 Initial 记账为已应用，框架与 BizTemplate 迁移器各管各的账），不再重放建表；
  打戳打 Warning 提示运维一次性 `pg_dump --schema-only` 对比 `dotnet ef migrations script` 确认无漂移。
  备用手段（自动打戳不合意时）：schema 隔离（连接串追加 `;SearchPath=<新schema>`，先
  `CREATE SCHEMA IF NOT EXISTS <新schema>`，新旧表同库不同 schema 共存）。

## 2. 本机/测试环境运行手册（192.168.10.250 实例）

1. **凭据**：只放 `src/AbpAdmin.HttpApi.Host/appsettings.secrets.json` 的
   `ConnectionStrings:Default`（入库的是开发样本；本机真实凭据改同文件后
   `git update-index --skip-worktree` 屏蔽）。基底 appsettings.json 的 Default 已无意义，
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
| Application/Domain/BizTemplate/EFCore 多数 | Testcontainers PostgreSQL（`AbpAdminTestPg`，每测试进程一个 `postgres:16` 容器、每程序集一个库）：测试模块注入容器连接串并对相应 DbContext 跑真迁移建表，用例间由测试基类 Respawn 清表隔离——运行期与测试同为 PG 单提供程序，无 provider 覆盖 |
| 权限定义动态化 | `PermissionDefinitionManagementTestModule` 重开 `IsDynamicPermissionStoreEnabled`（被测能力本身） |
| 实体回滚前滚闭环 | `EntityRestoreTestModule` 开 `EntityHistorySelectors.AddAllEntities()`（Host 同款） |
| Saas 租户库接线 | `SaasTestsModule` 用记录式 Fake 替换 `ITenantDatabaseCreator`（Npgsql 建库语义不进被测面） |
| Host 级（匿名端点扫查）/ 真库门控用例 | 容器默认库的专用 schema `abp_admin_hosttest`（测试自建+宿主自举迁移）；连接串由程序集加载时注入的 `ConnectionStrings__Default` 环境变量指向本进程容器（`RequiresHostDatabaseFact` 分层回落 secrets 仍可用）；**本组测试需要 Docker** |

## 4. 遗留与迁移点

- ~~`appsettings.json` 的 `ConnectionStrings:Default` 仍是 SQLite 字面量~~ 已改为 PG 占位串
  （`Host=localhost;...CHANGE_ME`，实际一律被 secrets/环境变量覆盖；未覆盖时 fail-fast，
  不再静默建 SQLite 文件）。`Database:Provider` 键已随之移除。
- 生产部署：多实例必须配 Redis（分布式锁/DataProtection/SignalR 背板）；
  Redis 关闭时分布式锁退化为进程内（`LocalInProcessDistributedLockProvider`），仅单实例有效。
