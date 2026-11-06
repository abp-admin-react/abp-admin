# 客户端错误处理开发方案 · 独立审核报告（hy4）

| 项 | 内容 |
| --- | --- |
| 审核对象 | `docs/client-error-handling-development-plan.md`（版本 1.0，2026-11-05，530 行） |
| 对照材料 | `C:/Users/Administrator/.cursor/projects/e-code-abp-admin/canvases/client-error-policy.canvas.tsx`（已确认定稿的六项方向与整体方案）；`E:/code/abp_admin` 当前源码；`docs/framework-contracts.md`；`backend/AGENTS.md`、`web/AGENTS.md`；`.github/workflows/ci.yml`；ABP 10.7.0 tag 官方源码 |
| 同期文档 | `docs/client-error-handling-development-plan-review-grok.md`（另一份独立审核，本文不替代、不复制） |
| 审核日期 | 2026-11-05 |
| 审核方式 | 只读源码核查 + 官方 tag 源码抽查；未运行构建/测试，未访问生产，未修改任何业务代码 |
| 结论 | **技术方向通过；当前 1.0 文本不通过交接基线。** 建议先出 1.1 修订版冻结 P0 门禁，再进入 P1。**本审核不授权启动代码实施。** |

> **引用约定**：文中裸写的 `§N` / `§N.N`（如 §5.3、§9、§12.2）一律指**审核对象方案**的章节；本审核报告自身的章节写作「本报告 §N」或「第 N 节」；既有审核报告写作「Grok §N」。问题编号 `B`/`M`/`F`/`H` 为本报告条目。

---

## 0. 先给结论

方案把最难的部分想对了：公开资格与翻译资源分离、过滤发生在服务端、审计保留原始诊断、成功 DTO 的失败字段单独治理、写请求结果未知不自动重放。对现状的描述准确度也明显高于一般规划文档——本次逐条核对 20 余处引用，绝大多数可精确到行号。

但按当前文字直接开工，会在四个地方与方案自己的目标冲突：

1. **§5.3 的出口清单只是实际出口的一部分。** 存在已可确证的内部原文外泄链（OAuth `error_description` 与 `BusinessException.Data`），以及整套绕过 ABP 异常管线的独立错误出口（TokenGateway、SSE 直写、导出 `BadRequest`）。客户"网络响应里也看不到内部细节"这个目标，在这些路径上不成立。
2. **P0 门禁不可机器判定、覆盖基数不明。** 全局异常过滤器替换当天就会作用于所有未登记端点，`AdminCompatibility` 登记表却既没有定义主键，也没有要求枚举宿主全部 `ActionDescriptor` 做完整性断言。
3. **验证顺序自相矛盾。** P1 完成条件要求"任一受控 API 出口都不能依赖前端隐藏原文"，§11.3 又禁止把"源码看起来接好了"写成"网络响应已验证"，但真实 HTTP 管线夹具排在 P4。P1 实际上无法按自己的标准验收。
4. **文档自称"独立、可执行的交接文档"，但决策依据与授权边界都不在版本控制内。** 六项取舍的唯一完整表述在用户目录的 canvas 里，三份相关 md 全部是 git 未跟踪文件，且 canvas 与方案对"是否已获授权开工"给出相反表述。

另有 11 处事实性/工程性瑕疵，见第 4 节。严重度统计：阻断级 4 项、重要 9 项、事实与文档 11 项、建议验收补充 9 项。

**与 Grok 报告的关系**：本文不重开 D1–D6，也不重复其已充分论证的四条门禁（全局登记范围、管理端插值盘点、关联号管道位置、宽松配置原始键校验）——这四条经核查成立，应保留。本文聚焦三块它未覆盖的内容：出口完整性的源码级证据与外泄链、发布门禁与验证顺序的机器可判定性、文档与工程治理。同时在 F3 与 M5 对 Grok 的两处结论作修正。

---

## 1. 多角度评级

| 角度 | 评级 | 关键依据 | 对应问题 |
| --- | --- | --- | --- |
| 产品取舍与需求可追溯 | 中 | D1–D6 与 canvas 六项一一对应；但 D3 的"完整诊断留在受权日志入口"全文无落点 | M1 |
| 安全威胁模型 | 偏弱 | 防泄漏规则细，但缺信任边界定义、缺跨租户/客户角色/被冒用管理员 token 的模型；多租户文案定制被无意禁止 | M2、B4 |
| 服务端出口完整性 | **不通过** | 7 个文件清单外另有整套 TokenGateway、SSE、OAuth grant、大量 `WithData` 与 200 内诊断字段 | B1、B4、M4 |
| ABP 扩展点可行性 | 基本成立 | 官方源码确认 filter 两个钩子可 override、中间件包装方法为 private；但 `ShouldHandleException` 的覆盖盲区未处理 | M3、§8 |
| 端点分类与授权 | 设计对、门禁弱 | 资格判定顺序严谨（不认 `clientType`/`client_id`/Origin），但登记表主键与覆盖基数未定义 | B2 |
| 协议与契约 | 中上 | 保留 ABP 信封、不改 200、OAuth 独立形状均正确；成功 DTO 失败字段映射规则有空洞 | M4、M5 |
| 多语言与资源隔离 | 成立 | 贡献者遍历、`UpdateAsync` 可写、csproj 白名单均经源码确认，方案判断正确 | M2 |
| 前端设计 | 中上 | 纯 TS 归一、快照只含机器契约、`skipErrorHandler` 语义收紧都好；跨源/CORS/凭证未定义 | M6、M9 |
| 阶段划分与依赖 | **不通过** | P1 验收标准与 P4 才交付的验证手段错配；全局改动不可局部回退 | B3、M7 |
| 测试与验收矩阵 | 中 | A01–A20 覆盖面广，但否定型断言缺覆盖基数与自动扫描口径 | M8、§6 |
| 发布、回退与运维 | 偏弱 | "关闭客户入口即可回退"对已生效的全局 filter/服务替换不成立 | M7 |
| 文档工程与交接 | **不通过** | 未纳入版本控制、绝对 Windows 路径链接、授权表述冲突、11 处事实瑕疵 | F1–F11（尤其 F10、F11） |

