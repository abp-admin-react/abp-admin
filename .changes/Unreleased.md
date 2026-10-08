# Unreleased(尚未随版本发布)

> 发布时把本文件内容整理为 `Migration {旧} - {新}.md` 并清空。随手累加,信息全优先。

## 模块变更

- `backend/NuGet.Config`:`<packageSources>` 增加 `<clear />`——屏蔽用户级/机器级源,包只允许来自本文件声明源(供应链加固,借鉴 abp-next-admin)。
- 新增 `.changes/` 结构化升级说明约定(本目录):发布版本时把 `Unreleased.md` 整理为 `Migration X.Y - X.Z.md`。
- `AbpAdmin.HttpApi.Host`:新增 `LogUniqueIdEnricher`——每条日志附唯一短 ID(`LogUniqueId` 属性),报障单点定位,覆盖后台作业/启动期等无请求上下文的日志。
- `AbpAdmin.Application.Contracts/Permissions`:审计日志组新增 `AbpAdmin.AuditLogs.Restore` 权限(实体变更回滚;能看变更历史 ≠ 能改写业务数据)。
- `AbpAdmin.Application/AuditLogs`:`AuditLogAppService.RestoreEntityChangeAsync`——按一条实体变更记录把 OriginalValue 写回实体(借鉴 abp-next-admin EntityRestoreAppService)。仅支持 Updated 型变更;并发戳不参与;无法还原的属性逐项跳过并说明;前端在审计日志页「实体变更历史」抽屉对 Updated 行提供「回滚」按钮(权限 `canRestoreEntityChange`)。
- 新增 `AbpAdmin.DynamicQueryable` 工程(零 ABP/NuGet 依赖的表达式树动态查询库,借鉴 abp-next-admin framework/dynamic-queryable):条件组翻译为 `Expression<Func<T,bool>>`,字段必须真实存在(fail-closed);配套纯单测工程。
- 新增 `IIdentityUserSearchAppService`(`/api/app/identity-user-search`):用户动态搜索落地——`GetAvailableFieldsAsync` 返回字段白名单元数据(名称/值类型/可用操作符),`SearchAsync` 按条件组分页搜索;字段白名单外直接拒绝,排序走既有白名单。
- `AbpAdmin.Application/AuditLogs`:OCR 修复——回滚反射从声明类型取 MethodInfo（Castle 代理子类不继承 private 方法，运行期 NRE）；跳过脱敏属性与 `[REDACTED]` 掩码值（防掩码写回凭据字段）；实体类型收敛到 `AuditLoggingEntityTypes.ChangeHistoryEnabled` 白名单；拒绝路径带错误码（`AbpAdminDomainErrorCodes.EntityRestore.*`，测试按 Code 断言）。
- `AbpAdmin.Application/PermissionManagement`:OCR 修复——六个写方法补 `[OperationLog]` 与 `EnsureHostSide()`；静态定义镜像记录禁止删除；重名/遮蔽检查重排（先动态重名精确归因）；`GetDefinitionsAsync` 分页下推数据库。
- `AbpAdmin.Application/Identity`:OCR 修复——动态搜索校验每字段操作符集（非法组合转 400）；表达式构建异常转业务错；计数在无序查询上执行；`IdentityUserSearchItemDto.Email/PhoneNumber` 补 `[Masked]` 脱敏（与 IdentityUserDto 同口径）。
- `AbpAdmin.DynamicQueryable`:清理——删除零引用 `DynamicConditionGroup`；`BuildPredicate` 收为 private 并对空条件 fail-closed。
- `web/src/pages/permission-definitions`:OCR 修复——列头筛选改用第三参 filter（此前静默无效）；编辑预填原始 displayName（防本地化串被覆盖成明文）；请求体显式 PascalCase；更新/删除路由修正为 `/{id}/group`、`/{id}/definition`（此前 405）；提交失败保持弹窗打开。
- `web/src/pages/audit-logs`:回滚按钮加进行中防重；抽屉按 entityId remount（防串实体旧数据）。
- (并行进行中的迁移机制重构——EF 迁移替代内嵌 Sql 脚本、PostgreSQL 单提供程序——由另一会话负责,此处不记录其条目,以该会话自己的说明为准。)
- **角色管理吸收包(借鉴 Admin.NET + ABP 上游机制)**:
  - 角色重命名级联:新增 `RoleRenamedCascadeHandler` 订阅 `IdentityRoleNameChangedEto`(与上游 PermissionManagement 的 `RoleUpdateEventHandler` 同构),同步改写 `MenuGrant.ProviderKey` 与 `RoleDataScope.RoleName`——偿还两表以角色名为键、重命名即静默失配的已登记技术债。
  - 数据范围防越权:`RoleDataScopeAppService` Create/Update 增加 `EnsureOperatorCanGrantAsync`——授「全部数据」要求操作者自身快照 IsAll、自定义 OU 须逐个落在操作者授权范围内;`CurrentDataScopeProvider` 对 `admin` 角色直接返回 IsAll(出厂不为 admin 配 RoleDataScope 行,不豁免则任何 IHasDataScope 实体对 admin 零行可见)。
  - 删除保护:`AbpAdminRoleAppService` 按 ABP 服务替换模式顶替 `IdentityRoleAppService`(`/api/identity/roles` 路由不变),角色下仍有用户时拒绝删除(原生删除会静默清掉用户-角色关联)。
  - 角色下用户只读查看:新增 `GET /api/app/role-user-admin?roleId=` + 前端角色页「用户」抽屉;`RoleDataScopeAppService` 三个写入口补 `[OperationLog]`,新增 `role(id)` 日志解析函数。
  - 前端配套:`DataScopeModal` 未配置语义如实展示(fail-closed 零行可见)且仅 404 走默认表单、其它读取失败关弹窗防默认值覆盖;`RoleGrantModal` 补「授权按节点生效」说明。
  - 新错误码:`AbpAdmin:Identity:RoleHasUsers`、`AbpAdmin:DataScope:RoleDataScopeEscalation`、`AbpAdmin:DataScope:RoleDataScopeCustomOuOutOfScope`(zh-Hans/en 已配)。
