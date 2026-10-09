# 框架契约与升级指南（源码模板）

> 本仓库以**源码模板**分发：下游 fork 源码、随业务改造。本文是契约地图——改哪些区域
> 会踩到机器保证、ABP 旁路事实有哪些、部署身份怎么配、fork 时通常改什么。
> 阅读顺序建议：先看 §1（保证测试清单）再看 §3（ABP 旁路），改造前过一遍 §5。

## 1. 机器保证测试清单（碰这些区域，先跑对应测试）

契约只存在于两类地方：测试（机器钉住）与本文（导航）。注释里的"必须有红测试兜底"
均指下表条目；改了对应区域而这些测试不红，说明改的不是你以为的地方。

| 测试 | 钉住的契约 | 破坏后果 |
|---|---|---|
| `FrontendContractSnapshotTests`（Application.Tests） | 前端消费的 DTO 形状/可空性 ↔ `web/src/abp` 手写镜像 | 契约漂移静默上线；改契约必须同提交刷新快照（`FRONTEND_CONTRACT_SNAPSHOT_UPDATE=1`）并同步镜像 |
| `HostServiceGraphValidationTests` / `HostInitializationLogTests`（HttpApi.Host.Tests） | 容器建图阶段零拨号、初始化日志清单 | 测试环境/慢网下建图卡死或误报 |
| `RedisStartupPostureTests` | 首用 fail-fast、失败冷却、`AbortOnConnectFail` 强制、关停闩锁、姿态告警纯判定（含缺省=启用口径）、非法 `Redis:IsEnabled` 启动即败、生产模板三件套（backplane/Redis/Quartz） | 多实例静默退化（进程内锁/令牌互验失败/重复触发） |
| `DataProtectionServiceRegistrationTests` | 密钥环 Redis 仓储、键名格式（env + 部署判别键）、`SetApplicationName`、证书加密出口 | 跨环境/跨部署令牌互验漏洞；密钥静默退回明文 |
| `DistributedCacheKeyShapeTests` | ABP 10.6.1 键规范化输出（`c:{Name},k:{Prefix}{key}`、租户 `t:{id},` 包裹） | 监控守卫/扫描锚定静默失配（漏键或越界） |
| `MonitoringAppServiceTests` / `CacheMonitorScanPaginationTests` | 缓存键空间硬边界（c:/t: + 隔离前缀）、SCAN 分页协议（溢出缓冲/合成游标/熔断/后置过滤）、删除守卫 | 共享 Redis 上误删/误读其它应用数据；翻页丢键 |
| `TenantRegionStoreTests`（Domain.Tests） | 租户地域归属：ExtraProperties 持久化往返、未标注回落 DefaultRegion | 方案二按租户分区搬迁时丢失归属标记（租户落错机房） |
| `.github/workflows/ci.yml` | 以上全部 + 前端 `tsc`/vitest，push/PR 必跑 | fork 后保留此文件即继承全部保证 |

## 2. 部署身份与共享 Redis 基线

- **`App:InstanceDiscriminator`**（可选，建议字符集 `[A-Za-z0-9._-]`）：同环境多套部署
  共用一台 Redis 时一处声明、三处生效——DataProtection 密钥环键名
  （`AbpAdmin:DataProtection-Keys:{env}:{判别键}`）、SignalR ChannelPrefix、缓存隔离前缀
  `DistributedCache:KeyPrefix` 的默认值（显式 KeyPrefix 优先）。独占实例/专属库保持空。
- **密钥环静态加密出口**：`DataProtection:CertificatePath` + `CertificatePassword` 配置后
  自动叠加 `ProtectKeysWithCertificate`（证书不可用启动即败）。共享 Redis 必须按敏感
  凭据存储对待（ACL/网络隔离）；明文 XML 是出厂默认，与文件系统同保护级别。
- **缓存监控硬边界**：配置隔离前缀后，键浏览/读值/删除只限含 `,k:{前缀}` 的本应用键
  （stamp/hash 全局键不在 `c:/t:` 结构内，监控不可见——既有取舍，见 §5）。
- **姿态自检**：backplane 开而 Redis 显式关、或非 Development 仍用出厂模板连接串，启动
  日志会告警（纯判定函数由 `RedisStartupPostureTests` 表驱动钉住）。

## 3. ABP 旁路清单（升级 ABP 时唯一需要重验的事实）

这些是框架绕开 ABP 官方 API 的地方，各自钉住或注明了依据的版本；ABP 大版本升级时
逐条重验，测试先红的就是真变了。

| 旁路 | 依据版本 | 机器钉住 |
|---|---|---|
| 键规范化形状（KeyPrefix 在 k: 段内、不在键首） | ABP 10.6.1 反编译 | `DistributedCacheKeyShapeTests` |
| `IServer.Info` 在部分实例被客户端 admin gate 拒 → raw `ExecuteAsync("INFO")` | 2026-09-29 实测 | 监控页 E2E + `ApplyInfoText` 白名单测试 |
| `IBatch` 只有同步 `Execute()`、无异步版 | SE.Redis 2.7.33/2.9.x 反编译 | 无（注释记录；批处理在 `HydrateKeysAsync`，单测缝短路） |
| 微软 `AddStackExchangeRedisCache` 的多路复用器不进 DI → 宿主自建四条连接各有归属（缓存/锁+DP/backplane/监控） | rel-10.6 源码核实 | `HostServiceGraphValidationTests` 按名钉住 |
| 前端代理生成器不可信（可空性丢失，abp#22798/#25176）→ 手写 `src/abp` 镜像 + 快照 | umi/ABP 官方代理均如此 | `FrontendContractSnapshotTests` |
| Schema 迁移走 EF Core 迁移：改实体 → `dotnet ef migrations add` → 提交迁移文件；启动时 `MigrateAsync` 应用（`Database:AutoMigrateOnStartup: true`，与 README 同口径） | 本仓库约定（框架 / Biz.Template / Webhooks 三套独立迁移历史） | CI 三条 `has-pending-model-changes` + 测试 Testcontainers PG 空库自举 |