---

## 2. 阻断级问题（进入 P0 前必须修订文本）

### B1 · 服务端出口清单不完整，且存在已可复现的原文外泄链

**证据**（均为当前仓库源码，非推测）：

- `backend/framework-app/AbpAdmin.HttpApi.Host/FriendlyBusinessExceptionMiddleware.cs:84-92` 手写 ABP 信封，`data = business.Data` **原样回传**。
- `backend/framework-app/AbpAdmin.Domain/Sms/AliyunSmsSender.cs:86` `.WithData("Exception", ex.Message)`；同目录 `AbpAdminTencentCloudSmsSender.cs:138` `.WithData("ProviderError", ...)`；`AbpAdmin.Application/VirtualFileExplorer/VirtualFileExplorerAppService.cs:55-56` `.WithData("Path", path)`；`OpenIddict/OpenIddictApplicationDescriptorBuilder.cs:343` `.WithData("value", exception.Message)`。
- 这四点经 `business.Data` 与中间件 `:91` 组合，构成一条**不经过任何异常转换器、也不需要未知异常**的确定性外泄链。方案 §3.4 只写了"禁止把 `ex.Message` 放进允许字段"，没有要求盘点存量 `WithData`，P0 也没有对应的产出物。`modules/platform/AbpAdmin.Collection/Withdrawals/WithdrawGuard.cs` 等处另有 30+ 个 `WithData("Reason"/"Balance"/"Floor", ...)`。
- `backend/framework-app/AbpAdmin.Application/Identity/IdentityUserSearchAppService.cs:166` `new UserFriendlyException(L["AbpAdmin:InvalidSearchCondition", ex.Message])` 与 `AuditLogs/AuditLogAppService.cs:499,573` 把 `ex.Message` 作为本地化参数直接插值进对外消息。这正是 Grok §4.2 所说"管理端也要先过滤参数再插值"的具体生产点，说明该风险不是理论上的。
- **完全绕过 ABP 异常管线的独立出口**：`backend/modules/platform/AbpAdmin.TokenGateway/Http/GatewayControllerBase.cs:25-26` 以 `ObjectResult` 直接返回 `{error:{code,message,type}}`（`[AllowAnonymous]`、`[IgnoreAntiforgeryToken]`）；`Http/ChatController.cs:165-169` 用 `Response.WriteAsync` 直写 SSE 错误帧；`Http/AuthController.cs:47-56` 有独立限流出口。它们既不在 §5.3 的 7 文件清单里，也不在 §4 端点分类的视野内，且响应形状不是 ABP 信封，§7.2 的归一器（只校验 ABP/OAuth 形状）无法识别。
- `backend/framework-app/AbpAdmin.Application/Monitoring/CacheMonitorAppService.cs:68,90,100`、`HttpProbeJobHandler.cs:124`、`ScheduledJobs/ScheduledJobScheduler.cs:380` 在 HTTP 200 结果里内嵌 `ex.Message`；方案 §3.3 说"客户 DTO 显式接入"，却没有列出这些作为示例或登记项。

**影响**：即使 P1–P4 全部按文字做完，上述路径仍会把内部原文或内部形状送到客户网络响应。方案 §1.1 的目标（"浏览器网络响应中也不应取得内部错误细节"）会被证伪。

**建议**：
1. P0 第一份清单改为按「**谁把字节写进响应**」枚举，数据源为宿主装配后的全部 Controller/Action + 全部 `app.Use(...)` 内联中间件 + `WriteAsJsonAsync`/`WriteAsync`/`ObjectResult`/`BadRequest` 静态扫描结果，而非手列 7 个文件。
2. 单列一张「存量 `Exception.Data` / `ex.Message` 插值登记表」，逐项标记"保留（已批准管理语义）/ 转为公开码 / 从 Data 中移除"，并把 `WithData("Exception", ex.Message)` 这类生产点作为 A02 的必测夹具。
3. 明确 TokenGateway 与现有 SSE 在本轮的归属：纳入统一 writer、或显式写入非目标并说明其错误形状不进入客户归一器。不能沉默。

### B2 · 全局过滤器替换的门禁缺主键、缺覆盖基数、缺机器断言

方案 §4.1「未分类端点运行时默认 Public」+ §12.1「先完成管理登记，再开放客户入口」+ P0「端点契约测试要求当前框架 API 有明确分类」这三句无法同时按字面执行，Grok §4.1 已指出。本审核补充的是**即使接受该门禁，它仍然不可执行**：