- `web/src/pages/menus`:RoleGrantModal 的 TreeSelect 节点 key 与 value 统一为角色名,消除 antd 控制台警告(勾选语义本就是角色名,角色名租户内唯一)。
- `AbpAdmin.Application/AbpAdminRoleAppService`:删除角色操作日志的 IHttpContextAccessor/ICorrelationIdProvider 恢复构造函数注入(测试基座 AbpAdminApplicationTestModule 已补 AddHttpContextAccessor 注册)。
- **菜单-角色授权双视角 + 树交互基建包(借鉴 Admin.NET/芋道,全局交互升级)**:
  - P0 角色侧「菜单权限」入口:角色页新增链接 + 弹窗(角色→菜单树勾选,Admin.NET/芋道同款交互方向)。新增 `GET/PUT /api/app/menu/role-menu-grants/{roleId}`——视图平铺全量菜单 + IsGranted(该角色已授权) + IsControlled(受任一角色控制);保存为差集更新(勾上=插入、勾掉=撤销,只动该角色,未受控菜单与其它角色不受影响)。语义适配本系统混合授权模型:节点徽标区分「公开」(不受控,勾不勾不影响可见)与「受限」(仅授权角色可见),说明文案钉住三态;勾选为逐节点精确集(与可见性算法同语义,无父子推导)。幽灵菜单 id 整单拒绝(MenuNotFound+Count);读取失败自动关弹窗防空集覆盖。测试 +3 例(视图标记/差集只动目标角色/幽灵拒绝),MenuAppServiceTests 20/20。
  - P1 共享 `TreePanel` 组件:全选/清空/展开折叠工具条 + 定高滚动(勾选状态调用方受控、展开内部管理),接入角色菜单权限弹窗、权限弹窗(PermissionModal)、数据范围 OU 树。
  - P1 数据范围 OU 树新增「父子联动」开关(芋道同款):默认不联动=精确集(勾哪个是哪个),打开后勾父自动勾子。
  - P2 菜单表单类型驱动收敛:路由地址字段仅「菜单(页面)」类型渲染(目录隐藏防误填),提交时目录归一清空残留 path;菜单列表工具栏新增「展开全部/折叠全部」总开关。
