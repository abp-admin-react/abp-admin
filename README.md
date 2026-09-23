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
| 后端 | .NET 10 · ABP Framework 10.6 · EF Core(Sqlite / PostgreSQL 双提供程序)· Redis · Quartz · SignalR |
| 前端 | React 19 · antd 6 · Ant Design Pro v6 · Umi Max 4 · pnpm 10 · Vitest · Biome |
| 认证 | OpenIddict(OIDC,前后端分离,SPA 独立部署) |

默认开发环境:后端 API `https://localhost:44395`,前端 `http://localhost:8000`,数据库默认 SQLite(`backend/AbpAdmin.db`,不入库)。默认管理员 `admin` / `1q2w3E*`。

## 内置能力

- ABP 官方模块:Identity、OpenIddict、多租户、审计日志、后台作业(Quartz)、设置管理、语言管理
- 权限体系:权限定义(PermissionDefinitionProvider)+ 数据范围(DataScope)+ 操作限流
- 基础设施:分布式锁与 DataProtection 密钥走 Redis(多实例就绪,fail-fast 启动)、健康检查、Serilog
- 数据库:Sqlite / PostgreSQL 双提供程序一键切换;DbMigrator 支持自动建库、分层配置加载
- 前端:登录/租户切换/菜单权限/OIDC 回调已接通,ABP 动态 API 对接封装在 `web/src/abp/`
- AI 协作规范:`.cursor/rules/` 内置 DDD 分层、应用层、授权等框架规约,人机共用一套标准

## 仓库结构

ABP 官方 Layered 模板的 **Host + 独立 SPA 变体**(以 `HttpApi.Host` 承载 API / 认证,前端独立在 `web/`,没有 `.Web` 项目);各层内部按 feature 文件夹组织(如 `AbpAdmin.Application/Identity/`),与官方 BookStore 教程一致。

```
├── backend/                              # 后端解决方案(.NET;VS 打开 backend/AbpAdmin.slnx)
│   ├── src/                              # 9 个项目
│   │   ├── AbpAdmin.Domain.Shared        # 常量 / 枚举 / 本地化资源(依赖链根)
│   │   ├── AbpAdmin.Domain               # 实体 / 领域服务 / 仓储接口
│   │   ├── AbpAdmin.Application.Contracts # 应用服务接口 + DTO + 权限定义
│   │   ├── AbpAdmin.Application          # 应用服务实现
│   │   ├── AbpAdmin.EntityFrameworkCore  # DbContext / 仓储实现 / 迁移
│   │   ├── AbpAdmin.HttpApi              # 少量定制 Controller(Auto API 为主)
│   │   ├── AbpAdmin.HttpApi.Client       # C# 动态客户端代理
│   │   ├── AbpAdmin.HttpApi.Host         # API / OIDC 宿主
│   │   └── AbpAdmin.DbMigrator           # 建库 / 迁移 / 种子数据
│   ├── test/                             # 6 个测试项目(TestBase、Domain/Application/EFCore.Tests 等)
│   └── etc/                              # nginx / quartz / 初始化脚本 / ip2region
├── web/                                  # 前端(Ant Design Pro v6 · Umi Max · pnpm)
└── docs/                                 # 设计文档与测试记录
```

---

## 快速开始