- 登记表的主键未定义。`(HTTP method, route template)`、`(ControllerType, ActionName)`、`(ServiceType, MethodName)` 三种选择在 ABP Auto API 下行为不同：`BizProjectAppService` 就是自动暴露为 `/api/app/biz-project`（`Services/BizProjectAppService.cs:20-21`），AppService 方法上的自定义 CLR 元数据能否出现在 `Endpoint.Metadata` 需要实测，方案 §4.2(5) 意识到了权限差异，却没定义匹配键。带路由参数、租户前缀、版本前缀的匹配规则也未写。
- "当前框架 API"是范围歧义（Grok 已指出应覆盖平台模块），但更根本的是**没有覆盖基数**：应断言的是"宿主最终装配枚举出的全部 `ActionDescriptor` 都在登记表内"，新增端点未登记时 CI 直接失败。方案 P0 写的是"每个待接出口有明确适配负责人"，这是人力分配，不是机器门禁。
- "未分类默认 Public"是 fail-safe，但反方向风险仍在：若登记匹配按字符串路由实现，路由重构后**静默降级为 Public**，管理端丢提示且无人察觉。方案 A04/A05 测的是伪造身份和授权，不测登记漂移。

**建议**：把 P0 完成条件改写为可执行的三条——① 登记表主键采用 `(ServiceType, MethodName)` 或经实测确认的等价稳定键，并附路由到主键的解析函数；② 新增宿主装配级测试 `AllEndpointsHaveAudienceTests`，枚举 `IActionDescriptorCollectionProvider` 并断言 100% 覆盖、分类值合法、`AdminCompatibility` 条目均绑定既有权限；③ 该测试进入 CI 的 `dotnet test backend/AbpAdmin.slnx --nologo`，新增端点未登记即红。

### B3 · 验证顺序倒置：P1 的验收标准需要 P4 才交付的手段

- P1 完成条件（§9）：「任一受控 API 出口都不能依赖前端隐藏原文；投影不修改原始诊断对象；故障时不会再抛格式化异常」。
- §11.3：「禁止把'源码看起来接好了'写成'网络响应已验证'」。
- §10 表头：「检查完整原始 HTTP body、headers、脚本及 UI，而非只看 toast 文案」；A01–A12 全部是响应级断言。
- 但「新增最小真实 HTTP 管线测试夹具」是 **P4 的第一项**（§9 P4），§11.1 也确认现有 `HostTestBed.cs:11-13`「不启动 Kestrel」、无 `TestServer`/`WebApplicationFactory`。

**影响**：P1 期间 A01–A12 只能靠单测直调投影器和源码断言"通过"，与 §11.3 直接冲突；更糟的是 P2/P3 按 §9「可并行」开展时，全局 filter 与语言服务替换已经落地却从未经过真实管线验证，风险窗口贯穿三个阶段。

**建议**：把「最小 TestServer 管线夹具」从 P4 提前到 **P1 的第一项交付**，作为 P1 的完成条件之一；P4 保留的是"全量矩阵 + 组合宿主装配一致性 + 文档"。同时把 §9 的并行关系改为：P1 的投影器与真实夹具冻结后，P2/P3 才可并行。Grok §6.2 关于"从 `HostUnderTest` 继续往下建、不要另写精简宿主"的建议成立，应一并采纳。

### B4 · 认证/OAuth 出口未纳入统一治理

- `backend/framework-app/AbpAdmin.HttpApi.Host/ExtensionGrants/ImpersonationTokenExtensionGrant.cs:130-133`：`catch (BusinessException ex) { return ...CreateErrorResult(InvalidGrant, ex.Code ?? ex.Message); }`，源码注释 `:132` 明写「Message 是未本地化的原始文本」。无 Code 时原始 `ex.Message` 直接进入 OAuth `error_description`。
- 方案 §3.2 只说「OAuth/OpenIddict 返回仍遵循 `error`/`error_description` 等原协议；控制可公开内容」，但 §5.3 的逐文件清单没有 grant/token 出口，A 矩阵也没有一条针对 OAuth 错误描述的泄漏用例。§3.4 只约束"登录、找回密码等匿名流程不区分账号是否存在"。
- 客户端 401 目前至少两种形状并存：`Identity/IdentitySessionValidationMiddleware.cs:57-62` 手写 `{error:"session_revoked", error_description:...}`，以及 ABP 信封的 `Client:AuthenticationRequired`。§7.2 的 `CustomerErrorKind` 有 `authentication` 但没有"会话被撤销"分支，归一规则也没写两套 401 的识别与优先级。

**影响**：客户在登录/换票据这一最高频路径上，仍可能拿到内部原文；前端 401 恢复逻辑面对两种形状未定义行为。

**建议**：把 OpenIddict grant/token 系列出口列入 §5.3，并新增"OAuth 错误描述白名单"投影（沿用公开码目录，不套 ABP 信封）；在 §7.2 明确两类 401 的归一映射与 `session_revoked` 的 kind；补验收项 H02、H03。

---

## 3. 重要问题（P1 前修订）

### M1 · D3 的「受权日志入口」没有落点