- 出站 HTTP(HttpAgent):ProfilerDelegatingHandler 报文透视经 `ConfigureHttpClientDefaults` 覆盖【全部】出站客户端(AuthServerTokenExchange / Turnstile / webhook 投递 / 短信 requester 及后续新增消费方,零接线),配置键 `HttpRemote:Profiler`(默认 false——报文含 client_secret/token/签名,仅本地联调临时开启);RecordingHttpHandler 测试替身补 Response.RequestMessage 关联(附加 handler 读报文元数据不再 ArgumentNullException)。
- **删除 ClickHouse 模块**(`AbpAdmin.ClickHouse` 整工程 + Host 引用/健康检查/appsettings 配置(含 secrets 的 ClickHouse 与死配置 MarketData 段)/Storage.Tests 用例):批量事件写入管道全仓库零生产者零消费者(无人调 `IClickHouseEventWriter.Enqueue`),web/DbMigrator/EF 均无感知。日志体系归位 ABP 正统形态——运行日志 Serilog(文件/控制台/可选 ES data stream sink),业务日志落主库;与 ruoyi-vue-pro(ES 完全不集成)/Admin.NET(ES 开关式可选 sink)/ABP 官方(框架不实现日志基础设施,sink 自理)三方共识一致:ES 是可配置可不配置的附加扩展,不是框架级依赖,`AbpAdmin.Elasticsearch` 模块保持现状。
- 新增日志保留期清理(借鉴 Admin.NET LogJob / ruoyi-vue-pro 日志清理 Job——两家都有、基座此前缺):`AbpAdmin.Domain/Logging` 三件套——`LogRetentionOptions` + `LogRetentionCleaner`(Id 升序取最旧一批、当批内删、每批独立 UoW(requiresNew),避免 `DeleteAsync(谓词)` 整表物化与单长事务锁表;批内「查询即删除」保证循环必然终止;截止时间走 IClock 防本地/UTC 混写;审计子表 AbpAuditLogActions/AbpEntityChanges 由库级 FK Cascade 随删)+ `LogRetentionBackgroundWorker`(Host 注册,24h 周期,双 RetentionDays 零时空转)。缺省/0 = 永不清理(缺省安全:不出厂自动删日志;出厂 appsettings 给 90 作推荐值)。覆盖两张此前无限增长的表:AppOperationLogs 与 AbpAuditLogs。配套三契约测试(0=跳过/到界删除保留近期/行数>BatchSize 分批清空),EFCore.Tests 574/574。
- **存量库自举打戳(`EfCoreLegacySchemaBaseliner`,框架 + BizTemplate 两个迁移器接入)**:迁移重构后,脚本时代建的库「业务表在而 `__EFMigrationsHistory` 缺失」会触发 MigrateAsync 重放 Initial 撞 42P07「relation already exists」→ 宿主起不来(实测 abp_test2 先后命中框架与 BizTemplate 两处)。修复模式与 nopCommerce(MigrationVersionInfo)/Umbraco(umbracoMigration) 及微软「存量库接入迁移须记账不重放」指引同构:迁移前探测三态(History 已记 Initial → 正常;哨兵表不存在 → 全新库正常建全部;两者兼具 → 建 History 并把 Initial 记账为已应用),此后只应用真增量。三态决策抽为纯函数 `Decide`,决策表四象限单测钉死(含「全新库不误戳」陷阱);PostgreSQL 专用(to_regclass 探针)。哨兵必须用各自 DbContext 的自有表(框架=AbpUsers、模板=BizProjects)——用跨模块哨兵会在全新库上把后跑模块的 Initial 误戳掉。打戳打 Warning 提示运维一次性 diff 确认无漂移。实测:存量库带 `Database:AutoMigrateOnStartup`(默认开)直接启动成功;二次重启零打戳告警(状态机收敛),`SkipAlreadyRecorded` 幂等路径生效。
- **限流分区键有界化(内存 DoS 防护)**:`OperationRateLimitingPartitionKeys.Bound`(截断 256,对齐 ABP 用户名上限)+ Email/Parameter/PhoneNumber 三个用户输入解析器接入。登录输入未经验证直通分区键,实测 10000 字符用户名生成 10KB Redis 键;微软限流文档点名该反模式("partitioning on unbounded user-controlled input can exhaust memory"),Envoy Gateway/nginx 同款警告。截断方向安全:合法输入 ≤256 永不触碰,攻击超长输入合并同桶自锁。配套四契约测试(超长截断/恰界原样/大写化+截断/归一化+截断)。
- **OCR 六镜头评审修复( open-code-review: function/design/security/performance/refactor/test 六路并行,35 文件全覆盖)**:
  - HIGH 预检阻断全新库自建:`PostgresStartupPreflight` 探测目标库抛 3D000(库不存在)被通用 catch 折叠成「连不上」,导致全新服务器上启动即失败且误导排障。修复:3D000 单独分支给出「服务器可达但库不存在 + CREATE DATABASE 建库语句」的明确指引(后续自动建库能力整体移除,见下条);掩码展示串防御性构建(连接串本身非法时不再让原始 ArgumentException 逃逸出 catch)。
  - 自动建库能力移除(评审后决策):`EnsureHostDatabaseExistsAsync`(经维护库 postgres CREATE DATABASE)删除——建库是部署侧一次性动作,系统只负责自动建表(迁移);目标库不存在时预检给出建库语句指引。README/runbook 同步(「DbMigrator 自动建库」表述删除)。
  - HIGH 保留期清理对租户行失效:`OperationLog`/`AuditLog` 均为 IMultiTenant,ABP 过滤器在宿主侧只放行 TenantId IS NULL——租户名下过期日志永远清不到(声明「全租户清理」静默落空,审计合规风险+磁盘无限增长)。修复:`IDataFilter.Disable<IMultiTenant>()` 包裹批删循环(与 ScheduledJobScheduler/ImpersonationManager 惯例一致)+ 租户行契约测试(旧测试全插 tenantId=null 行,假绿)。
  - HIGH 打戳器 schema 硬编码 public:探针/DDL 全部 `public."…"` 限定,而 EF 按 search_path 记账——runbook 自己推荐的 `SearchPath=abp_admin_efm` 隔离部署下会打出无人读的假账,PG15+ 无 public CREATE 权限则 42501 崩溃。修复:表名去 schema 限定,与 EF 同走 search_path(探针看到的=EF 要用的那本账)。
  - HIGH Initial 迁移 ID 漂移无守卫:两处硬编码 `"2026…_Initial"` 若迁移重建/改名即静默打假账→42P07 回归且只影响存量库。修复:ID 提为常量(`EfCoreLegacySchemaBaseliner.FrameworkInitialMigrationId`/`BizTemplateConsts.InitialMigrationId`)+ 漂移钉死测试(框架从 GetMigrations() 读真实清单,模板反射读 [Migration] 特性,各断言常量=事实)。
  - MEDIUM:`BatchSize` 负值会变非法 LIMIT 抛错(文档语义≤0 空转)→清理器钳制 `Math.Max(0,…)`+负值空转测试;`BatchSize` 此前不可从配置绑定→新增 `LogRetention:BatchSize` 键;限流键长钳制收口到 Checker.BuildCacheKeyAsync 最后一道(自定义解析器绕过内置 Bound 也有底),解析器接口契约注释更正(null→ClientIp 回退,非全局共享计数)。
  - LOW:打戳器改收 DbConnection(提供程序守卫归位,不再被调用方强转打死);CREATE TABLE 42P07 并发容忍;宿主/DbMigrator 陈旧注释修正(SQL 脚本工作流/SQLite UoW 残句/驱动报错说明);Biz.Template 死 glob(`Sql\**\*.sql`)+ 死 DDL 指针清理;保留期查询贯穿 CancellationToken;审计分支分批循环/257 边界/占位标记真阳性各补测试。
  - 评审明示接受(不改):保留期零命中轮的全索引扫(删热表加执行时间索引是净亏,行为已注释);迁移期双探针(每库 +1 连接,毫秒级);占位标记真阳性拒绝(方向安全)。
