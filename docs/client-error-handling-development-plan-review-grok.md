# 客户端错误处理开发方案审核报告（Grok）

| 项 | 内容 |
| --- | --- |
| 审核对象 | `docs/client-error-handling-development-plan.md`（版本 1.0，2026-11-05） |
| 对照材料 | `canvases/client-error-policy.canvas.tsx`；仓库内错误出口、语言资源接线、前端请求层 |
| 审核日期 | 2026-11-05 |
| 结论 | **有条件通过。** D1–D6 不重开。方向、分层和验收意识足够指导实现；按当前文字直接开工，会在管理端登记范围、管理端文案插值和关联号位置三处做出与正文目标不一致的实现。 |
| 建议 | 先把第 4 节四条门禁补进方案正文，再按 P0 开发。本文不授权改代码。 |

本文是独立审核，不替代原方案。产品取舍以方案第 1.2 节已确认决定为准。下面的意见都是执行缺口、源码核对和交接风险。

## 1. 总评

方案把 exchange 的教训写对了：公开资格和翻译资源分开，过滤发生在服务端，前端只做归一和展示。扩展点也选对了。`IExceptionToErrorInfoConverter` 同时服务 HTTP 和审计，方案禁止全局替换它；`FriendlyBusinessExceptionMiddleware` 自己组 ABP 信封并回传 `business.Data`；`AbpAdminDomainModule` 给全部资源挂数据库贡献者；匿名配置裁剪只动 settings。这几条与当前源码一致，说明方案不是凭空设计。

主要风险不在“客户会不会看到 SQL”，而在过滤器替换的当天，现有管理端会先被同一套默认策略打到。方案 4.1 规定未分类端点运行时默认 Public。画布却写“不是把全部现有 API 一次改成严格模式”。正文的发布顺序（先登记管理端，再开放客户入口）是对的，完成标准却把登记范围写成“当前框架 API”。平台模块的管理接口若不算进这句，上线后会静默丢掉现有业务文案。

另一处会直接做错的是关联号。方案要求无效关联号换新值，并且响应、日志、审计使用同一个值。现有 `UseCorrelationId()` 在 `UseFriendlyBusinessException()` 之外，审计在更内层。只在异常投影时另造新值，三处必然不一致。

## 2. 审核方法

对照阅读方案全文和画布，再核对下列实现，不把方案里的“已核实”直接当成事实：

- `FriendlyBusinessExceptionMiddleware` 的转译条件和响应体
- `AbpAdminHttpApiHostModule` 的异常页、关联号、租户失配手写响应顺序
- `AbpAdminDomainModule` 对本地化资源一视同仁追加 `DbLocalizationResourceContributor`
- `AbpAdmin.Domain.Shared.csproj` 的 `EmbeddedResource` 范围
- 上传防线、会话校验、Token 限流、用户/审计导出的手写响应形状
- `HttpApi` 对 `Domain` 的现有项目引用
- `web/src/abp/identity.ts` 用 `window.open` 打同步导出
- `docs/framework-contracts.md` 对 404、监控诊断 DTO、前端快照范围的既有约定
- `UserFriendlyException` / `BusinessException` 的分布，用来估计管理端回归面

未重新执行方案第 13 节引用的 21 项与 22 项既有测试，未访问生产，未改业务代码。

## 3. 分角度结论