D3 与 canvas Q3 都承诺「完整技术诊断通过有权限的日志入口查阅」。但全文没有任何 P 阶段任务、文件落点、权限定义或验收项描述这个入口；§4.3 只是说"管理监控页已批准的诊断 DTO 仍按既有权限运行"（`docs/framework-contracts.md:76-77` 确有此取舍）。这不等于 D3 已交付：客户支持人员凭 `correlationId` 在哪里、以什么权限、保留多久能查到完整异常链，方案未定义。§3.1 反而强调"前端不得把它作为访问日志的授权凭证"，正确但让 D3 更悬空。

**建议**：在 §12.3 DoD 加一条「D3 交付证据」：明确入口（复用现有审计/监控页或新建）、所需权限、关联号熵与有效期、客服检索 SLA，或显式把它降级为「本轮非目标，D3 中的该子承诺后移」并同步 canvas。二选一，不要留白。

### M2 · §6 事实上永久禁止了租户级客户文案定制，却未作为取舍登记

`backend/framework-app/AbpAdmin.Domain/Localization/DbLocalizationResourceContributor.cs:18` 持有 `ICurrentTenant`，`:21` `IsDynamic => true`，类注释 `:13` 写明「租户级覆盖优先于 host 级覆盖，host 级覆盖优先于虚拟文件 json 里的静态文本」。方案 §6 要求「跳过客户静态资源的数据库贡献者」+「`UpdateAsync` 拒绝动态覆盖」（`LanguageTextAppService.cs:268` 现有校验仅覆盖资源名与文化，`:271-285`），两者合起来等于**所有租户的客户错误文案只能由代码发布统一决定**。

在这套多租户架构（`__tenant` 头、租户级语言覆盖、租户解析失配手写 403）里，这是一个产品级取舍，不是技术细节。D2「随版本发布」是否意味着放弃租户定制，canvas Q2 未明确。方案若默认这样做，正式 SaaS 客户接入时大概率会被要求重开 D2。

**建议**：在 §1.3 非目标或 §6 显式写明「本轮不支持租户级客户文案覆盖」并注明「如未来需要，须重开 D2 并重新设计资源出口的租户隔离与缓存键」；同时在 §6.6 补一条缓存键约束：管理专用资源入口若与公开入口共用 URL 参数区分，缓存键必须包含 audience/资格维度，避免管理视图串入公共缓存。

### M3 · `ShouldHandleException` 的盲区会架空 A10

抽查 ABP 10.7.0 tag 官方源码 `AbpExceptionFilter.cs`：`OnExceptionAsync` 为 `public virtual`，`ShouldHandleException`、`HandleAndWrapException`、`LogException` 均为 `protected virtual`。其中 `ShouldHandleException` 仅在「ControllerAction + HasObjectResult」「Accept 为 JSON」「IsAjax」三者之一成立时返回 true，**否则只记日志并直接 return，不设 `ExceptionHandled`**。`AbpExceptionHandlingMiddleware.InvokeAsync` 也只在 `actionInfo.IsObjectResult` 时包装，否则 `throw`。

方案 §5.3(1) 要求「对客户 API 的 void/FileResult/非 JSON Accept 明确纳入安全处理」，A10 把它们列为必测，但方案没有说要 override `ShouldHandleException`。若不 override，这些请求的异常会绕过 MVC filter，冒泡到外层 `UseErrorPage()`（非 Development）或 `UseDeveloperExceptionPage()`（Development，`:1419`），后者正是 A21（Grok 提议）要防的开发异常页。

另有一个实施路径未定：`HandleAndWrapException` 中 `context.Result = new ObjectResult(new RemoteServiceErrorResponse(remoteServiceErrorInfo))`，而 `remoteServiceErrorInfo` 是 `LogException` 的 `out` 参数，**同一对象同时进日志和响应**。方案要求"保留 ABP 原日志/通知/状态码，又不修改共用对象"，最可行的实现是调用 `base.HandleAndWrapException(context)` 后替换 `context.Result`；但方案只写了"在 HTTP 响应创建时生成公开新对象"，未落到可编译的伪代码。

**建议**：§5.3 明确写出「override `ShouldHandleException` 使受控 API 的 void/FileResult/非 JSON Accept 也进入包装」以及「调用 base 完成日志/通知/状态码后替换 `context.Result`，禁止改写 `remoteServiceErrorInfo`」；并在 §5.3 保留已正确的判断——`AbpExceptionHandlingMiddleware.HandleAndWrapException` 确为 `private`（官方源码核实），不可假设 override，方案原话成立。

### M4 · 成功 DTO 的失败字段缺少「无码字符串 → 公开码」的规则

§3.3 定义 `ClientFailureDto`（`Code`/`Message`/`CorrelationId`），要求业务 DTO 映射时调用投影核心；§3.2 定义 8 个基础公开码；§3.4 定义了参数与校验映射。但存量任务/批量/监控结果里的失败信息是**纯字符串**（如 `ScheduledJobScheduler.cs:380` 的 `ex.Message.Truncate(...)`、`HttpProbeJobHandler.cs:124` 的 `error = ex.Message.Truncate(...)`、`CacheMonitorAppService.cs` 的 `ConnectionError`），**没有内部错误码**。投影核心按什么把这类字符串映射到 `Client:OperationFailed`？`Code` 是必填还是可空、无码时 `Message` 从哪个资源键取、关联号如何生成，方案都没写。§3.3 说"不得全局递归替换"，因此这些点必须逐个显式接入，但没有映射规则就无法接入。