- **PostgreSQL 启动预检(`PostgresStartupPreflight`)**:第一次运行没配置/占位串未改/PG 连不上时,宿主与 DbMigrator 在迁移前 `LogCritical` + 抛 `AbpInitializationException` 拒绝启动,错误信息含掩码目标(Host/Port/Database/User,密码永不回显)、「排查清单」与「两种配置方式」指引(不再让开发者读深层 Npgsql 堆栈)。占位判定=空串或含 `CHANGE_ME` 标记(出厂兜底串的密码段,大小写不敏感);探测强制 5 秒超时快速失败。接线:框架迁移器顶部(覆盖宿主自动迁移/DbMigrator/租户循环各自连接)+ 宿主模块迁移门控之前(`AutoMigrateOnStartup=false` 也拦)。占位判定四用例单测;实战双向验收(不可达端口/占位串各自给出对应错误,正常配置零干扰)。
- **SQLite/ClickHouse 终扫清零(运行时)**:移除已废弃的 `Database:Provider` 键;`ConnectionStrings:Default` 的 SQLite 兜底串改为 PG 占位串(fail-fast,不再静默建库文件);删 Application 的 `Microsoft.Data.Sqlite` 包与 TenantAppService 死 using、EFCore 工程的 `Volo.Abp.EntityFrameworkCore.Sqlite` 引用(两个测试工程各自显式引用,不受影响)。README 五处过时描述(双提供程序/Sql 脚本工作流/BizTemplate 旧机制)与 Domain.Shared/FileManagementFileConfig/slnx/HostModule/DbMigrator/ITenantDatabaseCreator 陈旧注释统一改为 EF 迁移口径;runbook §1 的存量库两条手动路替换为自动打戳说明、§4 兜底串遗留项关闭。终扫:源码/文档/测试零 ClickHouse;运行时(src)零 SQLite。SQLite 仅存于测试基座内存库(runbook §3 的既定设计)与 `.changes` 历史记录。