| 角度 | 判断 | 说明 |
| --- | --- | --- |
| 安全目标 | 成立 | 客户原文、内部码、未批准参数、资源目录、成功 DTO 里的失败字段都有对应规则。缺的是管理路径的插值纪律，以及关联号的管道位置。 |
| 架构分层 | 可落地 | 契约在 Domain.Shared、目录在 Domain、HTTP 适配在 HttpApi/Host，与“框架不引用业务模块”一致。本仓库 `HttpApi` 已经引用 `Domain`，投影核心放 Domain 能被过滤器调用。 |
| ABP 扩展点 | 成立 | 替换异常过滤器、单独适配授权中间件、不叠加第二套 `UseAbpExceptionHandling`、不重写 private 包装方法。这些约束写进方案是必要的。 |
| 管理兼容 | 目标明确，门禁不够 | D3 要求留下安全业务提示。未分类默认 Public 会先作用于全部现有管理接口。登记完成标准必须覆盖平台模块，不能只覆盖 framework-app。 |
| 出口完整性 | 主路径已点名，返回类型未写清 | 方案 5.3 的文件列表是对的。其中导出接口的失败是普通 `BadRequest`，上传防线的 `reason` 在 JSON 里，异常过滤器都碰不到。 |
| 多语言 | 成立 | 独立资源、跳过数据库贡献者、拒绝动态覆盖、配置/脚本固定公开视图、嵌入资源进 csproj。这是本仓库已经踩过的坑（SettingProperties 曾经漏嵌入）。 |
| 前端 | 首期边界清楚 | 独立 fetch、不改 `.umi`、不恢复 OpenAPI 生成器、快照只含机器契约。正式跨源 H5 的 API 源白名单还要写明，避免和“禁止任意绝对 URL”缠在一起。 |
| 测试 | 方向对，有两处会测错地方 | 要求真实 HTTP 管线，并承认 `HostTestBed` 不启动服务器。快照应放 Host 测试，沿用现有快照会漏掉模块公开码。关联号与 Development 异常页要进矩阵。 |
| 发布与回退 | 成立 | 禁止用原文透传做应急，示例开关默认关闭且生产启动拒绝。回退单位是同一套已验证版本。 |
| 文档一致性 | 有漂移 | 画布有三处和正文冲突，执行时以正文为准，并改画布，避免实施者只读画布。 |

## 4. 开工前补进方案的门禁

这四条不改变 D1–D6。它们把正文里已经有的原则写成可执行的完成条件。

### 4.1 过滤器替换的登记门禁要覆盖全部现有管理端点

方案 4.1：未分类端点运行时默认 Public，契约测试要求“当前框架 API”有明确分类。方案 12.1：先完成管理登记，再开放客户入口。画布实施页：不是把全部现有 API 一次改成严格模式。

这三句不能同时按字面执行。`AbpExceptionFilter` 是全局的，`FriendlyBusinessExceptionMiddleware` 也包住全部 API。替换或扩展它们的那一次提交，就会对未登记端点返回公开兜底文案。

`BusinessException` 分布在 framework-app，以及 Collection、Ledger、Ordering、Webhooks、TokenGateway。`UserFriendlyException` 另有约 49 处，分布在 11 个生产文件；`IdentityUserAdminAppService` 12 处，`AuditLogAppService` 11 处，`PermissionDefinitionManagementAppService` 8 处。这些文案今天由 ABP 默认转换器送到管理端。只要对应端点未登记，D3 所说的“保留安全业务提示”在发布当天就不成立。

补进 P0 完成条件：

- 登记范围是宿主最终装配里的全部现有 HTTP 端点，包含平台模块和 Biz.Template 的管理入口。
- 替换异常过滤器或扩展友好异常中间件的提交，必须带有同一份登记表。每个端点是 `AdminCompatibility`、`Shared` 或 `Public` 之一。
- 选择 `Public` 的现有管理端点，要有一份书面清单，写明哪些现网文案改为通用提示，并作为管理端兼容说明的一部分。
- “框架 API”只作为 framework-app 的子集，不能当作门禁口径。

安全上默认 Public 是对的。要补的是发布门禁，不是改回“已登录即管理端”。

### 4.2 管理端也要先过滤参数再插值，并盘点无码友好异常

方案 3.4 对客户路径写得很死：禁止先用全部 `Exception.Data` 生成文案，再从响应里删字段。方案 4.3 对管理端写的是保留已审核的业务码和本地化含义，无码且带 `ex.Message` 的友好异常不自动放行。

