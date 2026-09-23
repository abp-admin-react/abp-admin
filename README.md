# AbpAdmin

ABP 分层后端 + Ant Design Pro 管理前端。默认开发环境：

| 端 | 技术 | 地址 |
| --- | --- | --- |
| 后端 API | ABP 10.6 / .NET 10 | https://localhost:44395 |
| 前端 | Umi Max + Ant Design Pro | http://localhost:8000 |
| 数据库 | SQLite（默认） | `backend/AbpAdmin.db` |

仓库不包含数据库文件。克隆后必须先跑 `DbMigrator`，**只启动 Host 不会自动建库**。

默认管理员：`admin` / `1q2w3E*`

---

## 环境要求

- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet)
- [Node.js 22+](https://nodejs.org/)（前端 `package.json` 要求 `>=22`）
- [pnpm 10](https://pnpm.io/zh/installation)（前端包管理；可用 `corepack enable` 启用）
- 可选：[ABP CLI](https://abp.io/docs/latest/cli)（`dotnet tool install -g Volo.Abp.Cli`，用于 `abp install-libs`）

首次跑 HTTPS 后端时，建议先信任开发证书：

```bash
dotnet dev-certs https --trust
```

---

## 仓库结构

ABP 官方 Layered 模板的 **Host + 独立 SPA 变体**（以 `HttpApi.Host` 承载 API / 认证，前端独立放在 `web/`，没有 `.Web` 项目），分层与官方等价；各层内部按 feature 文件夹组织（如 `AbpAdmin.Application/Identity/`），与官方 BookStore 教程一致。

```
AbpAdmin/
├── backend/                              # 后端解决方案（.NET；VS 打开 backend/AbpAdmin.slnx）
│   ├── src/                              # 9 个项目
│   │   ├── AbpAdmin.Domain.Shared        # 常量 / 枚举 / 本地化资源（依赖链根）
│   │   ├── AbpAdmin.Domain               # 实体 / 领域服务 / 仓储接口
│   │   ├── AbpAdmin.Application.Contracts # 应用服务接口 + DTO + 权限定义
│   │   ├── AbpAdmin.Application          # 应用服务实现
│   │   ├── AbpAdmin.EntityFrameworkCore  # DbContext / 仓储实现 / 迁移
│   │   ├── AbpAdmin.HttpApi              # 少量定制 Controller（Auto API 为主）
│   │   ├── AbpAdmin.HttpApi.Client       # C# 动态客户端代理
│   │   ├── AbpAdmin.HttpApi.Host         # API / OIDC 宿主
│   │   └── AbpAdmin.DbMigrator           # 建库 / 迁移 / 种子数据
│   ├── test/                             # 6 个测试项目（TestBase、Domain/Application/EFCore.Tests、HttpApi.Host.Tests、ConsoleTestApp）
│   └── etc/                              # ABP Studio 运行配置 / nginx / quartz / 初始化脚本
├── web/                                  # 前端（Ant Design Pro v6 · Umi Max · pnpm）
└── docs/                                 # 测试方案与执行记录、升级回归清单、已知问题
```

日志、临时脚本、`*.db` 等**本地调试产物不入库**（规则见 `.gitignore`）；`backend/AbpAdmin.db` 是本地运行库，由 DbMigrator 生成。

---

## 一、初始化

在后端目录 `backend/`（含 `AbpAdmin.slnx`，即解决方案根目录）操作。

### 1. 还原并编译后端

```bash
dotnet restore
dotnet build
```

如需安装 ABP 客户端静态库（LeptonX Lite 主题等）：

```bash
abp install-libs
```

也可以一次性执行：

```powershell
./backend/etc/scripts/initialize-solution.ps1
```

该脚本会编译、`abp install-libs`，并运行一次 `DbMigrator`。

### 2. 初始化数据库

Host **启动时不会**创建数据库。必须先运行 Migrator：

```bash
cd backend/src/AbpAdmin.DbMigrator
dotnet run
```

或：

```powershell
./backend/etc/scripts/migrate-database.ps1
```

Migrator 会：

1. 若 `AbpAdmin.db` 不存在，自动创建 SQLite 文件
2. 执行 EF Core 迁移建表
3. 写入种子数据（管理员、OpenIddict 客户端等）

之后新增迁移，也要再跑一次 Migrator。

**数据库配置单一来源**：`Database:Provider` 与 `ConnectionStrings:Default` 只维护在 `backend/src/AbpAdmin.HttpApi.Host/appsettings.json`（凭证放同目录 `appsettings.secrets.json`）。DbMigrator 与 EF 设计时工具自动跟随读取，本项目 appsettings 只保留迁移器专属配置。生效顺序（后者覆盖前者）：

1. `HttpApi.Host/appsettings.json`（共享基座）
2. `HttpApi.Host/appsettings.secrets.json`（共享凭证）
3. `DbMigrator/appsettings.json`（迁移器专属：Quartz / Identity。OpenIddict 客户端清单已统一在 Host 基座，不再重复维护）
4. 环境变量（**最高**，生产/CI 一律走此通道：`Database__Provider` / `ConnectionStrings__Default`）

当前默认（PG）：

```json
"Database": { "Provider": "PostgreSql" },
"ConnectionStrings": {
  "Default": "Host=...;Port=5432;Database=...;Username=...;Password=..."
}
```

SQLite 分支：连接串 `Data Source=../../AbpAdmin.db;` 路径相对运行目录，从 `backend/src/AbpAdmin.DbMigrator` 或 `backend/src/AbpAdmin.HttpApi.Host` 启动时指向解决方案根目录 `backend/` 的 `AbpAdmin.db`；文件不存在会自动创建。

**全自动建表**：开发环境无需手工跑 Migrator——`HttpApi.Host` 启动时自动判断（`Database:AutoMigrateOnStartup`，默认 true）：有待应用迁移（新库 / 实体变更后）才执行迁移+种子，schema 已就绪则直接跳过。生产多实例部署可置 `false`（环境变量 `Database__AutoMigrateOnStartup=false`），改由 DbMigrator/CI 负责。

**全自动建库**：DbMigrator 启动时自动判断目标库是否存在，缺失则自动创建（SQLite 由驱动自动建文件；PostgreSQL 经维护库 `postgres` 执行 `CREATE DATABASE`）。PG 自动建库要求：① `pg_hba.conf` 放行维护库与目标库（如 `host all <user> 0.0.0.0/0 md5`）后 reload；② 登录角色具备 `CREATEDB`（`ALTER ROLE <user> CREATEDB`）。不满足时降级为警告，交由后续迁移给出原生错误。

### 3. 初始化前端

```bash
cd web
pnpm install
```

---

## 二、启动

按这个顺序：数据库已初始化 → 后端 → 前端。

### 后端

```bash
cd backend/src/AbpAdmin.HttpApi.Host
dotnet run
```

- API / Swagger：https://localhost:44395/swagger
- 健康检查：https://localhost:44395/health-status

### 前端

```bash
cd web
pnpm start
```

等价命令：`pnpm dev`。开发服务器默认 `http://localhost:8000`，`MOCK=none`，请求通过代理转到后端。前端用 **pnpm** 管理依赖（锁文件 `web/pnpm-lock.yaml`），不要再用 npm / yarn 安装。

浏览器打开 `http://localhost:8000`，用 `admin` / `1q2w3E*` 登录。

---

## 三、编译与测试

### 后端

```bash
# backend/（解决方案根目录）
dotnet build
dotnet test
```

发布示例：

```bash
dotnet publish backend/src/AbpAdmin.HttpApi.Host/AbpAdmin.HttpApi.Host.csproj -c Release -o ./publish/host
```

### 前端

```bash
cd web
pnpm build         # 产物在 web/dist
pnpm preview       # 本地预览构建结果（8000）
pnpm test          # Vitest
pnpm tsc           # 类型检查
```

生产构建不再走 `proxy.ts`。需要把前端的 API / OIDC 地址改成真实后端，见下一节。

---

## 四、配置在哪里改

### 后端

| 要改什么 | 文件 |
| --- | --- |
| 端口、CORS、前端地址、连接串、Redis、证书口令 | `backend/src/AbpAdmin.HttpApi.Host/appsettings.json` |
| 启动 URL | `backend/src/AbpAdmin.HttpApi.Host/Properties/launchSettings.json` |
| 数据库类型与连接串（Host 与 Migrator 共用） | `backend/src/AbpAdmin.HttpApi.Host/appsettings.json`（DbMigrator 分层加载自动跟随） |
| OpenIddict 客户端回调地址 | 同上文件的 `OpenIddict:Applications`（单一来源，Migrator 种子自动跟随） |
| 管理员默认密码 | `backend/src/AbpAdmin.Domain/AbpAdminConsts.cs`（改完需重新跑 Migrator 或手工改库） |

`appsettings.json` 常用项：

- `App:SelfUrl`：后端自身地址，默认 `https://localhost:44395`
- `App:SpaUrl`：前端多租户地址模板，默认 `http://{0}.localhost:8000`
- `App:CorsOrigins` / `App:RedirectAllowedUrls`：跨域与登录回跳白名单
- `Database:Provider`：`Sqlite`（默认）或 `PostgreSql`
- `ConnectionStrings:Default`：数据库连接
- `Redis:IsEnabled`：默认 `false`，本地可不启 Redis
- `AuthServer:CertificatePassPhrase`：生产证书口令

切到 PostgreSQL 时，只需改 Host 一份 `appsettings.json`（DbMigrator 分层加载自动跟随），例如：

```json
"Database": { "Provider": "PostgreSql" },
"ConnectionStrings": {
  "Default": "Host=localhost;Port=5432;Database=AbpAdmin;Username=postgres;Password=your_password"
}
```

改完后重新执行 `dotnet run`（DbMigrator）。

### 前端

| 要改什么 | 文件 |
| --- | --- |
| 开发代理目标（后端地址） | `web/config/proxy.ts` |
| OIDC 授权地址、ClientId、Scope | `web/src/abp/env.ts` |
| 路由 / 菜单 | `web/config/routes.ts` |
| 布局、标题、主题 | `web/config/defaultSettings.ts` |
| Umi 总配置 | `web/config/config.ts` |
| Cursor 前端规则 / antd MCP | `web/.cursor/rules/`、`web/.cursor/mcp.json` |

本地开发默认：

- 代理目标：`https://localhost:44395`
- OIDC `authority`：`https://localhost:44395`
- `clientId`：`AbpAdmin_App`（须与 Host `appsettings.json` 的 `OpenIddict` 节一致——种子与前端共用的单一来源）

改后端端口时，至少同步这三处：Host `launchSettings` / `appsettings`、`web/config/proxy.ts`、`web/src/abp/env.ts`。

---

## 五、数据库说明

1. 仓库忽略 `*.db`，克隆后没有库文件是正常的。
2. 建库、改表、灌种子只走 `AbpAdmin.DbMigrator`，不要指望启动 API 自动完成。
3. 本地 SQLite 文件在解决方案根目录：`backend/AbpAdmin.db`。
4. 新增实体后，在 `backend/src/AbpAdmin.EntityFrameworkCore` 生成迁移，再跑 Migrator：

```bash
dotnet ef migrations add YourMigrationName --project backend/src/AbpAdmin.EntityFrameworkCore --startup-project backend/src/AbpAdmin.DbMigrator
cd backend/src/AbpAdmin.DbMigrator
dotnet run
```

---

## 六、升级 Web 前端

当前栈已是 Ant Design Pro v6 这一代（React 19、antd 6、Umi Max 4、ProComponents 3），**不必再走 v5 → v6 大迁移**。包管理只用 **pnpm**，锁文件是 `web/pnpm-lock.yaml`，不要混用 npm / yarn。

官方参考：

- [Umi 快速上手](https://umijs.org/docs/guides/getting-started)（推荐 pnpm）
- [Ant Design：在 Umi 中使用](https://ant.design/docs/react/use-with-umi-cn)
- [Ant Design Pro 发版说明](https://github.com/ant-design/ant-design-pro/releases)
- [Pro Cheatsheet · 如何升级](https://github.com/ant-design/ant-design-pro/blob/master/docs/cheatsheet.zh-CN.md)

升级前先提交或备份当前改动。升完在 `web` 目录验证：`pnpm tsc`、`pnpm lint`、`pnpm build`，并手工测登录、菜单权限、OIDC 回调、多租户。

### A. 日常补丁（最常用）

只升依赖，不动脚手架结构：

```bash
cd web
pnpm update @umijs/max antd @ant-design/pro-components @ant-design/icons @ant-design/x
pnpm tsc
pnpm lint
pnpm build
```

- `@utoo/pack` **不要单独升**，跟 `@umijs/max` 走。
- antd 小版本一般可直接升；跨大版本看 [从 v5 到 v6](https://ant.design/docs/react/migration-v6-cn)。

### B. 对齐官方 Pro 模板

Pro 出新 tag（如 6.0.x / 6.1）时，对照 [Releases](https://github.com/ant-design/ant-design-pro/releases) 手工 diff 框架文件后合并。

> 本仓库已移除 fork 自带的 `/pro-upgrade`、`/antd` 等 Claude Code skill（前端只用 Cursor，见 `web/AGENTS.md`）。**不要执行 `npx skills add ant-design/ant-design-pro`**——它会把清理掉的 `.claude/`、`.agents/` 模板文件重新装回来。

**只合并框架文件**，例如 `package.json` 的依赖与脚本、`config/config.ts`、`tsconfig`、Biome。  
**必须保留本仓库业务**，不要被模板覆盖：

- `web/src/abp/**`
- `web/src/pages/` 下的 Identity / 租户 / 设置等自研页
- `web/config/proxy.ts` 的后端地址
- `web/src/abp/env.ts`、`web/src/access.ts`、`web/config/routes.ts` 中的 ABP 路由

### C. 大版本

| 方向 | 做法 |
| --- | --- |
| Pro 6.1 | 等正式 release，再用 B 对齐模板 |
| Umi 5 / antd 7 | 等 Pro 模板先升，再跟着升，不要自己先跳 |

v5 → v6 的官方清单（本仓库已完成，仅作对照）：`umi` → `@umijs/max`、Less → Tailwind、`from 'umi'` → `from '@umijs/max'`、`useRequest` → react-query、ESLint → Biome、moment → dayjs。见 [v6.0.0](https://github.com/ant-design/ant-design-pro/releases/tag/v6.0.0)。

---

## 生产证书

生产环境需要 `openiddict.pfx`（放在 Host 应用目录）。生成示例：

```bash
dotnet dev-certs https -v -ep openiddict.pfx -p 27417118-e03c-414b-828d-2a210646cccd
```

口令须与 `AuthServer:CertificatePassPhrase` 一致。更稳妥的做法是使用独立的签名证书和加密证书，参见 [OpenIddict 证书配置](https://documentation.openiddict.com/configuration/encryption-and-signing-credentials.html) 与 [ABP Configuring OpenIddict](https://abp.io/docs/latest/Deployment/Configuring-OpenIddict#production-environment)。

---

## 部署

部署流程与普通 ASP.NET Core + 静态前端相同。注意点见 [ABP Deployment](https://abp.io/docs/latest/Deployment/Index)。

## 更多文档

- [ABP 分层启动模板](https://abp.io/docs/latest/solution-templates/layered-web-application)
- [Web 应用教程](https://abp.io/docs/latest/tutorials/book-store/part-1)
- 本仓库维护文档：`docs/upgrade-checklist.md`（升级 ABP 版本回归清单 / 生产部署清单）