**建议**：在 §3.3 增加「无码失败字符串一律映射 `Client:OperationFailed` + 通用客户文案 + 关联号，原字符串仅留日志」的兜底规则，并给出 1 个 `ClientFailureDto` 映射示例；把 `ScheduledJobScheduler`/`HttpProbeJobHandler` 列为 P0 登记项。

### M5 · 前后端契约的发布顺序、版本协商与既有快照的关系未定义（含对 Grok 的修正）

- §7.4 的 `public-error-catalog.json` 含「契约版本」，但响应里不携带版本，前端也不比对，运行时没有版本协商。后端先发新公开码、前端旧客户端走通用兜底（设计如此，可接受）；**前端先发**时会出现快照里存在但后端尚未返回的码，方案未定义这种部署序的兼容策略。
- §7.2 规定「精确匹配快照中的公开码及字段结构，才接纳安全 message」，与 §7.4「公开码、分类、公开字段变化使快照校验失败」组合后，**快照本身成了前端的安全白名单**。方案没规定快照更新与前后端发布的原子性/顺序，也没规定快照文件损坏或缺失时是 fail-closed 还是降级。
- **对 Grok §6.1 的修正**：Grok 建议「这是第二套快照，不并入 `frontend-contract-shapes.json`」——对**公开码目录**成立，但不能外推到客户业务 DTO。`web/AGENTS.md:35` 与 `docs/framework-contracts.md:14` 的机器保证是「前端消费的 DTO 形状/可空性 ↔ `web/src/abp` 手写镜像」，实现在 `backend/test/AbpAdmin.Application.Tests/Monitoring/FrontendContractSnapshotTests.cs:39` 的显式 `SeedTypes`。方案 §7.1 新增 `web/src/abp/customer/example.ts`（客户 DTO）、§3.3 新增 `ClientFailureDto`，二者都是前端消费的业务 DTO，**仍须加入 `SeedTypes` 并同提交刷新 `frontend-contract-shapes.json`**；方案 §11.1 的测试落点完全没提这一项，而 CI 注释（`ci.yml:69-71`）明确写着「契约快照未同提交刷新时本步必红」。Grok 的措辞若被读成"客户侧一律不进第一套快照"，会造成契约保证缺口。

**建议**：§7.4 补「两套快照的分工」——公开码目录走 `UPDATE_PUBLIC_ERROR_CONTRACT`，业务 DTO 形状走 `FRONTEND_CONTRACT_SNAPSHOT_UPDATE`，互不替代；并补「发布顺序：后端公开码与前端快照同版本发布；快照缺失/解析失败时 fail-closed 走通用兜底」。

### M6 · 客户 fetch transport 的跨源、凭证与 URL 归一化存在硬阻塞

§7.1 写「API URL 先归一化再匹配同源与已登记客户/共享路径」「Bearer/CSRF/租户头不得自动转发到任意绝对 URL」「`redirect:'error'` 固定」，方向正确。但：

- 首期示例放在管理端同源页面（相对路径）没问题；正式独立 H5 的 API 基址通常是**绝对跨源 URL**。方案没有定义 `credentials` 策略（fetch 默认 `same-origin`）、CORS 允许源、`XSRF-TOKEN`/`RequestVerificationToken` 的跨源传递、预检请求与 401 的处理。现有管理端约定在 `web/src/requestErrorConfig.ts:156-177`（`RequestVerificationToken` 取自 `XSRF-TOKEN` cookie、Bearer 来自 `oidc.getAccessToken()`、`__tenant`、`Accept-Language`），客户库要么复用要么明确另立，方案只写了"复用既有语言与租户约定"。
- 同源判定未指定算法。应基于 `new URL(input, location.href).origin` 比较，并显式拒绝协议相对 URL、`\\`、含 `@` 的用户信息、百分号编码绕过；只做字符串前缀匹配会被 `https://evil.com/https://trusted/` 一类构造绕过。Grok §7 已建议"允许集合"写法，本条补充凭证与 URL 解析这两项更底层的约束。
- `display` 枚举：方案用 `none`、canvas 用 `silent`（Grok §8 已指出），统一为 `none` 即可。

**建议**：§7.1 增补「同源判定算法 + 允许源/路径集合 + `credentials` 与 CSRF 的跨源策略 + 预检失败归一」；P3 加静态约束（Grok §7 提议：客户页面不引用 `@umijs/max` 的 `request`、不引用 `requestErrorConfig`），并把它做成 lint 规则而非仅靠评审。

### M7 · 回退方案对已生效的全局改动不成立

§12.2 写「关闭新增客户入口或回退整套已验证版本」。但 P1 会替换全局 `AbpExceptionFilter` 服务类型并扩展 `FriendlyBusinessExceptionMiddleware`，P2 会替换 ABP 本地化服务的**具体类注册**并改动配置贡献者——这些都是全局、无开关的替换。一旦发布，"关闭客户入口"不能让管理端恢复原错误形状，唯一路径是整版本回滚（含前后端契约与资源，方案已提共同回退）。方案没有承认这一点，也没有要求替换前的灰度/影子对比机制。

**建议**：§12.2 明确写「全局替换类改动不支持局部关闭，只能整版本回滚」；P1 增加「替换前以影子模式记录新旧响应差异、或至少在隔离环境完成管理端全量回归」的门禁；并把"管理端既有错误用例全绿"设为替换提交的合并条件，而不是等到 P4。