环境要求:[.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet)、[Node.js 22+](https://nodejs.org/)、[pnpm 10](https://pnpm.io/zh/installation)(`corepack enable`);可选 [ABP CLI](https://abp.io/docs/latest/cli)(`abp install-libs` 安装 LeptonX 等客户端静态库)。首次跑 HTTPS 后端建议先 `dotnet dev-certs https --trust`。

```bash
# 1) 后端(backend/ 为解决方案根目录)
cd backend
dotnet build
cd src/AbpAdmin.DbMigrator && dotnet run && cd ../..   # 建库 + 迁移 + 种子
cd src/AbpAdmin.HttpApi.Host && dotnet run             # API: https://localhost:44395/swagger

# 2) 前端
cd web
pnpm install
pnpm start                                             # http://localhost:8000
```

说明:

- 开发环境 Host 启动时会自动判断并执行待应用的迁移(`Database:AutoMigrateOnStartup`,默认 true);显式跑 DbMigrator / 生产环境(置 false)走迁移器。
- 一次性初始化可执行 `./backend/etc/scripts/initialize-solution.ps1`(编译 + install-libs + 迁移)。
- 新增迁移:`dotnet ef migrations add Xxx --project backend/src/AbpAdmin.EntityFrameworkCore --startup-project backend/src/AbpAdmin.DbMigrator`,之后再跑一次 Migrator。

### 配置单一来源

`Database:Provider` 与 `ConnectionStrings:Default` 只维护在 `backend/src/AbpAdmin.HttpApi.Host/appsettings.json`(真实凭证放同目录 `appsettings.secrets.json`,该文件不入库);DbMigrator 与 EF 设计时工具自动跟随读取。生效顺序(后者覆盖前者):

1. `HttpApi.Host/appsettings.json`(共享基座)
2. `HttpApi.Host/appsettings.secrets.json`(共享凭证)
3. `DbMigrator/appsettings.json`(迁移器专属)
4. 环境变量(最高,生产/CI 一律走此通道:`Database__Provider` / `ConnectionStrings__Default`)

切 PostgreSQL 只改 Host 一份:`"Database": { "Provider": "PostgreSql" }` + 连接串。Redis 默认关闭(`Redis:IsEnabled: false`),本地开发可不启。

### 常用配置位置

| 要改什么 | 文件 |
| --- | --- |
| 端口、CORS、连接串、Redis、OpenIddict 客户端回调 | `backend/src/AbpAdmin.HttpApi.Host/appsettings.json` |
| 后端启动 URL | `backend/src/AbpAdmin.HttpApi.Host/Properties/launchSettings.json` |
| 管理员默认密码 | `backend/src/AbpAdmin.Domain/AbpAdminConsts.cs`(改后需重跑 Migrator) |
| 前端开发代理目标 | `web/config/proxy.ts` |
| OIDC 授权地址 / ClientId / Scope | `web/src/abp/env.ts`(ClientId 须与 Host `OpenIddict` 节一致) |
| 路由 / 菜单、布局主题 | `web/config/routes.ts`、`web/config/defaultSettings.ts` |

改后端端口时至少同步三处:Host `launchSettings` / `appsettings`、`web/config/proxy.ts`、`web/src/abp/env.ts`。

---

## 新增一个业务模块(标准流程)

按 ABP 官方 BookStore 教程的约定走,几乎零配置:

1. **Domain.Shared**:枚举、常量(`XxxConsts`,含表前缀)、本地化资源 JSON
2. **Domain**:按功能建文件夹(如 `Domain/Xxx/`),实体继承 `AggregateRoot<Guid>` / `AuditedAggregateRoot<Guid>`;需要自定义查询时定义 `IXxxRepository` 接口
3. **EntityFrameworkCore**:实体映射配置(本项目集中在 `EntityFrameworkCore/Configs/`,每个实体一个配置类,DbContext 统一调用)+ `dotnet ef migrations add`
4. **Application.Contracts**:DTO(带验证特性)+ `IXxxAppService` 接口(简单 CRUD 直接继承 `ICrudAppService`)
5. **Application**:实现 `XxxAppService`(CRUD 继承 `CrudAppService`,注入 `IRepository<T, Guid>`);配置对象映射
6. **权限**:在 `Application.Contracts/Permissions/AbpAdminPermissionDefinitionProvider` 定义权限,服务方法加 `[Authorize(XxxPermissions.Xxx.Default)]`
7. **验证**:无需手写 Controller——Auto API 自动暴露为 REST 接口,Swagger 直接验证
8. **前端**:`web/src/pages/` 建页面,`web/config/routes.ts` 挂路由(菜单权限随 ABP 权限走)

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

```bash
# 后端(backend/)
dotnet build && dotnet test
dotnet publish backend/src/AbpAdmin.HttpApi.Host/AbpAdmin.HttpApi.Host.csproj -c Release -o ./publish/host

# 前端(web/)
pnpm build      # 产物 web/dist
pnpm test       # Vitest
pnpm tsc        # 类型检查
```

## 生产部署

- 生产环境需要 OpenIddict 签名证书(`openiddict.pfx`,口令对应 `AuthServer:CertificatePassPhrase`;生成方式见 [OpenIddict 证书配置](https://documentation.openiddict.com/configuration/encryption-and-signing-credentials.html))
- 连接串/证书口令等敏感配置走环境变量或 `appsettings.secrets.json`(不入库)
- 多实例部署:打开 `Redis:IsEnabled`(分布式锁与 DataProtection 密钥依赖 Redis),`Database:AutoMigrateOnStartup` 置 false,由 DbMigrator/CI 负责迁移
- 其余见 [ABP Deployment](https://abp.io/docs/latest/Deployment/Index)

## License

[MIT](LICENSE)