管理路径如果继续调用 ABP 默认本地化，`WithData` 的值仍会进 `message`。连接信息、路径、用户输入只要进过 `Data`，管理端响应里就还有。D3 保留的是业务语义，方案 4.3 同时要求不返回原始系统诊断。两条要同时成立，管理端插值就必须使用同一套允许参数，只是资源可以仍走管理端资源，公开码也可以仍是内部业务码。

无码 `UserFriendlyException` 的文案今天就是用户看到的那句。不先盘点，实施者无法判断该登记保留，还是改为通用提示加关联号。P0 增加一件具体产出：上述约 49 处逐条标记“登记保留”或“改为通用提示”。没有这份表，A17 无法判断失败是回归还是预期收紧。

### 4.3 关联号校验放在现有 CorrelationId 中间件，不放在投影器

当前管道顺序（`AbpAdminHttpApiHostModule`）：

1. Development 下 `UseDeveloperExceptionPage()`
2. `UseCorrelationId()`
3. 非 Development 下 `UseErrorPage()`
4. `UseFriendlyBusinessException()`
5. 更后面才是审计

方案 3.1 要求无效关联号换新值，并保证响应、日志、审计一致。方案 5.3 又要求失败发生前关联号已经可用。投影阶段发现非法头再生成新值，审计和日志里仍是中间件早已记下的原值。

补进设计：在现有关联号中间件的位置做字符集和长度校验；非法则在进入路由、业务、审计之前换成新值。投影器只读取这个已经稳定的值，不另造第二个。响应头、响应体 `data.correlationId`、日志作用域和审计字段使用这一份。

### 4.4 宽松配置键要读原始配置节，不能只校验 Options 对象

方案 5.4 要求出现 `ShowAllErrors`、`ExposeDetails`、`AllowRawMessage` 这类键时启动失败。`IConfiguration` 绑定到 POCO 时，未声明的键会被丢掉，绑定结果看起来合法。

校验放在 Host 的配置绑定处，读取 `ClientErrorHandling` 原始子键，与允许键集合比较。Domain 里的 `PublicErrorOptionsValidator` 继续负责文化、长度和上限。两处职责分开写，避免实施者只给 Options 做范围检查，就认为 5.4 已经完成。

## 5. 应写入方案的源码核对

这些条目方案已经点到文件名。补上返回形状后，实施者才不会只改异常过滤器。

### 5.1 友好异常中间件今天回传内部码和 Data

`FriendlyBusinessExceptionMiddleware` 只接管带码 `BusinessException`，默认 HTTP 403，响应体使用 `business.Code`、`AbpAdminResource` 按该码本地化的文案，以及完整的 `business.Data`。无码异常、非业务异常、响应已开始，都原样抛回。Development 下外层是 `UseDeveloperExceptionPage()`。

方案要扩展这条中间件，并让 API 在 Development 也不走开发异常页。这与源码一致。验收矩阵建议增加一条明确用例：宿主环境为 Development 时，受控 API 的未知异常响应体里没有开发异常页 HTML、堆栈和源码路径。方案 12.1 提了这件事，A01–A20 没有单独编号。

### 5.2 手写失败有三条不经过异常过滤器

异常过滤器只看抛出的异常。下面三处是直接写响应或直接 `return`：

| 位置 | 今天的形状 | 实施含义 |
| --- | --- | --- |
| `FileUploadContentGuardMiddleware.RejectAsync` | ABP 信封，`data` 含 `fileName` 与 `reason` | `reason` 要按公开参数登记。文件名是用户输入，是否回传单独决定。只改异常过滤器不会碰到这里。 |
| `UserExportController.ExportAsync` | `BadRequest(new { error = "结果集 {count} 条…" })`，`error` 是字符串 | 前端 `exportUsers` 使用 `window.open`，失败页就是这段 JSON。改成统一信封会直接改变浏览器里看到的页面。 |
| `AuditLogExportController.ExportAsync` | `BadRequest(new { error = 本地化字符串 })` | 同样不是异常路径。限流异常才走异常过滤器。 |