### M8 · 验收矩阵的否定型断言缺覆盖基数与自动扫描口径

§10 说「使用合成标记 `INTERNAL_ONLY_MARKER`」「检查完整原始 HTTP body、headers、脚本及 UI」，但没有定义覆盖基数：多少个端点 × 多少种错误类型 × 多少种语言 × 多少种出口形状。A02「没有原始 details、Data、路径、SQL」这类断言，实施时很容易变成"挑一个出口测一次即通过"。此外 `docs/framework-contracts.md:14` 的既有保证（快照同提交刷新）与 `ci.yml:69-71` 的红线说明，本仓库习惯用机器测试钉契约；本方案的新矩阵反而退回人工判定。

**建议**：把 P0 的出口清单作为测试数据源，矩阵按「出口 × 错误类型」笛卡尔积生成用例；写一个公共断言器，对完整 body + headers + 脚本 + UI 文本扫描 `INTERNAL_ONLY_MARKER` 及路径/SQL/堆栈正则集合；A 项逐条标注所覆盖的出口 ID，未覆盖的出口不得计入通过。

### M9 · 限流等待秒数存在双源，前端归一未定义优先级

`web/src/requestErrorConfig.ts:117` 现有管理端读的是**响应体** `data.RetryAfterSeconds`；方案 §3.2/§7.2 与 canvas「限流」场景写的是「根据已校验的 `Retry-After` / `retryAfterSeconds` 显示等待时间」，`CustomerError` 有 `retryAfterSeconds?: number`。后端 `OpenIddict/TokenEndpointRateLimitingMiddleware.cs:107-120` 同时写 `Retry-After` 头和 JSON，Token 端点与 ABP 信封形状又不同。两者不一致或缺失时取谁、负值/超大值/非整数的边界，方案未定义；§5.3 说要保留 `Retry-After` 头，但没说响应体字段是否保留。

**建议**：§3.2 明确「以已校验的响应头 `Retry-After` 为唯一事实源，响应体字段仅作管理端兼容保留；非法/越界值降级为通用限流提示」，并在 A09 增加双源冲突用例。

---

## 4. 事实性错误与文档工程缺陷

| ID | 位置 | 核查结果 | 建议 |
| --- | --- | --- | --- |
| F1 | canvas「依据与限制」 | 「已存在的 `docker-compose.yml`、`buildspec.yml` 及未跟踪 `nul` 保持原样」——`git status --short` 只有 ` M docker-compose.yml` 与两份 `?? docs/*.md`；全仓无 `buildspec*`，无名为 `nul` 的文件。三项中仅一项属实 | 更正 canvas 描述；方案 §2.2「本次规划时已有 docker-compose.yml 修改」属实，保留 |
| F2 | canvas references / `docs/framework-contracts.md:70` | `:70` 确为「404 全局静默」，但「管理监控页既有取舍」在 `:76-77`。canvas 把两件事合并挂到 `:70` | 拆成两条引用 |
| F3 | 方案 §13 `:73` vs canvas `:92`（**修正 Grok §8**） | `web/src/requestErrorConfig.ts` 共 202 行：`:73` 是 `export const errorConfig` 配置对象声明；`:92` 是 `const abpError = error.response?.data?.error`，紧邻 `:93-97` 真正的「validationErrors → details → message」优先展示逻辑。**canvas 的 `:92` 明显比方案 `:73` 更精确**，Grok 用「行号会漂移，引用文件即可」淡化了这处方案引用不精确 | 方案改为引用 `:93` 或「文件 + 函数名 `errorConfig.errorHandler`」，不要只写 `:73` |
| F4 | 方案 §2.1(10) | 「当前 Umi 每请求拦截器临时挂在共享 axios 实例上」在仓库源码中无对应实现；只有 `requestErrorConfig.ts:156/:178` 的**静态** `requestInterceptors`/`responseInterceptors` 数组配置。axios 仅为 Umi 传递依赖（`web/package.json:89` 的 pnpm override） | 改写为「Umi 的静态拦截器配置经 `app.tsx:340-344` 生效」，不把框架内部机制写成仓库事实 |
| F5 | 方案 §2.2 / §11.2 版本基线 | `.NET 10 / ABP 10.7.0` 属实（`Directory.Packages.props:15`、32 个 csproj 均为 `net10.0`、CI `10.0.x`），但仓库**没有 `global.json`、没有 `Directory.Build.props`**；`backend/AbpAdmin.slnx` 存在 | 把"版本变化先重验"落成机器约束：CI 增加 ABP 版本锁定断言，不要依赖人工记忆 |
| F6 | 方案 §11.2 前端检查 | `web/package.json:12` 的 `lint` 确含 Biome lint + `tsc --noEmit`（`:28`），说法属实；但 `.github/workflows/ci.yml:95/:97` **只有 `pnpm tsc` 与 `pnpm test`，没有 `pnpm lint`、没有 `pnpm build`** | 方案应注明 lint/build 不在现有 CI；若 §12.3 要求「生产构建产物中无示例路由」，需新增 CI 步骤或明确人工验收记录 |
| F7 | 方案 §13 末段 | 「exchange 21 项通过、`requestErrorConfig.test.ts` 22 项通过」——静态可数 21 个 `it` 声明 + 1 个 `it.each([403,500])` 展开 = 22 用例，数字吻合；但"通过"是运行态声明，本轮只读核查未复现 | 写明基线测试的执行日期与命令输出位置，避免与「未执行新增测试」混淆 |
| F8 | 全文 Markdown 链接 | 大量使用 `E:/code/abp_admin/...` 作为链接目标，多数渲染器会把 `E:` 当作未知协议而无法点击，且硬编码到本机；这与「独立、可执行的交接文档」相悖 | 改为仓库根相对路径（如 `backend/AGENTS.md`），外部 URL 保持绝对 |
| F9 | 方案 §5.1 表格 | 前两行写全路径 `backend/framework-app/...`，第三行起 `AbpAdminDomainSharedModule.cs`、`AbpAdmin.Domain.Shared.csproj` 省略前缀，与其他行基准不一致 | 统一为相对仓库根的路径 |
| F10 | 三份文档的版本控制状态 | 方案、Grok 报告均为 `??`（未跟踪）；canvas 位于用户目录 `C:/Users/Administrator/.cursor/projects/...`，**不在仓库内**。六项已确认取舍的完整表述因此不在任何受版本控制的交付物中 | 把方案与至少一份「决策记录（D1–D6 + 授权边界）」提交进仓库；canvas 可作为附件或导出 md，不能只留在用户目录 |
| F11 | 授权边界冲突（治理级） | canvas「六项产品取舍及整体方案已于 2026-11-05 经用户确认，规划定稿……**本次仅确认规划，不授权启动代码实施，全部实施阶段尚未执行**」；方案抬头「状态：方案已确认，**可据此开发**」「执行者按任务顺序推进，**无须重新询问这些选择**」、§9「所有任务初始未完成，执行者完成后勾选」 | 二者口径不同：确认的是**设计取舍**，不是**开工授权**。建议方案改写为「设计已定稿；实施需另行授权」，并把 §9 的勾选清单标为「授权后启用」，避免交接时被当作已获批准 |