## 4. 本宿主的多实例姿态（出厂与生产模板）

- 开发默认（appsettings.json）：Redis 关（缓存走内存、锁进程内、DP 密钥文件系统）——单实例够用。
  Redis 显式关闭分支会移除无人能构造的 MedallionAbpDistributedLock 瞬态注册，并把
  IAbpDistributedLock 回切为 LocalAbpDistributedLock（进程内）——Redis-off 图自洽、可独立
  启动，由 `HostServiceGraphValidationTests` 钉住。
- 生产模板（appsettings.Production.json）：`SignalR:UseRedisBackplane` + `Redis:IsEnabled`
  + `Quartz:UsePersistentStore` 三件套全开，`Redis__Configuration` 由部署环境注入。
- 本机部署状态（Redis/ES 端点与凭据）一律放 `appsettings.secrets.json`
  （入库的是公开安全的开发样本；本机真实凭据以 skip-worktree 屏蔽，
  "as it ships" 的测试形态刻意不加载它）。
- 多机房（异地灾备）超出本节单集群范围：铁律见 §7，操作手册见 [dr-runbook.md](dr-runbook.md)。

## 5. 已知取舍登记（有意为之，勿当缺陷修）

- **404 全局静默**：`requestErrorConfig` 对 404 不弹全局错误（业务语义：不存在/未配置）。
  页面层读端点存在该语义时**必须**用 `isNotFound` 分流（对照实现：`dataScope.ts`）；
  开发期有 console 痕迹。改全局策略前先盘全仓 404 语义调用点。
- **`onRealTimeMessage`/`offRealTimeMessage` 暂无生产消费者**：页面级动态订阅的框架基建，
  契约（两层注册属主、合并不清空、重建重挂）由 `signalr.test.ts` 钉住；接入首个页面
  消费者时按测试里的用法接线，不新增并行机制。
- **监控页透出原始异常文本**（ConnectionError/InfoError）：排障工具取向，接受端点/超时
  信息对持 `CacheMonitor.Default` 权限者可见；React 转义，无 XSS 面。
- **HTTP 错误通知走 `AntdApp.useApp()` 注入桥**：渲染纯度约束（effect 注入、静态回退），
  见 `requestErrorConfig.ts` 注释。

## 6. 下游 fork 改造清单（源码模板专属）

通常要改：`App:SelfUrl`/`CorsOrigins`/`RedirectAllowedUrls`、OpenIddict 客户端清单
（appsettings `OpenIddict` 节）、菜单模板（`MenuTemplateDefinition.cs`）、Logo/标题、
业务模块（`AbpAdmin.Biz.*`）；部署时设 `App:InstanceDiscriminator`（共享 Redis 场景）。

克隆时可整体剔除的模板遗留（不影响框架能力）：`web/cloudflare-worker`（antd pro 演示
mock API，前端无引用，见其目录 README）、Payment 三件套（EasyAbp
PaymentService/Prepayment/WeChatPay，无支付业务时）。

别动：§1 表中的保证测试与被它们钉住的实现；守卫（`AssertAbpKey`/`IsAllowedKey`/
`BuildScanPattern`）、键名构造（`BuildDataProtectionKeyName`）、姿态判定——改这些先改
契约测试，让红测试带你走。

## 7. 多机房部署约定（方案一铁律）

> 定位：单主多备（A 省主库 + B 省流复制备库，写流量单点），灾备不是双活。操作面
> （流复制搭建/切换/回切/备份、升级方案二路径）见 [dr-runbook.md](dr-runbook.md)；
> 本节只登记会踩代码契约的硬约定。

- **issuer 单域名**：`AuthServer:Authority` 作为 issuer 签进所有 token，双机房共用一个
  对外域名；切机房只动 DNS，永不改 issuer（拆 issuer = 另一机房验签全挂）。
- **连接串 DNS 别名**：生产 `ConnectionStrings:Default`/`Redis:Configuration` 用别名不用
  裸 IP——切库改解析、应用零改动；这也是方案二（按租户分区）路由的前置习惯。
- **密钥材料一致**：`openiddict.pfx` 与 DataProtection 证书两机房同一份（离线分发 + 备份），
  生产打开密钥环静态加密（§2 出口）。
- **租户地域归属**：ExtraProperties 键 `AbpAdminTenantConsts.RegionPropertyName`（与
  PackageId 同机制，只存租户实体——ABP 10.6 的 TenantConfiguration 无 ExtraProperties，
  归属不进解析缓存项），新建租户默认写 `DefaultRegion`，读侧
  （`TenantRegionExtensions.GetRegion`）缺省回落——存量租户零迁移。方案一只标记不路由；
  方案二据此 + 每租户连接串（`TenantConfiguration.ConnectionStrings`，解析链路已就绪）
  做归属路由（持久化往返与回落由 `TenantRegionStoreTests` 钉住）。