## 依赖项变更

| 库 | 原版本 | 现版本 |
| --- | --- | --- |
| (无主动调整) | | |

## 数据库迁移

| 项目 | 迁移/脚本 | 说明 |
| --- | --- | --- |
| (本批无新增;迁移机制重构的迁移条目见该重构自己的说明) | | |

## 配置变更

| 键 | 变更 | 默认值 | 必填 |
| --- | --- | --- | --- |
| `Serilog:WriteTo[File].Args.configure[0].Args.outputTemplate` | 变更 | File sink 默认模板 + 行尾 ` {LogUniqueId}` | 否 |
| `Serilog:WriteTo[Console].Args.configure[0].outputTemplate` | 新增 | `[HH:mm:ss Level] Message LogUniqueId` | 否 |
| `OperationLogs:RetentionDays` | 新增 | 90(缺省/0 = 永不清理) | 否 |
| `Auditing:RetentionDays` | 新增 | 90(缺省/0 = 永不清理) | 否 |
| `ClickHouse:*`(appsettings.json / appsettings.secrets.json) | 移除 | — | — |
- 新增 HTTP 探活定时作业(JobType=`AbpAdmin.HttpProbe`,Host/租户均可配):`Application/Monitoring/HttpProbeJobHandler` 对 Payload 指定 Url 发一次 GET(可选 `TimeoutSeconds`≤60 / `ExpectedStatusCode`),成败落定时作业执行记录;失败先发 `HttpProbeFailedEto`(ILocalEventBus,`onUnitOfWorkComplete:false` 立即分发——外层调度 UoW 随后必然回滚,缓冲发布会让失败通知静默丢失)再抛出。Payload 缺失/非法 JSON/非法 URL 快速失败且不发请求。命名客户端 `AbpAdminHttpProbe` 注册跟随消费方(Application 模块)。补监控缺口:此前 Monitoring 仅进程/Redis 本地监控,无 URL 探活。
- Webhooks 双升级:(1) 首个业务事件源——新增 `HttpProbeFailedWebhookEventHandler` 订阅探活失败事件桥接 `IWebhookPublisher`(订阅方按 `AbpAdmin.HttpProbe.Failed` 建订阅;桥接器自开 requiresNew UoW 落 SendRecord,不被探活异常引发的调度 UoW 回滚波及),发布管道自建成以来首次接线;(2) `WebhookDeliveryJob` 手写 do/while 重试迁 HttpAgent 重试策略——间隔 2s/4s 不变,仅对网络异常与瞬态状态码(408/429/500/502/503/504)重试,4xx 不再无效重发;尝试次数经 OnRetry 回调跟踪,Warning(重试中)/Error(终态,非 2xx 与网络层两种形态)日志语义保持;正文用 `SetContent(payload,"application/json")` 原文发送(`SetRawStringContent` 会包一层引号,破坏 HMAC 签名与收端解析)。