**核查中确认属实、应予保留的**：`FriendlyBusinessExceptionMiddleware.cs:60` 判定条件与 `:84-94` 手写信封；`AnonymousSettingTrimApplicationConfigurationContributor.cs:21` 只清 settings；`PreLoginApiEndpointsConvention.cs:23` 3 API + 3 脚本白名单；`AbpAdminDomainModule.cs:169-180` 对 `Resources.Values` 无差别追加 `DbLocalizationResourceContributor`；`AbpAdmin.Domain.Shared.csproj:44,46` 与 `Biz.Template.csproj:38` 的 `EmbeddedResource` 白名单（新增 `Localization/ClientErrors/*.json` 确需补 Include）；`BizProject.cs:11` 无 `IMultiTenant`、`BizProjectAppService.cs:50-53` 列表无数据过滤；§11.1 五个测试项目与 `HostTestBed` 描述；`web/AGENTS.md` 三条前端纪律。方案 §2 的"已核实现状"整体可信。

---

## 5. 建议补进验收矩阵的用例

以下用 `H` 前缀，与 Grok 建议的 `A21–A26` **并存不重号**；两组应一并并入 1.1 版矩阵。

| 编号 | 场景 | 必须满足 |
| --- | --- | --- |
| H01 | 出口清单完整性 | 宿主装配枚举的全部 `ActionDescriptor` 与内联中间件出口均有 `Public`/`Shared`/`AdminCompatibility` 分类；新增未登记端点使 CI 失败 |
| H02 | OAuth grant 失败 | `ImpersonationTokenExtensionGrant` 抛无 Code `BusinessException` 时，`error_description` 不含 `ex.Message`，仅公开码/安全文案 |
| H03 | 双形状 401 | ABP 信封 401 与 `session_revoked` 401 均归一为 `authentication`；并发恢复只触发一次；原头不进客户响应 |
| H04 | 存量 `Exception.Data` | 短信发送器、`VirtualFileExplorer`、`OpenIddictApplicationDescriptorBuilder` 抛出的 `BusinessException` 经友好异常中间件后，`data` 中无 `INTERNAL_ONLY_MARKER`、无路径/异常原文 |
| H05 | 无码任务失败 | 200 结果内纯字符串失败信息映射 `Client:OperationFailed` + 通用文案 + 关联号；原串只在日志 |
| H06 | TokenGateway / SSE | 独立错误体与 SSE 错误帧按登记结论处理；若列为非目标，则断言客户归一器不会把它们误判为已批准 ABP 错误 |
| H07 | `ShouldHandleException` 盲区 | 客户 API 的 void/FileResult/非 JSON Accept 异常不落到开发异常页，响应体经统一投影；`HasStarted` 时不追加第二份 JSON |
| H08 | 跨源与凭证 | 绝对 URL 允许集合外的源、`//` 协议相对 URL、含 `@` 用户信息、编码绕过均被拒绝；`credentials` 与 CSRF 头不转发到未登记源；预检失败归一为安全错误 |
| H09 | 契约部署序 | 快照缺失/损坏时 fail-closed；后端新增公开码 + 前端旧快照时走通用兜底；`ClientFailureDto` 与客户 DTO 的镜像变更已同提交刷新 `frontend-contract-shapes.json` |