会话撤销（`session_revoked` / `error_description`）和租户失配（`tenant_mismatch`）也是手写 JSON，方案已把对应文件列入 5.3。P0 清单应按“响应是怎么写出去的”登记，并写明客户可见时收敛到哪一个公开码。租户失配说明“令牌租户与解析租户不一致”属于内部策略细节，公开文案用权限或登录类通用句即可。

`ImageProcessingBusyExceptionFilter` 不标记异常已处理，只补 `Retry-After`，响应体仍交给后面的异常过滤器。替换 `AbpExceptionFilter` 时要保留这个顺序，否则 503 的等待时间只剩响应体、丢掉响应头。

### 5.3 SignalR 与 200 响应里的诊断字段是残留面，要写进非目标

现有 `NotificationHub`、`ChainStatusHub`（`/signalr-hubs/chain-status`）不在方案 5.3 的逐文件清单里。Hub 本身几乎没有客户端可调用的业务方法，链状态是服务端推送。REST 过滤器不会改这些推送的 payload。方案 12.1 已说明新的客户 Hub 要单独接入。建议在非目标里写上：本轮不改造现有 Hub 推送内容；租户失配中间件对 `/signalr-hubs/` 的 JSON 拒绝仍然要接入统一写口。

定时任务、审计详情、监控页、用户导入行的 `errorMessage` 走 HTTP 200。`docs/framework-contracts.md` 已接受持有 `CacheMonitor.Default` 的人看到连接/超时类诊断。方案 3.3 和 4.3 把这些管理 DTO 留在本轮之外，这个边界是对的。登记时这些查询必须保持 `AdminCompatibility`，不能标成 `Shared`。

### 5.4 嵌入资源与数据库贡献者的设计可以按方案做

`AbpAdmin.Domain.Shared.csproj` 只嵌入 `Localization\AbpAdmin\*.json` 和 `SettingProperties\*.json`。新的客户资源目录不改 csproj，发布包里就没有文案。同一 csproj 的注释记录过 SettingProperties 漏嵌入后元数据静默失效。方案 2.1 第 9 条是有仓库先例的。

`AbpAdminDomainModule` 在初始化时对 `localizationOptions.Resources.Values` 全部 `Contributors.Add`。只新建资源类、不在这里跳过，在线语言覆盖仍会改客户响应。方案要求跳过静态客户资源，并让 `LanguageTextAppService.UpdateAsync` 拒绝写入，同时保留 `RestoreToDefaultAsync`。这与“文案随版本发布”一致。

### 5.5 分层放在 Domain 与本仓库的项目引用匹配

官方分层模板里 HttpApi 通常只引用 Application.Contracts。本仓库 `AbpAdmin.HttpApi.csproj` 同时引用 `AbpAdmin.Domain`。方案把无 HTTP 的投影核心放在 Domain、过滤器放在 HttpApi，在这个仓库里能编译，也不必让 HttpApi 去引用 Application 实现。

业务模块通过 EntityFrameworkCore 间接引用 Domain。公开定义的 Provider 接口放在 Domain.Shared，模块可以声明自己的映射，框架工程不必反向引用 Collection 或 Ledger。这个方向保持不变。

## 6. 测试与契约

### 6.1 公开码快照不要放进现有的 FrontendContractSnapshotTests

`FrontendContractSnapshotTests` 在 `AbpAdmin.Application.Tests`，用环境变量 `FRONTEND_CONTRACT_SNAPSHOT_UPDATE=1`，并且只种子化一部分 Contracts DTO。`docs/framework-contracts.md` 写明：Application.Tests 不引用业务模块，模块 DTO 不进这套快照。

