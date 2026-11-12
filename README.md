# abp-admin

基于 [ABP Framework](https://abp.io)(vNext 10.6 / .NET 10)+ Ant Design Pro v6(React 19 · Umi Max)的中后台管理**模板主仓库**。

**定位**:这是我的"框架底座"仓库——只放框架层与公共能力,不带任何业务。新项目从这里派生:业务代码在各项目仓库中以独立模块工程开发;框架的修复与升级在本仓库进行,派生项目通过 git 血缘合并跟随,长期低成本同步。

| 角色 | 仓库 | 职责 |
| --- | --- | --- |
| 上游(本仓库,公开) | `github.com/abp-admin-react/abp-admin` | 框架层、公共模块、开发规范;只发通用版本,不收业务 |
| 派生项目(私有) | 各项目自己的仓库 | 业务模块、项目配置;`origin` 指向自己,`upstream` 指向本仓库 |

---

## 技术栈

| 端 | 技术 |
| --- | --- |
| 后端 | .NET 10 · ABP Framework 10.6 · EF Core(PostgreSQL 单提供程序)· Redis · Quartz · SignalR |
| 前端 | React 19 · antd 6 · Ant Design Pro v6 · Umi Max 4 · pnpm 10 · Vitest · Biome |
| 认证 | OpenIddict(OIDC,前后端分离,SPA 独立部署) |

本地开发默认使用后端 `https://localhost:44395`、前端 `http://localhost:8000` 和 PostgreSQL。只在本机访问的 Docker 体验栈初始管理员为 `admin`，默认口令为 `1q2w3E*`。

## 内置能力

- ABP 官方模块:Identity、OpenIddict、多租户、审计日志、后台作业(Quartz)、设置管理、语言管理
- 权限体系:权限定义(PermissionDefinitionProvider)+ 数据范围(DataScope)+ 操作限流
- 基础设施:分布式锁与 DataProtection 密钥走 Redis(多实例就绪,fail-fast 启动)、健康检查、Serilog
- 数据库:PostgreSQL 单提供程序,EF Core 迁移 + 存量库自动打戳;DbMigrator 支持分层配置加载(迁移机制详见 `docs/pg-migration-runbook.md`)
- 前端:登录/租户切换/菜单权限/OIDC 回调已接通,ABP 动态 API 对接封装在 `web/src/abp/`
- AI 协作规范:`.cursor/rules/` 内置 DDD 分层、应用层、授权等框架规约,人机共用一套标准

## 仓库结构

ABP 官方 Layered 模板的 **Host + 独立 SPA 变体**(以 `HttpApi.Host` 承载 API / 认证,前端独立在 `web/`,没有 `.Web` 项目);各层内部按 feature 文件夹组织(如 `AbpAdmin.Application/Identity/`),与官方 BookStore 教程一致。

```
├── backend/                              # 后端解决方案(.NET;VS 打开 backend/AbpAdmin.slnx)
│   ├── src/                              # 10 个项目
│   │   ├── AbpAdmin.Domain.Shared        # 常量 / 枚举 / 本地化资源(依赖链根)
│   │   ├── AbpAdmin.Domain               # 实体 / 领域服务 / 仓储接口
│   │   ├── AbpAdmin.Application.Contracts # 应用服务接口 + DTO + 权限定义
│   │   ├── AbpAdmin.Application          # 应用服务实现
│   │   ├── AbpAdmin.EntityFrameworkCore  # DbContext / 仓储实现 / Sql 脚本迁移
│   │   ├── AbpAdmin.Biz.Template         # 自包含业务模块样板(复制改名即新业务)
│   │   ├── AbpAdmin.HttpApi              # 少量定制 Controller(Auto API 为主)
│   │   ├── AbpAdmin.HttpApi.Client       # C# 动态客户端代理
│   │   ├── AbpAdmin.HttpApi.Host         # API / OIDC 宿主
│   │   └── AbpAdmin.DbMigrator           # 迁移 / 种子数据
│   ├── test/                             # 9 个测试项目(含随模块复制的 AbpAdmin.Biz.Template.Tests)
│   └── etc/                              # nginx / quartz / 初始化脚本 / ip2region
├── web/                                  # 前端(Ant Design Pro v6 · Umi Max · pnpm)
└── docs/                                 # 设计文档与测试记录
```

---

## 快速开始

环境要求:[.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet)、[Node.js 22+](https://nodejs.org/)、[pnpm 10](https://pnpm.io/zh/installation)(`corepack enable`);可选 [ABP CLI](https://abp.io/docs/latest/cli)(`abp install-libs` 安装 LeptonX 等客户端静态库)。首次跑 HTTPS 后端建议先 `dotnet dev-certs https --trust`。

直接运行后端前，创建本地 PostgreSQL 数据库，将宿主 `Configuration/Database.json.example` 复制为同目录的 `Database.json` 并填写连接串。也可设置环境变量 `ConnectionStrings__Default`。

```bash
# 1) 后端(backend/ 为解决方案根目录)
cd backend
dotnet build
cd framework-app/AbpAdmin.DbMigrator && dotnet run && cd ../..   # 迁移 + 种子（库需已存在，见 docs/pg-migration-runbook.md）
cd framework-app/AbpAdmin.HttpApi.Host && dotnet run             # API: https://localhost:44395/swagger

# 2) 前端
cd web
pnpm install
pnpm start                                             # http://localhost:8000
```

说明:

- 开发环境 Host 启动时会自动判断并执行待应用的迁移(`Database:AutoMigrateOnStartup`,默认 true);显式跑 DbMigrator / 生产环境(置 false)走迁移器。
- 一次性初始化可执行 `./backend/etc/scripts/initialize-solution.ps1`(编译 + install-libs + 迁移)。
- 改框架表:改 `AbpAdminDbContext` 后执行 `dotnet ef migrations add`(命令见 `docs/pg-migration-runbook.md`),再跑一次 Migrator。业务模块(样板)同法。已应用的迁移不要改。

### Docker 一键起栈(全栈,前端唯一对外)

使用 Docker 可从源码构建并启动前端、后端、PostgreSQL 16、Redis 7 和一次性数据库迁移器。默认访问地址为 [http://localhost:8000](http://localhost:8000)，API、OIDC 和 SignalR 由前端 nginx 统一代理。

首次启动时，将仓库根目录的 `.env.example` 复制为 `.env`，为
`STRING_ENCRYPTION_PASSPHRASE` 填写用于加密设置的随机长字符串，再运行以下命令。同一个数据库后续启动时继续使用原加密口令。

```bash
docker compose up -d --build        # 起栈:db → 迁移/种子 → host → web(自动等待就绪)
# 前端/登录/API: http://localhost:8000  (纯 HTTP,无证书告警)
docker compose logs -f migrator host    # 迁移种子与后端日志
docker compose down                # 停栈(数据卷保留); -v 连数据一起删
```

本机体验栈初始化完成后，直接用 `admin` / `1q2w3E*` 登录。

- 使用新数据卷时，迁移器会写入身份演示数据：部门树（演示集团）、角色（部门经理、普通员工、财务角色）和用户（`zhang.cto`、`li.dev`、`wang.fm`、`zhao.mkt`、`fin-viewer`，口令与 admin 相同）。财务账簿和业务模板示例仍默认关闭。想预填业务模板示例项目，可在首次启动前将 `BIZ_TEMPLATE_SEED_SAMPLE_DATA` 设为 `true`。
- 端口和口令通过仓库根目录的 `.env` 设置。修改端口时同步更新 `WEB_HOST_PORT` 和 `WEB_PUBLIC_URL`，后者也用于 OIDC 回调。同端口的 `localhost` 与 `127.0.0.1` 会在登录前统一到配置的访问地址。修改 `OPENIDDICT_CERT_PASSPHRASE` 后须重建 host 镜像。前端默认绑定 `127.0.0.1`。其他变量见 Compose 文件头注释。
- 对外开放时，设置自用的 `IDENTITY_ADMIN_PASSWORD`。已有数据库对外开放前，先在账号设置里更改现有管理员口令。
- Swagger 不在这套栈的对外路径里(生产姿态默认关闭且反代清单未含 `/swagger/`);确需经前端访问,按 compose 文件头 `App__EnableSwaggerInProduction` 注释同步三处配置。
- 后端跑 Production 姿态:Redis 缓存/分布式锁/DataProtection 密钥环/SignalR backplane、Quartz 持久化(`qrtz_*` 表由迁移器幂等建表)全部落在这套内联库上。
- 临时排查内网服务:`docker compose exec db psql -U postgres -d abpadmin`;宿主机 `pnpm start`(dev 模式)仍可直接对接本地后端 `https://localhost:44395`。
- 生产部署提示见文末「生产部署」;compose 栈面向本机体验。

#### 体验部门与声明类型

在本机 `.env` 里将 `SEED_IDENTITY_UI_DEMO` 设为 `true`，为 `IDENTITY_UI_DEMO_PASSWORD`
填写仅用于演示账号的强口令，再运行 `docker compose up -d --build`。

- 在「身份管理 → 部门」展开「演示·示例公司」。研发部通过统一角色让 `ou-demo-dev` 可以查看部门和成员，运营部的 `ou-demo-ops` 可用于对照。两个账号使用你设置的演示口令登录。
- 在「身份管理 → 声明类型」查看 `demo.project_code`、`demo.support_region` 和 `demo.badge_label`。在用户页打开 `ou-demo-dev` 的「更多 → 声明（高级）」，可查看示例值。这些值用于演示身份属性，功能权限仍在角色中配置。

演示开关默认关闭。关闭开关会停止写入演示数据，已创建的记录可在管理页面清理。

### 配置单一来源

`backend/framework-app/AbpAdmin.HttpApi.Host/` 集中管理基础设施配置。仓库提供 `Configuration/Database.json.example` 和 `Configuration/Security.json.example`。本地可按模板创建 `Database.json`、`Security.json`，或用宿主目录下的 `appsettings.secrets.json` 覆盖。三个本地文件均由 Git 忽略。

宿主和迁移器先读取宿主 `appsettings.json` 和 `Configuration/*.json`，再叠加各自的环境或程序配置。迁移器和 EF 设计时工具可读取宿主的本地连接串。容器和 CI 通过环境变量传入连接串 `ConnectionStrings__Default`。直接运行宿主时，Redis 默认关闭，可在 `Configuration/Cache.json` 中查看相关设置。

### 常用配置位置

| 要改什么 | 文件 |
| --- | --- |
| 服务地址、CORS、连接串、Redis、OpenIddict 客户端回调 | `backend/framework-app/AbpAdmin.HttpApi.Host/Configuration/` |
| 后端启动 URL | `backend/framework-app/AbpAdmin.HttpApi.Host/Properties/launchSettings.json` |
| 首次初始化的管理员口令 | Docker 通过 `.env` 的 `IDENTITY_ADMIN_PASSWORD` 覆盖本机默认值，直接运行迁移器使用 `Identity__AdminPassword` |
| 前端开发代理目标 | `web/config/proxy.ts` |
| OIDC 授权地址 / ClientId / Scope | `web/src/abp/env.ts`(ClientId 须与 Host `OpenIddict` 节一致) |
| 路由 / 菜单、布局主题 | `web/config/routes.ts`、`web/config/defaultSettings.ts` |

改后端端口时至少同步三处:Host `launchSettings` / `appsettings`、`web/config/proxy.ts`、`web/src/abp/env.ts`。

---

## 新增一个业务模块(标准流程)

业务代码**物理隔离**是本模板升级顺滑的前提。按业务量级二选一:

### A. 子系统级业务 → 复制 `AbpAdmin.Biz.Template` 样板模块(推荐)

`AbpAdmin.Biz.Template` 是自包含业务模块样板:实体/DTO/服务/权限/本地化/DbContext 全部在这一个工程里。建表走模块内 EF 迁移(`Migrations/` 目录,由 `BizTemplateDbSchemaMigrator` 执行,记到 `__BizTemplate_EFMigrationsHistory`,与宿主 `__EFMigrationsHistory` "两本账"互不干扰)。

新增业务三步:

1. 复制 `backend/modules/biz/AbpAdmin.Biz.Template` 这一个工程(测试工程一并复制到 `backend/test/modules/`),替换 `BizTemplate` 词根(工程名/目录/RootNamespace/常量)
2. 改表时执行 `dotnet ef migrations add`(命令同上,`--project` 指向模块工程、`--context BizTemplateDbContext`),不要改已经应用的迁移
3. 宿主接线:`AbpAdmin.HttpApi.Host` 与 `AbpAdmin.DbMigrator` 各加一行 csproj 引用 + `DependsOn` 一行

框架迁移循环(`AbpAdminDbMigrationService`)自动枚举所有 `IAbpAdminDbSchemaMigrator` 实现,模块迁移器显式注册一行即被扫到——**业务建表不产生任何框架仓库改动**。

**数据权限示例**:模块内 `BizDataScopeDemo`(实体/服务/迁移)演示业务模块如何消费框架数据权限基建——实体实现 `IHasDataScope` + 模块 DbContext 带同款数据范围筛选器,业务查询零过滤代码,写入侧校验显式归属组织必须在当前数据范围内;可运行页面在 `web/src/pages/biz-template/data-scope`(路由 `/biz-template/data-scope`,权限 `BizTemplate.DataScopeDemo`),过滤契约测试在 `backend/test/modules/biz/AbpAdmin.Biz.Template.Tests/DataScopes/`。框架侧 `/data-scope-demo` 演示页退役时,引用改指本示例。**整模块移除时的前端足迹清单**:`web/config/routes.ts`、`web/src/access.ts`(canManageBizDataScopeDemo)、`web/src/locales/{zh-CN,en-US}/menu.ts`、`web/src/pages/menus/routeRegistry.ts`、`MenuTemplateDefinition.cs` 的 `biz-template-data-scope` 节点、`web/src/abp/bizDataScopeDemo.ts` 与页面目录本身。

配置按三层落位(样板模块内三层都有带注释的实例,照着扩):

1. 基础设施配置(连接串/Redis/端口)只进宿主 `appsettings.json`——框架配置唯一来源,业务模块只读不写
2. 模块部署期配置(换环境要变、改完要重启:第三方密钥、业务开关)放模块内 `Configuration/`——强类型 Options + 模块自带 JSON,Content 复制随模块走,宿主零改动;宿主同名节/secrets/环境变量可覆盖
3. 运行期业务参数(管理员日常调)用模块自带 `SettingDefinitionProvider`——设置页可改、免重启,存储复用框架 SettingManagement,不建参数表;设置页 Tab 按 Group1 动态生成,模块自带分组不改前端。**必须 `WithProviders("G","D")` 双声明**:G 钉住 host 保存/重置的写路由(不声明会落 T 层导致值搁浅),D 保住默认值读链(不声明无存储值时读到 null)——且 reset 的层清理按声明过滤,详见样板模块注释

约定:模块内不建跨上下文外键/导航(跨模块用 Id + 应用层组合);权限 Provider 与本地化资源随模块自持,不写进框架集中文件。凭证类敏感值不进模块 JSON 基线(随源码入库),走 secrets 文件或环境变量。

### B. 小功能(几张表)→ 轻模块

直接按层追加:实体进 `Domain/Xxx/`、常量进 `Domain.Shared/Xxx/`、映射配置进 `EFCore/Configs/Xxx/`、迁移追加——全是追加式改动,merge 冲突可控。判据:预计产生 3 个以上非追加式框架文件改动,或有独立生命周期诉求(后台任务/独立配置),就用 A。

参考:[ABP 分层模板文档](https://abp.io/docs/latest/solution-templates/layered-web-application) · [BookStore 教程](https://abp.io/docs/latest/tutorials/book-store/part-1)

## 派生一个新项目

```bash
git clone https://github.com/abp-admin-react/abp-admin.git your-project
cd your-project
git remote set-url origin https://your-git/your-project.git   # 指向自己的仓库并 push
git remote add upstream https://github.com/abp-admin-react/abp-admin.git
```

日常跟随上游升级:

```bash
git fetch upstream && git merge upstream/main
```

纪律(保证升级永远顺滑):

1. 业务代码只进独立模块工程,不散装改框架层
2. 框架要改,回上游改好再 merge 下来;绝不从派生项目向上游 push
3. 上游只保留通用能力,业务特性一律留在派生项目

---

## 编译与测试

> 后端测试依赖 **Docker**(Testcontainers 自动拉起 `postgres:16` 容器,每个测试进程一个、随进程回收;镜像可用环境变量 `ABPADMIN_TEST_PG_IMAGE` 覆盖)。跑测试前确认 Docker 已安装且守护进程在运行。

```bash
# 后端(backend/)
dotnet build && dotnet test
dotnet publish backend/framework-app/AbpAdmin.HttpApi.Host/AbpAdmin.HttpApi.Host.csproj -c Release -o ./publish/host

# 前端(web/)
pnpm build      # 产物 web/dist
pnpm test       # Vitest
pnpm tsc        # 类型检查
```

## 生产部署

- 生产环境需要 OpenIddict 签名证书(`openiddict.pfx`,口令对应 `AuthServer:CertificatePassPhrase`;生成方式见 [OpenIddict 证书配置](https://documentation.openiddict.com/configuration/encryption-and-signing-credentials.html))
- 生产连接串和证书口令通过环境变量注入。本地覆盖配置可放在 Git 忽略的 `appsettings.secrets.json`。
- 多实例部署:打开 `Redis:IsEnabled`(分布式锁与 DataProtection 密钥依赖 Redis),`Database:AutoMigrateOnStartup` 置 false,由 DbMigrator/CI 负责迁移
- 多机房(异地灾备)部署:铁律见 [docs/framework-contracts.md §7](docs/framework-contracts.md),操作手册见 [docs/dr-runbook.md](docs/dr-runbook.md)
- 其余见 [ABP Deployment](https://abp.io/docs/latest/Deployment/Index)

## License

[MIT](LICENSE)