Grok 建议的 A21（Development 异常页）、A22（非法 Correlation-Id 三处一致）、A23（宽松配置键启动失败）、A24（同步导出 `window.open`）、A25（上传 `reason`）、A26（无码友好异常盘点）经本审核复核均成立，建议保留。A22 可再补两条：关联号的字符集/长度/熵下限，以及 D3 受权检索入口的权限断言（见 M1）。

---

## 6. 建议的 1.1 版 P0 门禁（可直接替换 §9 P0 完成条件）

1. **出口清单**：以宿主装配枚举 + 静态扫描生成版本化清单，覆盖全部 Controller/Action、`app.Use(...)` 内联中间件、`WriteAsJsonAsync`/`WriteAsync`/`ObjectResult`/`BadRequest`、OAuth grant/token、SSE/Hub 推送、200 结果内失败字段；每项标注"谁写出响应、当前形状、目标公开码或协议适配器、是否本轮改造"。
2. **端点登记表**：定义稳定主键与路由解析函数；`AllEndpointsHaveAudienceTests` 断言 100% 覆盖、`AdminCompatibility` 均绑定既有权限；该测试进入 CI。登记范围含 `modules/platform/*` 与 `modules/biz/*`（Grok §4.1）。
3. **存量诊断值登记表**：`WithData(...)`、`ex.Message` 插值、200 内嵌诊断逐项标记保留/收敛/移除；与无码友好异常盘点表（Grok §4.2）合并维护。
4. **关联号与配置门禁**：在 `UseCorrelationId()`（`:1427`）位置做字符集/长度/熵校验并生成唯一值，投影器只读不另造（Grok §4.3）；宽松配置键从 `IConfiguration` 原始子键校验（Grok §4.4）。
5. **真实管线夹具前置**：最小 TestServer 夹具在 P1 首项交付，P1 完成条件以真实响应为准（B3）。
6. **文档门禁**：方案、决策记录、Grok 报告与本报告纳入版本控制；修正 F1–F11；明确"设计定稿 ≠ 开工授权"（F11）。

---

## 7. 建议保持不动的决定

以下经本审核独立复核（含 ABP 10.7.0 tag 官方源码抽查）仍成立，实施时不要为省事改掉：

- 沿用 ABP 错误信封，失败不统一改 HTTP 200；OAuth 保持 `error`/`error_description`；`details` 客户端恒 `null`。
- **不替换 `IExceptionToErrorInfoConverter`。** 官方 `AbpExceptionFilter.LogException` 会把转换后的 `RemoteServiceErrorInfo` 序列化进日志并同时用于 `context.Result`；全局替换 converter 会一并删掉诊断，方案的禁令正确。
- 投影不修改原异常、`Exception.Data` 与共用 `RemoteServiceErrorInfo`；日志、审计、异常通知保留原始诊断。
- `AbpExceptionHandlingMiddleware.HandleAndWrapException` 在 10.7.0 中确为 `private`，方案「不能假设通过 override 完成过滤」的判断**经官方源码核实成立**；同 tag 的 `AbpExceptionFilter` 的 `ShouldHandleException`/`HandleAndWrapException` 均为 `protected virtual`，方案 §5.3 的替换路线可行（但见 M3）。
- 不叠加 `UseAbpExceptionHandling` 形成第二个原文出口；仓库当前也确实没有显式调用。
- 客户资源不继承内部模板，通用配置与 `ConfigurationScript`/`LocalizationScript` 对管理员也不放宽；成功 JSON 不做全局字符串替换。
- 公开码快照不含文案与内部码，更新走显式环境变量；示例默认关闭、生产启用时启动失败、故障注入只放测试程序集。
- 不新建错误目录表，不顺手改造账务幂等；管理端 404 全局静默保持。

---

## 8. 最终结论与放行条件

方案的**方向与分层值得保留**：公开范围在服务端、审计留原始诊断、端点资格不信任客户端自报身份、写请求结果未知不自动重放、资源出口与异常出口一并治理——这些是同类方案里最容易漏的部分，本文的核查没有发现需要推翻的设计决定。

但 1.0 文本尚不满足它自己宣称的「独立、可执行交接文档，执行者无须重新询问这些选择」：出口清单不完整（B1）、门禁不可判定（B2）、验证手段排在验收之后（B3）、认证出口未纳管（B4）、决策与授权记录未进版本控制（F10/F11）。在这些条件下启动 P1 的全局过滤器替换，最可能的后果不是"客户看到 SQL"，而是**管理端在替换当天静默丢掉既有业务提示，且没有任何真实 HTTP 测试能在合并前发现**。

**放行条件**（全部满足后方可进入 P1）：

1. 发布 1.1 版方案，并入本报告 §6 的六条 P0 门禁、本报告 §5 的 H01–H09、Grok 的 A21–A26，并修正 F1–F11。
2. P0 产出三份版本化清单（出口、端点登记、存量诊断值），且 `AllEndpointsHaveAudienceTests` 在现有 CI 中通过。
3. 最小 TestServer 夹具随 P1 首项交付，管理端全量既有错误用例在该夹具下回归通过。
4. 方案、决策记录与两份审核报告纳入版本控制；授权边界书面澄清为「设计定稿，实施需另行授权」。

本审核为只读评审，未运行构建或测试，未访问生产，未修改任何业务代码；仅新增本报告文件。