方案把 `PublicErrorContractSnapshotTests` 放在 `AbpAdmin.HttpApi.Host.Tests`，用另一个变量 `UPDATE_PUBLIC_ERROR_CONTRACT=1`，并由宿主组合结果对比 `web/src/abp/customer/public-error-catalog.json`。这是对的。方案里应加一句：这是第二套快照，不并入 `frontend-contract-shapes.json`；模块测试只锁各自定义，对前端文件的一致性只由宿主组合测试负责。否则模块测试各写一份局部快照，前端文件会和运行时装配结果分叉。

Host 测试比较 `web/` 下的文件时，路径从测试输出目录回到仓库根来计算，并在断言里打印实际路径。不要依赖进程当前目录。

### 6.2 最小 TestServer 要复用 HostUnderTest 的宿主装配

`HostUnderTest` 的注释写明：每份测试各自拼一套管道，测试会一直绿，却不再描述真正发布的宿主。方案 11.1 要求最小管线使用与实际模块相同的 filter、middleware、serializer，并核对组合宿主装配。实施时管线夹具应从 `HostUnderTest` 继续往下建到能发 HTTP 请求的测试宿主，并隔离数据库。另写一套精简 `WebApplication` 再手工注册过滤器，会漏掉租户失配、会话中间件和开发异常页的真实顺序。

关键安全用例在 Docker / Testcontainers 不可用时记为未验收。方案 11.2 已经这么要求，保持不变。

### 6.3 建议补进矩阵的用例

| 编号建议 | 场景 | 必须满足 |
| --- | --- | --- |
| A21 | Development 宿主上的未知 API 异常 | 响应不是开发异常页；无堆栈、路径、源码片段 |
| A22 | 非法 `X-Correlation-Id` | 响应、日志、审计是同一个新值；原头不出现在客户响应 |
| A23 | 配置节含 `ExposeDetails` 等未声明键 | 启动失败 |
| A24 | 用户同步导出超阈值、`window.open` | 响应无内部异常；若管理端保留业务句，形状变更写入兼容说明 |
| A25 | 上传拒绝 | 客户响应无未登记的 `reason`；管理端仅保留已登记字段 |
| A26 | 无码 `UserFriendlyException` 抽样 | 与 P0 盘点表一致：登记句保留，未登记句变为通用提示加关联号 |

## 7. 前端边界

方案 7.1 选择独立 `fetch`，管理端继续用现有 Umi 拦截器。这避开了“每个请求往共享 axios 上挂临时拦截器”的现状。`skipErrorHandler` 在客户库里只表示不再全局展示，归一化仍然发生；管理端测试继续锁住旧行为。两套语义要在模块接入指南里各写一段，避免客户页面抄管理端的 `skipErrorHandler` 后去读 `error.response`。

建议在 P3 加一条静态约束：客户示例和以后的客户页面不引用 `@umijs/max` 的 `request`，也不引用 `requestErrorConfig`。只靠代码评审会漏。

传输层写“先归一化再匹配同源与已登记路径，身份头不转发到任意绝对 URL”。首期示例若放在管理端同源页面，相对路径足够。独立 H5 的 API 基址通常是绝对 URL。方案应把“已配置的 API 源 + 已登记路径”写成允许集合，把协议相对 URL 和其他源写成拒绝。这样首期实现不会把“禁止跨源”写死，正式客户接入时也不必改安全模型。

画布错误体验里，写操作超时要“模板示范结果查询入口”。方案第 8 节没有对应的查询端点，并且写明框架不承诺幂等。以方案为准：示例只展示“结果暂未确认”，不提供一个看起来能查询任意写操作结果的接口。画布那句改掉，避免示例被做成伪幂等 API。

展示枚举正文是 `global | page | form | none`，画布配置表写成 `silent`。实现和测试使用正文的 `none`。

客户 JSON 字段使用 lowerCamelCase。管理端查询参数约定仍是 PascalCase（`web/AGENTS.md`）。客户请求库的查询参数口径在接入指南里单独写一句，避免示例函数混用两套。

生产构建：方案要求示例路由不进入生产。菜单里不出现不够，`config/routes.ts` 仍会打进管理端包。完成条件写成生产构建产物中没有该页面路由。

## 8. 画布与正文的差异

执行以 `docs/client-error-handling-development-plan.md` 为准。画布是同一方案的可视化摘要，下面三处会把实施带偏：

| 画布 | 正文 | 处理 |
| --- | --- | --- |
| “不是把全部现有 API 一次改成严格模式” | 未分类默认 Public；过滤器是全局的 | 改画布。严格模式的生效范围等于未登记端点，所以登记门禁先于过滤器替换 |
| 公开错误映射一行写“宿主配置只允许收紧或覆盖安全默认值” | 5.4 映射由代码 Provider 注册，配置文件不能指向类型、脚本或表达式 | 改画布。配置只能收紧长度、数量、回退文化和是否附带关联号 |
| 页脚“配置控制安全默认值和呈现” | 呈现方式在调用点，不在部署配置；也没有关闭安全策略的开关 | 改画布页脚，与 5.4 一致 |
| 写超时场景要求模板示范结果查询入口 | 第 8 节没有该端点，框架不承诺幂等 | 改画布 |
| 展示责任含 `silent` | `none` | 统一用 `none` |
| 管理端错误入口引用 `requestErrorConfig.ts:92` | 方案引用第 73 行附近 | 行号会漂移，引用文件即可 |

画布“依据与限制”写访谈已收敛、无待确认分支。本审核不新增产品分支，第 4 节是执行门禁，可以并进方案而不重新打开 D1–D6。

## 9. 方案中建议保持不动的决定

这些决定经过源码核对后仍然成立，实施时不要为了省事改掉：

- 沿用 ABP 错误信封，失败不统一改成 HTTP 200；OAuth 保持 `error` / `error_description`。
- 不替换 `IExceptionToErrorInfoConverter`，投影不修改原异常、`Exception.Data` 和共用 `RemoteServiceErrorInfo`。
- 不增加 `ShowAllErrors` 一类运行时开关，也不要把原文透传当作回退手段。
- 客户资源不继承内部错误模板，通用配置和两种脚本对管理员也不放宽。
- 成功 JSON 不做全局字符串替换；客户任务失败字段显式映射。
- 公开码快照不含文案和内部码；更新快照使用显式环境变量。
- 示例端点默认不注册，生产配置打开示例开关时启动失败；故障注入只放测试程序集。
- 不新建错误目录表，不顺手改造账务幂等。
- 404 管理端全局静默保持不动，与 `docs/framework-contracts.md` 和 `web/AGENTS.md` 一致。客户 404 默认要有反馈。

## 10. 建议的下一步

1. 把第 4 节四条门禁和第 6.3 节用例补进方案正文，并修正画布第 8 节列出的句子。
2. P0 的第一份清单按“谁写出响应”来做：异常过滤器、授权中间件、友好异常中间件、上传/会话/限流/租户失配、导出 `BadRequest`、`window.open` 下载、200 响应中的失败字段、Hub 推送（标明本轮不改）。
3. 同一份 P0 清单给出现有端点的三类分类，以及约 49 处无码友好异常的保留或收紧决定。
4. 门禁写进方案之后再按原文的五个提交边界开发。在此之前不替换 `AbpExceptionFilter`。

## 11. 审核结论

方案可以作为实现基线。它已经把公开范围放在服务端，把审计留在原始诊断对象上，并把管理端兼容、资源旁路、成功响应里的失败字段、上传下载和写操作结果未知放进了同一份验收矩阵。这些是这轮设计里最容易漏、也最有价值的部分。

当前版本还不能视为“执行者无须再做范围判断”。未分类默认 Public 的生效面、管理端插值、关联号管道位置、配置未知键，这四件事正文有原则、没有落点。补进方案后，P0 有明确产出，后续 P1–P4 可以按原文并行关系推进。
