# 客户端错误处理方案：三份审核独立核实与处置

> 日期：2026-11-05；审阅基线：开发方案v1.0；首次合并v1.1。本文件在第二轮随v1.2更正处置标签、Q20变量名和H01链路表述，原始审核报告不改。仅修订文档，未实施代码、未运行新增测试、未访问生产。

执行正文：[client-error-handling-development-plan.md](./client-error-handling-development-plan.md)。原报告保持不变：[Grok](./client-error-handling-development-plan-review-grok.md)、[hy4](./client-error-handling-development-plan-review-hy4.md)、[Qwen](./client-error-handling-development-plan-review-qwen.md)。

## 核实方法与结论

三份报告分别核对当前仓库、exchange参考实现及ABP 10.7.0固定标签源码，再由主审合并冲突建议。报告中的“实测”“可复现”等自述不直接当证据；本轮没有请求抓包，不把静态风险说成已验证的生产泄漏。原计划行号属于v1.0历史位置，下面以章节名指向修订落点；报告行号未改变。源码行号按本次读取版本计数。

采用四类处置：**吸收**＝加入执行计划；**部分吸收**＝保留成立部分，并写明不采纳的部分；**已覆盖**＝保持原规则，必要时补验收；**拒绝**＝事实不成立、超出确认范围或会破坏既定边界。实现建议中的合理替代方案不当作事实错误；选择其中一种时说明取舍。

本记录按报告内条目及重复建议整理为 G01—G34、H01—H37、Q01—Q32，共103个追踪项，**不代表103个独立缺陷**。跨报告重复项只进入一份计划任务。v1.1已补入：隐式ABP异常中间件、全装配出口登记、OAuth/下载/上传写出、入口关联号、严格配置绑定、静态资源范围、双快照分层、P1真实HTTP夹具、A01—A34及全局改动回滚条件。

## 关键拒绝与纠错速查

| 审核位置 | 不采纳的断言/要求 | 原因与证据 | 最终处理 |
| --- | --- | --- | --- |
| Qwen 136、240行 | ABP原生路径不返回Data，只有手写中间件会返回 | 固定版本选项默认包含IBusinessException；转换器命中后赋exception.Data，见下方O1/O2。全仓没有覆盖配置，不能推出默认列表为空。 | 所有异常出口都要过滤，管理等待字段另保兼容。Q10。 |
| Qwen 134行 | zh-Hant缺键就必然返回内部错误码 | 本仓默认文化en；ABP默认文化回退先于最终缺键，见本仓DomainSharedModule:65和O3。 | 测“整个允许回退链缺键”，不记录虚假必现基线。Q11。 |
| Qwen 150–151行 | 业务/平台模块控制器为0 | TokenGateway的Http目录已有4个具体Controller；按Controllers目录名搜索漏检。 | 改为按真实装配、继承/路由和写出点盘点。Q13。 |
| Qwen 160行 | 开发/测试未登记端点只计日志、保留原响应 | 这会绕过已确认D1及Development默认Public，管理资格不能由“待登记”推定。 | 离线预览/计数可以做，在线原文dry-run不做。Q14。 |
| Qwen 124行 | 禁止任何HTTP状态推导kind | 无体401/403/429仍需安全分类和会话/等待动作。拒绝原文与允许本地安全状态兜底是不同问题。 | 公开码优先，协议次之，状态安全兜底。Q07。 |
| hy4 182行 | Umi只有业务静态拦截器，没有临时挂载机制 | 当前`.umi/plugin-request/request.ts:144–147、188–216、222–235`确实在共享axios实例挂载、卸载单请求拦截器；生产生成物相同。 | 精确表述当前4.7.24 runtime能力，独立fetch决定保留。H17。 |
| Grok 62、79行 | 49处全部是无码UserFriendlyException | 49个构造点中12处显式带code；AuditLogAppService的11处及GdprRequestAppService:298是反例。 | 全量盘点，但不以“49无码”作门禁。G04。 |
| hy4 141行 / Qwen 190行 | 模板客户DTO也统一塞入框架Application.Tests的SeedTypes | 违反框架测试不反向引用业务模块的依赖方向；现有递归也限定种子程序集，见FrontendContractSnapshotTests:126、159、197–199。 | 框架DTO进原快照，模板DTO进模块形状测试；目录快照由宿主组合负责。H09/Q20。 |
| Grok 176行 | 首期同时完成独立H5跨源API允许源能力 | 合理后续能力，但超出已确认D5“机制与模板示例”；需要独立凭据、CORS/CSRF契约。 | 本期明确同源、拒绝未支持跨源；保留后续扩展说明。G24；hy4策略建议已吸收，见更正后的H10。 |

### 固定版本源码行号校准

- **O1**：[AbpExceptionHandlingOptions.cs](https://github.com/abpframework/abp/blob/10.7.0/framework/src/Volo.Abp.ExceptionHandling/Volo/Abp/AspNetCore/ExceptionHandling/AbpExceptionHandlingOptions.cs#L25)，25—28行：默认SendExceptionDataToClientTypes含IBusinessException。
- **O2**：[DefaultExceptionToErrorInfoConverter.cs](https://github.com/abpframework/abp/blob/10.7.0/framework/src/Volo.Abp.ExceptionHandling/Volo/Abp/AspNetCore/ExceptionHandling/DefaultExceptionToErrorInfoConverter.cs#L138)，138—140行回传Data，186—190行使用Data替换文案参数；不能仅过滤响应data而保留已插入原文的message。
- **O3**：[AbpDictionaryBasedStringLocalizer.cs](https://github.com/abpframework/abp/blob/10.7.0/framework/src/Volo.Abp.Localization/Volo/Abp/Localization/AbpDictionaryBasedStringLocalizer.cs#L151)，151—159行使用默认文化回退；本仓 [AbpAdminDomainSharedModule.cs:65](../backend/framework-app/AbpAdmin.Domain.Shared/AbpAdminDomainSharedModule.cs:65) 注册默认en。
- **O4**：[AbpApplicationBuilderExtensions.cs](https://github.com/abpframework/abp/blob/10.7.0/framework/src/Volo.Abp.AspNetCore/Microsoft/AspNetCore/Builder/AbpApplicationBuilderExtensions.cs#L108)，108—113行UseUnitOfWork自动调用UseAbpExceptionHandling；本仓 [HostModule:1516](../backend/framework-app/AbpAdmin.HttpApi.Host/AbpAdminHttpApiHostModule.cs:1516) 已调用该扩展。此项是现有必接出口，不是未来可能风险。

以下完整表的缩写在各节定义；拒绝项同时保留原报告行、源码行或上面的固定版本证据。对范围/授权取舍，证据为计划§1.2已确认D1—D6，不伪造不存在的代码依据。表中canvas行号指修订前版本；本轮已同步其摘要措辞，执行以Markdown为准。

合并后二次复核还补了三项执行约束，不归为原报告新增问题：① HostTestBed.cs:10—15已记录Quartz静态依赖，真实HTTP夹具按环境进程隔离；② kind优先不允许覆盖HTTP401/403的会话动作，码/状态冲突安全兜底；③ 二进制下载接口200错误JSON也须有界识别，合法JSON文件按声明契约处理。已纳入计划§7.2、§7.3、§11.1及A10/A15。


## Grok

已完整核对 `docs/client-error-handling-development-plan-review-grok.md` 第 1–226 行。下表报告行号均指该原报告；对原计划的引用使用章节名，指本轮修订前的 v1.0 历史版本。重复建议合并到稳定编号，不作为遗漏。

路径缩写：`B/`＝`../backend\`；`W/`＝`../web\`；`P`＝`./client-error-handling-development-plan.md`；`F`＝`./framework-contracts.md`；`C`＝`C:\Users\Administrator\.cursor\projects\e-code-abp-admin\canvases\client-error-policy.canvas.tsx`。官方固定版本依据及方法名见表后脚注。

| ID | 原报告行号 | 建议摘要 | 处置 | 核实事实、源码依据及拒绝理由 |
| --- | --- | --- | --- | --- |
| G01 | 43、60、107–109 | 异常扩展点成立，不叠加第二套 ABP 中间件 | 部分吸收，补重大事实修正 | 不额外叠加正确，但报告遗漏当前已经自动注册的事实。`B/framework-app/AbpAdmin.HttpApi.Host/AbpAdminHttpApiHostModule.cs:1516` 调用 `UseUnitOfWork()`；官方 ABP 10.7.0 `AbpApplicationBuilderExtensions.UseUnitOfWork` 内部先调用 `UseAbpExceptionHandling()`。拒绝维持 P §5.3“若实际执行版本已有”的条件语气，必须列为现有必接出口；保留 UoW，替换/适配原异常中间件。外层 Friendly 兜底不能重新处理内层已经消费并写出的异常。 |
| G02 | 58–71、217、219–220 | 全局替换前完成全部现有管理端点登记，同提交带清单 | 吸收 | 宿主确实装配模板及全部平台模块：`B/framework-app/AbpAdmin.HttpApi.Host/AbpAdmin.HttpApi.Host.csproj:71–77`。P §4.1 只写“当前框架 API”，口径不足。明确为宿主最终装配中的全部受控 HTTP API，包含 ABP、framework-app、平台模块及 Biz.Template；协议专用出口单列，不把静态文件/Razor 页面强行当 ABP API。现有管理端点选择 Public 时逐项记录兼容变化。 |
| G03 | 73–77 | 管理端也必须先筛选参数再本地化插值 | 吸收 | 官方 ABP 10.7.0 `DefaultExceptionToErrorInfoConverter.TryToLocalizeExceptionMessage` 遍历 `Exception.Data` 替换模板；`CreateErrorInfoWithoutCode` 按选项回传 Data，`AbpExceptionHandlingOptions` 默认包含 `IBusinessException`。因此仅删除响应 Data 不能撤销已经进入 message 的值。P §3.4 应明确覆盖 AdminCompatibility，允许继续使用管理资源和批准业务码；不得全局改 converter 而破坏审计。 |
| G04 | 62、79、168、219 | 盘点约 49 处“无码”友好异常，决定保留或收紧 | 部分吸收 | 49 个 `new UserFriendlyException` 构造点、11 个生产文件属实，但“49 处无码”错误：12 处显式带 code，37 处未显式带 code。`B/framework-app/AbpAdmin.Application/AuditLogs/AuditLogAppService.cs:360–403、489–501、559–575` 的 11 处均带 code，另有 `B/framework-app/AbpAdmin.Application/Gdpr/GdprRequestAppService.cs:298`。仍盘点全部构造点，因为有 code 也可预插值原文，如 AuditLog `:498–501、572–575`；无码原文例见 `B/framework-app/AbpAdmin.Application/Identity/IdentityUserSearchAppService.cs:166`。拒绝把“49 无码”固定成门禁，改为执行时生成清单，按码、模板、参数来源及是否已格式化分类。 |
| G05 | 81–93、164 | 关联号在入口中间件校验，投影只读取稳定值；补 A22 | 吸收 | 当前 Host module 顺序为 `:1419` 开发异常页、`:1427` CorrelationId、`:1433` 生产错误页、`:1443` Friendly、`:1540` 审计。官方 `AbpCorrelationIdMiddleware.InvokeAsync/GetCorrelationIdFromRequest/CheckAndSetCorrelationIdOnResponse` 仅为空值生成新号，随后建立 provider 作用域，响应头回调捕获相同值。应在此入口位置校验并确定一次，禁止投影另造第二个值；响应头、body、日志及审计一致。 |
| G06 | 95–99、165 | Host 检查原始配置未知键，不能只检查绑定后的 Options；补 A23 | 吸收，允许等价实现 | .NET 10 `ConfigurationBinder.BindProperties` 在 `BinderOptions.ErrorOnUnknownConfiguration=true` 时已有未知键拒绝逻辑；可用严格binder或原始节扫描实现。普通绑定后校验POCO确实无法发现已丢弃的键。启动失败及Host/Domain分工吸收，不把报告建议的扫描方式变成唯一实现要求；必须启动时触发，不延迟到首次请求。 |
| G07 | 105–109、163 | 增加 Development 未知 API 异常专门验收 A21 | 吸收，强化已覆盖原则 | `B/framework-app/AbpAdmin.HttpApi.Host/FriendlyBusinessExceptionMiddleware.cs:60–69` 只接带码业务异常，其余重抛；Host module `:1419` 开发异常页。P §5.3、§12.1 已有原则，补独立矩阵便于执行。测试必须覆盖自动 ABP 中间件及外层兜底，不只测试 MVC action throw。 |
| G08 | 117、167 | 上传 reason/fileName 是独立手写响应，补 A25 | 吸收 | `B/framework-app/AbpAdmin.HttpApi.Host/Files/FileUploadContentGuardMiddleware.cs:204–223` 直接写 400，`:220` 返回 `{fileName,reason}`，异常过滤器碰不到。P §5.3 已列文件，补具体形状、允许参数及测试。不能因报告说“登记 reason”而放行任意 reason 字符串；未知拒绝原因须映射安全文案，文件名不得默认回显。 |
| G09 | 118–119、166 | 两类导出 BadRequest 与 window.open 必须独立验证，补 A24 | 吸收 | `B/framework-app/AbpAdmin.HttpApi/Controllers/UserExportController.cs:56–61` 返回 error 字符串；`B/framework-app/AbpAdmin.HttpApi/Controllers/AuditLogExportController.cs:59–64` 返回本地化字符串；`W/src/abp/identity.ts:301–307` 直接 window.open。P §5.3 已列文件，需补它们属于直接 ActionResult、记录形状变化并验证直开失败安全。 |
| G10 | 121 | 按实际写出方式登记会话撤销、租户失配及其公共映射 | 吸收 | `B/framework-app/AbpAdmin.HttpApi.Host/Identity/IdentitySessionValidationMiddleware.cs:57–61` 写 session_revoked/error_description；Host module `:1502–1507` 写 tenant_mismatch；`B/framework-app/AbpAdmin.HttpApi.Host/OpenIddict/TokenEndpointRateLimitingMiddleware.cs:107–119` 另有 OAuth 限流形状。保留各自协议和状态、输出安全文案，不能因为它们长得像 OAuth 就全部换 ABP 信封。 |
| G11 | 123 | 保留图片繁忙 filter 顺序及 Retry-After | 已覆盖，补明确验收 | `B/framework-app/AbpAdmin.HttpApi/Imaging/ImageProcessingBusyExceptionFilter.cs:9–20` 只加头、不标记 handled；`B/framework-app/AbpAdmin.HttpApi/AbpAdminHttpApiModule.cs:45–48` 注册。P §5.1、P0 已要求保留。应在真实 503 响应中断言头仍在，防止 writer.Clear 清掉。 |
| G12 | 125–127、218 | 现有 Hub payload 明确非目标，租户失配 HTTP 拒绝仍接线 | 吸收 | `B/framework-app/AbpAdmin.HttpApi.Host/SignalR/NotificationHub.cs:10–24`、`ChainStatusHub.cs:10–32` 为服务端推送 Hub，没有业务调用方法；Host module `:1490` 同时匹配 /api 与 /signalr-hubs。现有推送列非目标，但不能排除现存 HTTP 拒绝出口；未来客户 Hub 单独建立公开契约。 |
| G13 | 129 | HTTP 200 诊断 DTO 保持管理专用，不能标为 Shared | 吸收并限定含义 | `F:76–77` 接受 CacheMonitor.Default 持有人看到诊断；`B/framework-app/AbpAdmin.Application/Identity/IdentityUserAdminAppService.cs:584–591` 会把友好异常转行错误文本。P §3.3、§4.3 已排除全量改写管理 DTO。登记管理专用并保留真实权限；同时明确错误受众标签不能替代成功 DTO 的授权与字段控制。 |
| G14 | 131–133 | 新资源目录必须嵌入程序集 | 已覆盖 | `B/framework-app/AbpAdmin.Domain.Shared/AbpAdmin.Domain.Shared.csproj:44–48` 只嵌入现目录，注释记载 SettingProperties 漏嵌入先例；`B/modules/biz/AbpAdmin.Biz.Template/AbpAdmin.Biz.Template.csproj:38` 只嵌入 BizTemplate。P §2.1、§5.1、§8 已覆盖两处，保留发布包验收，不重复建任务。 |
| G15 | 135 | 静态客户资源跳过数据库贡献者、拒绝覆盖、保留恢复默认 | 已覆盖 | `B/framework-app/AbpAdmin.Domain/AbpAdminDomainModule.cs:177–179` 为所有资源添加贡献者；`B/framework-app/AbpAdmin.Application/Localization/LanguageTextAppService.cs:269–305` 可写注册资源，`:334–337` 删除覆盖。P §6 已完整覆盖。 |
| G16 | 137–141 | 保持 Domain 核心、HttpApi 适配及模块 Provider 分层 | 已覆盖 | `B/framework-app/AbpAdmin.HttpApi/AbpAdmin.HttpApi.csproj:19–20` 同时引 Contracts/Domain；模板 csproj `:17` 引 EFCore，`B/framework-app/AbpAdmin.EntityFrameworkCore/AbpAdmin.EntityFrameworkCore.csproj:12` 引 Domain。P §5.1 已匹配仓库，无需改层或反向引用业务实现。 |
| G17 | 145–149 | 新公开错误快照独立于 DTO 快照，宿主唯一对前端文件负责 | 吸收，补禁止混用 | `F:85–88` 确认 Application.Tests 不引模块；`B/test/AbpAdmin.Application.Tests/Monitoring/FrontendContractSnapshotTests.cs:34–39、95–105` 有原快照及更新逻辑。P §7.4、§11.1 已选新 Host 快照；明确不并入 frontend-contract-shapes.json，模块测试只锁自身定义，宿主组合唯一核对共享前端文件。默认缺文件应失败，不照搬旧测试自动生成行为。 |
| G18 | 151 | 快照路径从输出目录定位仓库根，不依赖 CWD | 吸收 | `B/test/AbpAdmin.HttpApi.Host.Tests/HostUnderTest.cs:112–131` 已用 AppContext.BaseDirectory 向上查 AbpAdmin.slnx。新快照应定位仓库 web 目录，并在失败断言打印解析后的绝对路径；原计划未明确这一实现约束。 |
| G19 | 153–157 | HTTP 测试复用 HostUnderTest，不另拼假管线，隔离数据库 | 吸收并补限制 | `B/test/AbpAdmin.HttpApi.Host.Tests/HostUnderTest.cs:13–19、50–58、69–109` 提供真实模块 builder；`HostTestBed.cs:10–15、37–45` 初始化但不启动 HTTP。建议正确。当前 HostUnderTest `:64–78` 固定 Development，复用它不能声称已验证非 Development 分支；生产禁止样例须扩展受控环境/测试证书接缝或独立纯配置验证，仍复用真实模块装配。 |
| G20 | 157 | Docker 不可用不能将关键安全测试算通过 | 已覆盖 | P §11.2、§12.3 已明确。`B/test/AbpAdmin.HttpApi.Host.Tests/HostTestBed.cs:25–35` 有本机连接回退路径；新增夹具坚持隔离测试容器，不能照搬成允许连真实库。 |
| G21 | 159–168 | 汇总新增 A21–A26 验收 | 吸收，关联既有处置不重复建任务 | A21 对应 G07；A22 对应 G05；A23 对应 G06；A24 对应 G09；A25 对应 G08；A26 对应 G04。均有实际源码出口支撑；A26 名称改为友好异常带码/无码及参数边界，避免沿用“49 无码”错误数量。 |
| G22 | 172 | 指南分别说明管理和客户 skip 语义 | 吸收 | `W/src/requestErrorConfig.ts:87` 的管理 skip 直接抛原错误；P §7.2 客户 skip 只映射 display none。两套确有差异，指南分别说明，客户不得复制 error.response 原文处理。 |
| G23 | 174 | 客户目录禁止导入 Umi request/requestErrorConfig，设静态门禁 | 吸收 | P §7.1 选独立 fetch，目前主要是运行行为验收；`W/src/requestErrorConfig.ts:73–97` 有管理鉴权和原文语义。新增针对声明客户目录/示例的导入约束，沿用仓库工具，不为此另引一套 lint 平台。 |
| G24 | 176 | 现在就支持配置 API origin 与路径，允许独立 H5 跨源 | 部分吸收；拒绝扩大首期实现 | 指南可注明未来扩展点，但首期保持同源相对路径及 redirect:error。P §7.1 已选唯一传输方式，`W/AGENTS.md:30` 明确相对路径/无 baseURL，D5 仅框架机制与示例。跨源还涉及 CORS、凭据、CSRF、租户头，缺少独立 H5 接入任务时不应顺带建设。拒绝的是本期新增跨源 transport，并非永久禁止后续支持。 |
| G25 | 178、195 | 删除画布要求模板示范写操作结果查询入口 | 吸收 | C:25 确有此句；P §7.3 明确结果未知、不自动重放，§8 没有通用写结果查询协议。删除这一承诺，已有业务查询入口由业务自行接入，不新增伪通用幂等 API。 |
| G26 | 180、196 | 画布 silent 统一为 none | 吸收 | C:37、133 使用 silent，P §7.2 使用 none；统一为 none。 |
| G27 | 182 | 指南明确客户查询参数命名 | 吸收 | P §3.1 的 lowerCamelCase 是错误 JSON，`W/AGENTS.md:30` 的 PascalCase 是请求查询参数，二者不冲突。客户查询名遵循后端 DTO/端点契约，默认沿用 PascalCase 适配；不能用错误 JSON 命名推导查询参数，也不能覆盖已有显式命名参数。 |
| G28 | 184 | 生产产物不能包含样例路由，隐藏菜单不算 | 已覆盖原则，吸收构建断言 | P §8、P3 已说不进生产路由；`W/config/routes.ts:14–29` 可见 hideInMenu 条目仍有 route/component。增加生产 route manifest/构建产物断言，不能只测菜单。 |
| G29 | 192 | 改画布“不把全部 API 一次改严格”歧义 | 吸收 | C:157 与 P §4.1 的全局默认 Public 存在歧义。改为登记门禁先于全局出口切换；已登记管理端按兼容规则，其余默认公开。与 G02 同一落点。 |
| G30 | 193 | 澄清画布“宿主配置覆盖安全默认值”不等于配置映射 | 吸收 | C:35 把公开错误映射与宿主配置放在一行；P §5.4 明确映射由代码 Provider 注册。画布改为模块代码注册映射、部署配置仅含 §5.4 所列项，不允许任意代码映射或类型/表达式配置。 |
| G31 | 194 | 画布页脚配置控制呈现的措辞含混 | 吸收 | C:173 混用了部署配置与调用点展示责任；P §7.2 已把 display 放调用点。改为部署选项控制边界、调用点控制提示责任，安全策略不能关闭。 |
| G32 | 197 | 92与73行锚点不一致，建议取消源码行号 | 不采纳取消行号，统一精确锚点 | `W/src/requestErrorConfig.ts:73`是errorConfig起点，`:92–97`是实际读取和优先级。不同粒度引用不构成事实矛盾，但后者更便于核实；新版统一到94行实际优先级，保留行号及方法名，方便执行者复核。 |
| G33 | 199、205–213 | 保持 D1–D6、ABP/OAuth 信封、审计、资源边界、显式 DTO、快照、样例开关、无新表及管理 404 | 已覆盖 | P §1.2、§3、§5.3–5.4、§6、§7.2–7.4、§8、§12 已覆盖全部保持项；`F:70–77`、`W/src/requestErrorConfig.ts:126–135` 支撑管理 404 与诊断边界。无需重新访谈或额外审批。 |
| G34 | 217–220 | 更新门禁/矩阵，按实际写口清单，再按五个提交边界执行 | 吸收并去重 | 分别落实 G02–G13、G21、G25–G31；P §9 的五个提交边界保持，不另造阶段。本次仅更新规划与审核处置，不能据此提前替换 filter 或修改业务代码。 |

必要脚注：

- 官方固定版本 ABP 10.7.0：[AbpApplicationBuilderExtensions.cs](https://raw.githubusercontent.com/abpframework/abp/10.7.0/framework/src/Volo.Abp.AspNetCore/Microsoft/AspNetCore/Builder/AbpApplicationBuilderExtensions.cs)，方法 `UseUnitOfWork`、`UseAbpExceptionHandling`；[AbpExceptionHandlingMiddleware.cs](https://raw.githubusercontent.com/abpframework/abp/10.7.0/framework/src/Volo.Abp.AspNetCore/Volo/Abp/AspNetCore/ExceptionHandling/AbpExceptionHandlingMiddleware.cs)，方法 `InvokeAsync`、private `HandleAndWrapException`。证实当前内层自动异常出口，不能按“未显式调用”判定不存在。
- 官方固定版本 ABP 10.7.0：[DefaultExceptionToErrorInfoConverter.cs](https://raw.githubusercontent.com/abpframework/abp/10.7.0/framework/src/Volo.Abp.ExceptionHandling/Volo/Abp/AspNetCore/ExceptionHandling/DefaultExceptionToErrorInfoConverter.cs)，方法 `TryToLocalizeExceptionMessage`、`CreateErrorInfoWithoutCode`；[AbpExceptionHandlingOptions.cs](https://raw.githubusercontent.com/abpframework/abp/10.7.0/framework/src/Volo.Abp.ExceptionHandling/Volo/Abp/AspNetCore/ExceptionHandling/AbpExceptionHandlingOptions.cs)，构造函数。证实 Data 模板插值及 IBusinessException 默认 Data 输出资格。
- 官方固定版本 ABP 10.7.0：[AbpCorrelationIdMiddleware.cs](https://raw.githubusercontent.com/abpframework/abp/10.7.0/framework/src/Volo.Abp.AspNetCore/Volo/Abp/AspNetCore/Tracing/AbpCorrelationIdMiddleware.cs)，方法 `InvokeAsync`、`GetCorrelationIdFromRequest`、`CheckAndSetCorrelationIdOnResponse`。关联号必须在建立 provider 及响应头回调作用域前完成校验。
- 官方固定版本 .NET 10.0.0：[ConfigurationBinder.cs](https://raw.githubusercontent.com/dotnet/runtime/v10.0.0/src/libraries/Microsoft.Extensions.Configuration.Binder/src/ConfigurationBinder.cs)，方法 `BindProperties`，选项 `BinderOptions.ErrorOnUnknownConfiguration`。严格 binder 可替代手工原始配置键扫描，但不能只做绑定后范围校验。
- 本次只读复核未重新运行产品测试。此前提及的 `buildspec.yml` 修改和未跟踪 `nul` 来自 `E:\code\exchange\exchange` 工作树历史，不能移植为 abp_admin 的当前状态，也不能据本次未见而断言它们历史上不存在。


## hy4

已完整读取 `client-error-handling-development-plan-review-hy4.md` 第 1–254 行并独立核对本地源码及 ABP 10.7.0 官方源码；未读取另两份审核报告，未运行测试。本节编号 H01–H37 是处置 ID，“原 ID”保留 hy4 自身编号。报告行号均指 hy4 原报告；对原计划的判断标明“v1.0”，不作为修订后计划的现状证据。

路径均相对仓库根 `E:\code\abp_admin`，缩写如下：`FW`＝`backend/framework-app/`；`HOST`＝`FW/AbpAdmin.HttpApi.Host/`；`APP`＝`FW/AbpAdmin.Application/`；`DOMAIN`＝`FW/AbpAdmin.Domain/`；`TG`＝`backend/modules/platform/AbpAdmin.TokenGateway/`；`CANVAS`＝`C:\Users\Administrator\.cursor\projects\e-code-abp-admin\canvases\client-error-policy.canvas.tsx`。`FW` 等缩写递归展开即准确源码路径。

| ID / 原 ID | 报告行 | 处置 | 独立核实、修订要求及拒绝理由 |
| --- | --- | --- | --- |
| H01 / B1 | 54–70 | 吸收核心风险，纠正链路归因 | MVC、隐式ABP中间件和手写链均可能返回未经批准Data；converter还可能把Data插入message，此核心结论已进入计划。不能仅凭构造点证明具体异常必经Friendly，但这不否定信息输出风险。证据：HOST/FriendlyBusinessExceptionMiddleware.cs:84–92、DOMAIN/Sms/AliyunSmsSender.cs:86、APP/VirtualFileExplorer/VirtualFileExplorerAppService.cs:56和本记录O1/O2。TokenGateway已有固定错误映射（TG/Core/UpstreamErrors.cs:28–34），按独立协议登记；受权管理诊断DTO（APP/Monitoring/CacheMonitorAppService.cs:25）不自动等同客户泄漏。第二轮纠正原记录把链路限定写得像否定默认Data事实的歧义。 |
| H02 / B2 | 72–80 | 部分吸收 | 补稳定登记键、装配覆盖、双向漂移检测。现有 `backend/test/AbpAdmin.HttpApi.Host.Tests/Authorization/AnonymousEndpointSweepTests.cs:222–239、246–260` 已示范 endpoint 枚举和反向存在性断言。拒绝仅用 `(ServiceType, MethodName)` 作完备主键：重载、别名、非服务控制器、框架协议需区分；采用逻辑 ID＋声明类型/方法签名＋实际 HTTP method/route 映射。ActionDescriptor 不能覆盖 inline middleware/SSE写出；清单允许独立协议/本轮非目标登记，不把全部 Razor/静态资源强塞 ABP 三类策略。运行时未知默认 Public 与 CI 要求明确登记并不矛盾。 |
| H03 / B3 | 82–91 | 吸收，细化并行顺序 | 真实HTTP夹具前置P1，复用HostUnderTest.cs:69–109；HostTestBed.cs:11–15只初始化且有静态生命周期约束。P2/P3纯逻辑在契约冻结后可先写，集成及全局替换门禁等待真实基线；这只是对报告“夹具冻结后并行”的工作粒度细化，不再称其要求过度串行。第二轮保留真实管线前置并将非Development安排于发布前独立进程。 |
| H04 / B4 | 93–101 | 吸收 | `HOST/ExtensionGrants/ImpersonationTokenExtensionGrant.cs:130–133` 确认 `ex.Code ?? ex.Message` 进入 OAuth描述，需纳入安全映射。`HOST/Identity/IdentitySessionValidationMiddleware.cs:57–62` 是另一种401体。保持OAuth协议，两类401统一为既有 authentication。拒绝为 session_revoked 强增独立 kind 的必要性：可用安全 reason/code细分，避免无必要重开已定模型。 |
| H05 / M1 | 107–111 | 吸收落点要求，选择已有诊断能力 | 报告提供复用/新建及明确非目标的选择，不是无条件强制SLA。计划复用APP/AuditLogs/AuditLogAppService.cs:36授权、:807–809关联信息、APP/AuditLogs/AuditLogQueryExtensions.cs:54检索、web/src/pages/audit-logs/index.tsx:59–66深链及DOMAIN/Logging/LogRetentionOptions.cs:7–19。未覆盖的完整诊断查受控服务端日志；不承诺审计DTO包含全部堆栈。新增客服SLA和编号有效期不选，本期非目标已写明。纠正上轮强制交付措辞，D3并未搁置。 |
| H06 / M2 | 113–119 | 吸收范围显式化 | 用户D2/D6已选择随版本发布的客户文案；报告要求把不支持租户覆盖明确登记，现已写入非目标。DOMAIN/Localization/DbLocalizationResourceContributor.cs:47–71及AbpAdminDomainModule.cs:177–179说明现有租户动态能力，客户资源需跳过。管理资源独立入口并采用private/no-store或隔离后的缓存键；不选同URL切视图的条件方案。不再把要求显式登记误写成必须重开产品决定。 |
| H07 / M3 | 121–129 | 吸收，补授权及HasStarted限制 | 采纳override ShouldHandleException和base完成日志/通知后替换context.Result。官方filter的HandleAndWrapException与中间件private包装见[S1][S2]；新版补非授权、未开始响应等前置条件。报告说的是替换Result，并未要求无条件二次写body，删除原记录对该未提出方案的拒绝措辞。 |
| H08 / M4 | 131–135 | 吸收 | 客户纯字符串失败映射OperationFailed及静态文案，历史任务关联号缺失可空；DOMAIN/ScheduledJobs/ScheduledJobScheduler.cs:380、APP/Monitoring/HttpProbeJobHandler.cs:124是盘点样本。原报告没有要求全量改写管理DTO，删除该拒绝对象；维持显式客户DTO投影及管理诊断原权限边界。 |
| H09 / M5 | 137–143 | 部分吸收 | 两套快照职责吸收。`backend/test/AbpAdmin.Application.Tests/Monitoring/FrontendContractSnapshotTests.cs:39、95` 是SeedTypes及更新变量。框架ClientFailureDto应进入既有快照；模板DTO拒绝通过框架测试反向引用业务模块来统一登记：该测试`:126、159、197–199` 只递归首个种子程序集，`backend/test/AbpAdmin.Application.Tests/AbpAdmin.Application.Tests.csproj` 无Biz.Template引用。模块DTO由模块自有快照/测试或宿主组合校验。拒绝强加运行时版本协商/原子同步部署；采用增量兼容、未知新码兜底、破坏性变化另版本化。静态目录缺失/损坏构建失败，运行时不可信响应安全兜底。 |
| H10 / M6 | 145–153 | 吸收策略要求，首期明确同源 | 补标准URL解析、credentials:same-origin、跨源拒绝、CSRF/身份头不外送及未来CORS接入边界；源点web/src/requestErrorConfig.ts:156–175、HOST/AbpAdminHttpApiHostModule.cs:1136–1148。报告要求定义策略，未要求首期完成跨源产品，删除原误述。userinfo/歧义路径与合法编码查询分开；预检失败仅安全网络提示，none统一。 |
| H11 / M7 | 155–159 | 吸收，选择报告的隔离回归分支 | 全局filter/资源服务替换不能用关闭样例回退；全局兼容故障整版回滚，管理回归与替换提交同过。报告给出影子比较“或”隔离环境全量回归，新版采用后者；不再把未选影子模式表述为拒绝强制框架建设。 |
| H12 / M8 | 161–165 | 吸收并限定适用组合 | 报告提出的是出口×错误类型两维，不是四维全量笛卡尔积。按ExitId×适用错误类型自动生成用例，不适用项说明原因，另做语言完整性。结构白名单和合成敏感标记为主、SQL/路径正则补充，新增写出候选漂移门禁；纠正上轮扩大维度后再拒绝的表述。 |
| H13 / M9 | 167–171 | 部分吸收 | `web/src/requestErrorConfig.ts:117`确读body等待秒数；`HOST/OpenIddict/TokenEndpointRateLimitingMiddleware.cs:107–120`只有Retry-After头和error/error_description的JSON，其本身不能证明存在两个等待值。双源优先级问题仍成立。最终选择合法header优先、批准body兜底，冲突不拼值；不选报告的header唯一源，是为兼容合法body-only响应而不采纳任意body原文。保留管理旧字段及503头；HTTP-date和非负秒数均按[S3]校验。 |
| H14 / F1 | 179 | 吸收归因澄清，第二轮更正 | 原canvas把两个仓库的工作区状态合写，buildspec.yml与nul来自E:\code\exchange\exchange，而非abp_admin。第二轮定向核实了该来源；不能由abp_admin当前未见这些文件推断原描述凭空捏造或历史不存在。文档改为按仓库区分并要求执行前重新查状态，业务文件保持原样。 |
| H15 / F2 | 180 | 吸收 | `docs/framework-contracts.md:70`是404静默，`:76–77`才是监控原始异常取舍；`CANVAS:80`合并引用不精确，拆分或精确说明。 |
| H16 / F3 | 181 | 吸收 | `web/src/requestErrorConfig.ts:73`是配置开始，`:93–97`是真正validationErrors/details/message优先级。原计划事实依据锚到93或函数名，属于引用精度改善，不是设计前提错误。 |
| H17 / F4 | 182 | 拒绝事实结论，吸收措辞精确化 | 当前生成runtime确有临时挂共享实例：`web/src/.umi/plugin-request/request.ts:144–147`复用axios，`:188–216`挂每请求interceptor，`:222–235`完成后eject；`.umi-production/plugin-request/request.ts`同位置亦有。报告只查业务静态配置、漏查runtime。保留独立fetch论据，精确称“当前Umi 4.7.24生成runtime的每请求拦截器行为”，不改.umi。 |
| H18 / F5 | 183 | 部分吸收 | `.NET10/ABP10.7.0`由`backend/Directory.Packages.props:15`、`.github/workflows/ci.yml:33`确认；源目录未见global.json/Directory.Build.props。拒绝仅为本任务新增永久ABP==10.7.0断言：版本已集中声明，绝对锁死妨碍升级。行为/装配契约测试随升级回归、记录核验基线即可。 |
| H19 / F6 | 184 | 吸收 | `web/package.json:12、28`确认lint含tsc；`.github/workflows/ci.yml:94–97`只有tsc/test，无前端lint/build。计划明确未来增加相关CI检查，或必须保存构建产物验收证据；不能声称当前CI已证明生产示例剔除。 |
| H20 / F7 | 185 | 部分吸收 | 测试声明计数不证明运行通过，但审核人未跑也不能推翻此前实际基线。当前`web/src/requestErrorConfig.test.ts`是20个普通it＋参数化2个（it.each在323行），22项合理。补执行日期、命令、结果来源；未保存日志文件则写规划会话工具输出，不编造输出路径。 |
| H21 / F8 | 186 | 部分吸收 | 接收跨机器可移植性目标；本次桌面交付按当前文件链接约定保留绝对可点击路径，同时文首声明仓库根、清单提供相对根路径。若另导出普通仓库Markdown，应从文档所在目录计算链接，例如`../backend/AGENTS.md`；不能照抄`backend/AGENTS.md`使docs下链接指错。exchange仍属仓外参考。本轮不把绝对路径一律改掉，避免破坏当前交付入口。 |
| H22 / F9 | 187 | 吸收 | v1.0 §5.1文件清单中Domain.Shared模块/csproj省略前缀，其他行是仓库根相对路径；统一完整仓库根相对文件清单。 |
| H23 / F10 | 188 | 部分吸收 | “尚无受版本控制的交付物”属实：方案/报告未跟踪，canvas在仓外。但六项决定已经写在仓内v1.0 §1.2，无须再造重复决策文件。吸收后续将方案与处置记录纳入同一文档提交的交付建议；不采纳把当前自动Git提交设为完成前提，用户本轮要求核实和改文档，没有要求提交。不能把报告原意误写成“六项决定只在canvas”。 |
| H24 / F11 | 189 | 吸收 | v1.0抬头“可据此开发”可误读；`CANVAS:170`明确仅确认规划。改“设计定稿，本文提供实施步骤；本次仅更新文档，实施需另行授权”，任务标明授权后执行；不重新询问D1–D6。 |
| H25 / H01 | 201 | 部分吸收 | 按H02建立装配＋非MVC出口登记，保留独立协议/非目标明确记录，不将全部Action/Razor强改ABP分类。`backend/test/AbpAdmin.HttpApi.Host.Tests/Authorization/AnonymousEndpointSweepTests.cs:222–239`可复用枚举套路。 |
| H26 / H02 | 202 | 吸收 | OAuth grant无码BusinessException合成夹具成立，明确覆盖`HOST/ExtensionGrants/ImpersonationTokenExtensionGrant.cs:133`，同时避免内部码原样泄露。 |
| H27 / H03 | 203 | 部分吸收 | 两形状401统一authentication＋并发一次恢复成立，源点`HOST/Identity/IdentitySessionValidationMiddleware.cs:57–62`。“原头不进客户响应”仅在指敏感头时吸收；若指所有协议头均删则拒绝，WWW-Authenticate/Retry-After等须保留。 |
| H28 / H04 | 204 | 吸收测试意图 | 用真实生产异常形状的合成Data覆盖实际MVC与中间件出口，源点同H01；不声称每个例子原本都必经Friendly中间件，不修改原Data来获得测试通过。 |
| H29 / H05 | 205 | 吸收，限客户DTO | 无码字符串不公开原文，保留原任务执行关联号；源点`DOMAIN/ScheduledJobs/ScheduledJobScheduler.cs:380`、`APP/Monitoring/HttpProbeJobHandler.cs:124`。管理监控DTO仍按既有批准边界，不全量改造。 |
| H30 / H06 | 206 | 部分吸收 | 保留TokenGateway独立协议回归和客户ABP归一器不误认其信封的断言；`TG/Core/UpstreamErrors.cs:28–34`已安全收敛，拒绝宣称当前网关必泄漏，拒绝强制统一信封。 |
| H31 / H07 | 207 | 吸收 | 细化既有A10/A11，可合并不重复编号；官方filter.ShouldHandleException与middleware.InvokeAsync支持，见[S1][S2]。HasStarted不得追加JSON。 |
| H32 / H08 | 208 | 部分吸收 | origin、userinfo、歧义路径和凭证不外送用例成立；按userinfo/歧义路径测试，不把合法含@/%查询一刀切。跨源未支持时验收明确拒绝即可，不强制完整跨域认证建设。现有CORS落点`HOST/AbpAdminHttpApiHostModule.cs:1136–1148`。 |
| H33 / H09 | 209 | 部分吸收 | 静态快照缺失/损坏应构建失败，运行时未知码安全兜底；两套快照按H09分工，模板DTO不反向塞框架测试。具体递归边界`FrontendContractSnapshotTests.cs:126、159、197–199`。 |
| H34 / 附加关联号及配置 | 211、220 | 部分吸收 | 关联号在`HOST/AbpAdminHttpApiHostModule.cs:1427`的UseCorrelationId前验证/生成，投影仅读既定值；加受权检索断言。拒绝将熵下限/有效期作为请求字符串可测性质或授权保障，编号不承担授权；安全随机生成＋格式长度校验即可。原始IConfiguration子键校验吸收，单靠Options属性无法发现未知宽松键；v1.0 §5.4已要求非法宽松字段失败，补具体落点。 |
| H35 / 建议P0六门禁 | 217–222 | 部分吸收 | 出口/登记/诊断三清单与夹具前置按H01–H03吸收。SSE/Hub/200字段先登记适用性和是否本轮接入，拒绝扩大成全仓通信协议改造。文档版本化交付可保留，本轮不自动Git提交，理由见H23。 |
| H36 / 建议保持不动 | 230–237 | 已覆盖 | ABP/OAuth形状、converter不替换、原对象不变、private方法事实、公共资源固定公开、不全局替换成功JSON、不增错误表/资金幂等、管理404均在v1.0 §3、§4、§5.3、§6、§7.3、§8有明确约束，修订版保留。官方依据[S1][S2]及审计converter共用事实[S4]；不包装成新需求。 |
| H37 / 最终放行条件 | 247–252 | 吸收已核实的质量条件 | 报告提出建议放行条件属于正常审核，不产生实施授权，也不是擅设审批边界。已核实的管线、登记、管理回归和文档交付进入计划；未核实或不适用部分按具体条目处置。当前只改文档不提交Git，实施首个文档提交须纳入方案、处置及引用报告。纠正上轮将建议质量门禁等同新增审批的措辞。 |

必要补充：

- 静态客户资源隔离是已确认设计的执行项，不重开产品决策。`DOMAIN/AbpAdminDomainModule.cs:169–180` 全局加数据库贡献者，`APP/Localization/LanguageTextAppService.cs:269–304`仍能写覆盖，必须同时保留接线排除和写入拒绝；恢复默认允许清理历史覆盖。
- 管理专用资源优先采用独立受权端点及private/no-store；不得为缓存方便增加Shared接口的audience参数后向管理员放宽，共享固定公开的决定保持不变。
- hy4关于威胁边界的总评（报告38行）可通过说明“可信服务端登记/授权、匿名与客户默认公开、有效管理员凭证仍需原权限”补清。不能借“被盗管理员token”扩大为本轮新建身份安全产品；错误投影本身不能证明凭证没有被盗。

[S1] [ABP 10.7.0 AbpExceptionFilter.cs](https://raw.githubusercontent.com/abpframework/abp/10.7.0/framework/src/Volo.Abp.AspNetCore.Mvc/Volo/Abp/AspNetCore/Mvc/ExceptionHandling/AbpExceptionFilter.cs)：核对 `ShouldHandleException`、`HandleAndWrapException`、`LogException`，确认可重写钩子、日志与响应对象共用、授权分支另行处理。

[S2] [ABP 10.7.0 AbpExceptionHandlingMiddleware.cs](https://raw.githubusercontent.com/abpframework/abp/10.7.0/framework/src/Volo.Abp.AspNetCore/Volo/Abp/AspNetCore/ExceptionHandling/AbpExceptionHandlingMiddleware.cs)：核对 `InvokeAsync` 与私有 `HandleAndWrapException`，确认HasStarted/非ObjectResult放行条件，不能假设可override包装方法。

[S3] [RFC 9110 §10.2.3 Retry-After](https://www.rfc-editor.org/rfc/rfc9110.html#name-retry-after)：允许HTTP-date或非负整数delay-seconds。

[S4] [ABP 10.7.0 AuditLogInfoToAuditLogConverter.cs](https://raw.githubusercontent.com/abpframework/abp/10.7.0/modules/audit-logging/src/Volo.Abp.AuditLogging.Domain/Volo/Abp/AuditLogging/AuditLogInfoToAuditLogConverter.cs)：`ConvertAsync`也调用`IExceptionToErrorInfoConverter`，HTTP公开策略不能通过全局替换该接口污染审计记录。


## Qwen

已完整读取原报告，未读取另两份审核报告。以下编号 Q01—Q32 固定用于追踪处置；原报告行号指 `docs/client-error-handling-development-plan-review-qwen.md`。计划章节引用均以被审 v1.0 为历史基线，不能用于定位更新后行号。审核意见不是事实证据；每项结论重新对照当前仓库或 ABP `10.7.0` 固定标签源码核实。

路径缩写：`R` = `E:\code\abp_admin`；`B` = `R\backend\framework-app`；`T` = `R\backend\test`；`M` = `R\backend\modules`；`W` = `R\web`；`X` = `E:\code\exchange\exchange`。下表中的源码行号来自本轮只读取证。官方源码引用使用文末固定版本链接和方法名，行号由主报告统一校准。

| ID / 原报告编号 | 原报告行 | 处置 | 独立核实、理由与计划处理 |
| --- | --- | --- | --- |
| Q01 / P0-1 | 30、225 | 部分吸收 | 补齐 `B\AbpAdmin.HttpApi\Controllers\FileShareController.cs:27-40`、`GdprDownloadController.cs:48-57` 两个匿名下载出口，覆盖异常、显式404、限流和文件已开始情况。前者非限流业务异常显式返回404，后者无catch。**拒绝“它们是唯一整包下发Data路径”**，ABP业务异常默认也可能携带Data，见Q10。 |
| Q02 / P0-2、P1-5 | 31、138-140、226 | 部分吸收 | 复算 `B\AbpAdmin.Domain.Shared\Localization\AbpAdmin\*.json`：20个文件；en/zh-Hans各158个 `AbpAdmin:` 键；zh-Hant仅4个总键。明确完整性检查只作用于新客户静态资源及其声明支持文化，不要求迁移所有旧管理资源。**拒绝“现计划必导致宿主启动失败”**：v1.0第6节已单建客户资源，旧资源还存在默认英语回退。语言包稀疏是工作量输入，不是启动必败证据。 |
| Q03 / P1-1 | 32、112、227 | 吸收并收紧接线 | `B\AbpAdmin.HttpApi.Host\AbpAdminHttpApiHostModule.cs:1516` 调用UseUnitOfWork；官方[管线扩展][abp-pipeline]的 `UseUnitOfWork` 隐式安装异常中间件，应改为确定事实。官方[异常中间件][abp-middleware]的HandleAndWrapException为private，但InvokeAsync可覆写；[AbpMiddlewareBase][abp-middleware-base]实现IMiddleware。因此可替换原具体服务并覆写InvokeAsync，保留原管线位置。**拒绝含糊的“外层投影已写出的响应”作为实现方案**：不能依赖原中间件不接管，也不能引入全量HTTP缓冲重写。增加唯一writer和实际执行顺序验收。 |
| Q04 / §4.1 filter扩展位 | 101-102 | 吸收 | 官方[MVC注册][abp-mvc-options]的AddActionFilters通过AddService注册AbpExceptionFilter；[filter][abp-filter]的ShouldHandleException、HandleAndWrapException、LogException都是protected virtual。计划明确 `Replace(ServiceDescriptor.Transient<AbpExceptionFilter, ClientErrorExceptionFilter>())`，测试原服务键解析到新实现，不并列新增第二个filter。“扩展行为”不构成错误API断言，但改写为“可覆写方法”更准确。 |
| Q05 / P1-2 | 113、227 | 部分吸收 | `B\AbpAdmin.HttpApi.Host\AbpAdminHttpApiHostModule.cs:868-893`证明cookie事件这条路径对/api只写401/403状态、不写body，须纳入响应生产者及验收。**拒绝泛化成所有API的401/403均由cookie事件决定**：同文件:833将Bearer转给OpenIddict，会话/租户守卫也自行输出。保留ASP.NET IAuthorizationMiddlewareResultHandler与ABP IAbpAuthorizationExceptionHandler两种适配；逐路径验证状态、协议头、公开信封、Razor跳转及不重复写入。ABP包未直接引用ASP.NET接口不否定该扩展位。 |
| Q06 / P1-3 | 114 | 吸收约束，拒绝绝对化 | `B\AbpAdmin.Domain\AbpAdminDomainModule.cs:177-179`逐资源追加数据库贡献者，必须跳过客户标记资源。官方[localizer factory][abp-localizer-factory]的CreateStringLocalizerCacheItem/Async还会追加GlobalContributors。加入“不将数据库贡献者改为全局注册”和“创建localizer后客户资源仍无数据库贡献者”的负向测试。**拒绝唯一可行/不存在任何排除点的绝对表述**：贡献者本身也能实现判定，本期只明确采用当前接线排除。 |
| Q07 / P1-4 | 120-125、228 | 部分吸收 | 固定PublicKind枚举和前端kind映射。官方[状态finder][abp-status]的GetStatusCode将业务失败与已认证授权失败都可能映射403，不能仅凭403覆盖已识别公开码。**拒绝“一律禁止根据HTTP状态推导kind”**：空体/非ABP的401、403、404、429仍需安全的本地分类和身份/等待动作。明确公开码分类优先，受控协议其次，状态只作安全兜底；网络、取消、超时来自本地传输判定。 |
| Q08 / P2-1 | 127-129 | 部分吸收 | 增加Controller/FileResult/非JSON Accept的NotFound用例。**拒绝“Controller的EntityNotFound一律500、只有AppService才404”**：`B\AbpAdmin.HttpApi\Controllers\AuditLogExportController.cs:79-84`是特定下载链历史注释及显式404；官方[filter][abp-filter]的ShouldHandleException也接受JSON Accept/Ajax，官方[状态finder][abp-status]会将EntityNotFound映射404。公开404还须遵守端点既定隐藏资源规则，不输出实体类型或ID。 |
| Q09 / P2-2 参数及原文 | 131-135 | 部分吸收 | 限流异常可作Data与插值夹具：`B\AbpAdmin.Domain\RateLimiting\OperationRateLimitingChecker.cs:272-286`写PolicyName、计数、RuleDetails并复制ExtraProperties；`B\AbpAdmin.HttpApi.Host\FriendlyBusinessExceptionMiddleware.cs:91`直接下发Data。分别验证消息插值参数和回传字段白名单。**拒绝“真正泄露只在Data不在message”及“现有文案全部安全”**：官方[converter][abp-converter]的TryToLocalizeExceptionMessage会用Data替换占位符；若批准参数来源不受控，message同样可能泄露。 |
| Q10 / P2-2 ABP不发Data | 106、136、240① | 拒绝错误断言，吸收更严格测试 | 官方[异常选项][abp-exception-options]构造器默认 `SendExceptionDataToClientTypes=[typeof(IBusinessException)]`；[converter][abp-converter]的CreateErrorInfoWithoutCode命中后直接赋exception.Data。`B\AbpAdmin.Domain.Shared\RateLimiting\AbpAdminOperationRateLimitingException.cs:16`继承BusinessException。仓库无显式配置不等于默认列表为空，**“原生路径无Data、管理429默认拿不到等待秒数”错误**。测试原生filter、中间件、手写出口均过滤未批准Data，并按管理旧形状保留批准等待字段。 |
| Q11 / P2-2 繁中缺键 | 134 | 拒绝确定性断言，吸收缺键防护 | `B\AbpAdmin.Domain.Shared\AbpAdminDomainSharedModule.cs:65`将AbpAdminResource默认文化设为en；官方[本地化选项][abp-localization-options]默认开启文化回退，[dictionary localizer][abp-dictionary-localizer]的GetLocalizedStringOrNull会查默认文化。**繁中缺错误键并不必然返回内部键**。只有整个回退链缺失才有该风险；手写中间件:74确无ResourceNotFound保护。增加完整回退链缺键测试，不把“zh-Hant必泄漏”写成现状。 |
| Q12 / P1-6 规模与批量策略 | 146-159、230 | 部分吸收 | 当前扫描有64个带Authorize的AppService文件，可作为盘点量级输入。权限前缀可生成候选清单，**不能自动判为AdminCompatibility，也不能只审前缀例外**；仍须核实每条服务/方法真实授权组合。维持未登记默认Public、先完成管理登记再开放客户入口的顺序。权限串近似计数不作为验收门槛。 |
| Q13 / P1-6 控制器计数 | 150-151 | 拒绝并修正遗漏 | **“模块控制器0、全动态API”错误**，目录glob漏掉 `M\platform\AbpAdmin.TokenGateway\Http\` 下AuthController、WalletController、ChatController、AdminPlansController四个具体Controller。ChatController:127-172已有SSE错误帧；GatewayControllerBase:25-45已有专用错误协议；AdminPlansController:46-48手写返回。HttpApi目录8个Controller文件还包含 `B\AbpAdmin.HttpApi\Controllers\AbpAdminController.cs:8` 抽象基类，具体为7个。盘点按路由、继承、输出点，不能依赖Controllers目录名；纳入这些既有协议，保持OpenAI兼容/SSE形状。 |
| Q14 / P1-6 dry-run | 160 | 拒绝 | 开发/测试环境对未登记端点“只记日志不改响应”保留了未知原文，违反D1、默认Public和Development也安全；叫“管理兼容”不能证明调用者已获管理资格。可做离线登记预览、影子分类计数、旧版本隔离基线采样，**不得把它变成新机制的原文透传模式**。 |
| Q15 / P1-6 可观测性 | 161 | 吸收 | 增加未分类、资格解析失败、公开目录缺码的计数和受权排查入口。标签限稳定端点标识、分类原因等低基数非敏感字段；不得用异常原文、token、URL参数作标签。该项是实现要求，不伪称现有能力。 |
| Q16 / P1-6 SettingUi | 162 | 部分吸收 | `B\AbpAdmin.Application\Settings\AbpAdminSettingUiAppService.cs:52-55`具Replace、ExposeServices、RemoteService(false)、类级读权限；:122/:198叠加写权限。必须测试服务代理授权，不能仅看控制器元数据。**拒绝将上游/历史匿名问题描述成本仓当前匿名可达**；本仓已有替换服务和宿主默认拒绝。“唯一现成反例”无须采纳。 |
| Q17 / §7.1 exchange多层兜底 | 46、168-170 | 部分吸收 | 准确补充源项目i18n映射、重点码英文备用、通用兜底、管理码硬屏蔽。`X\Web-H5\src\lib\tradeErrorMessage.ts:79-99`实际CODE_EN_FALLBACK为**15码，不是17码**；en映射171键、后端E枚举587项复算成立。**不采纳新框架必须复制四层的暗示**；“未知错误统一兜底”原概括本身正确。 |
| Q18 / §7.2 全HTTP200 | 172-176 | 部分吸收，拒绝全部化 | 可明确只吸收机器码与文案分离，不迁移Furion包装和HTTP200业务失败约定。**不能由某个JsonResult未设StatusCode证明所有业务失败都是200**；`X\Admin.NET\Admin.NET.Core\Utils\AdminResultProvider.cs:60-79`仍有SetResponseStatusCodes及401/403处理，H5 request.ts:163-175处理非2xx。v1.0第3.1节与A09已禁止统一改200，补来源边界即可。 |
| Q19 / §7.3 新增能力 | 178-180 | 已覆盖，可补来源说明 | `X\Web-H5\src\api\request.ts:153-181`无专门429/取消/超时分类；timeout为20秒；tradeErrorMessage.ts:109-112异常仅附code。新计划的取消、结果未知、等待分类是框架增量，不能说已经由exchange验证。无须改变设计。 |
| Q20 / P1-7 两类快照 | 186-191、229 | 吸收，保持依赖方向 | `T\AbpAdmin.Application.Tests\Monitoring\FrontendContractSnapshotTests.cs:29-39/:83/:95`负责DTO形状和nullability；公开目录快照负责码、分类及字段允许范围，二者不可互代。ClientFailureDto加入既有SeedTypes；Biz.Template客户DTO在模块测试复用同一快照规范，**不为集中SeedTypes让框架Application.Tests反向引用业务模块**（其csproj:12-15现无该引用）。新变量统一为UPDATE_PUBLIC_ERROR_CONTRACT，与正文一致；此前本表误写PUBLIC_ERROR_CONTRACT_SNAPSHOT_UPDATE，第二轮已纠正。两类刷新命令同节说明。前端纯本地错误对象不是后端DTO，不登记为C#契约。 |
| Q21 / P2-3 Host装配 | 193-196 | 部分吸收 | `T\AbpAdmin.HttpApi.Host.Tests\HostTestBed.cs:43-45`可提供真实DI和端点表；真实装配断言与最小HTTP字节夹具互补。**纠正报告断言形状**：替换DI后MvcOptions里的ServiceFilter.ServiceType仍是AbpExceptionFilter，断原注册仅一个并解析到新实现，不能要求列表直接变成子类。中间件顺序不能由MvcOptions证明，须共享实际注册函数并以执行探针/行为测试验证。元数据覆盖检查限定受控API及协议入口，不要求静态文件等每个端点都带客户分类。 |
| Q22 / P2-4 CI范围 | 198-200、231 | 吸收 | `R\.github\workflows\ci.yml:29/:39/:43/:53-66/:72/:95/:97/:106`确认restore闭包、restore、工具恢复、7套EF模型漂移、后端全测、tsc、前端测试及双镜像构建。文档列完整CI检查和运行目录，注明CI不跑lint；本地lint已含tsc，无需重复。模型漂移使用CI占位连接配置，不访问真实库。 |
| Q23 / P2-4 Vitest环境 | 201 | 吸收 | `W\vitest.config.ts:13`默认happy-dom；纯TS测试明确使用文件头 `// @vitest-environment node`，不改全局配置。**不采用报告另提的environmentMatchGlobs路线**，当前Vitest4无需引入该配置分支。 |
| Q24 / P2-5 skip差异 | 203-205 | 吸收 | `W\src\requestErrorConfig.ts:87`和requestErrorConfig.test.ts:84保留管理侧原始rethrow；客户skip只关展示，归一、身份协调及安全reject照常。明确本期不顺手统一两条传输。 |
| Q25 / §3.1引用精度 | 80-86、245 | 吸收 | 优先级证据改指 `W\src\requestErrorConfig.ts:94-97`，403另在:103-107；手写异常guard在 `B\AbpAdmin.HttpApi.Host\FriendlyBusinessExceptionMiddleware.cs:60-65`，信封:84-94，Data:91。canvas当前确引:92和:60。**入口指针偏粗不是机制错误**，更新精确依据即可。 |
| Q26 / Retry-After口径 | 90 | 部分吸收，明确不同选择 | 现有管理429读取 `W\src\requestErrorConfig.ts:117` 的error.data.RetryAfterSeconds；503由 `B\AbpAdmin.HttpApi\Imaging\ImageProcessingBusyExceptionFilter.cs:20`写HTTP Retry-After，均保留。**最终新客户归一规则采用合法HTTP Retry-After优先，批准body字段兜底；冲突不拼接、不相加；非法/不可见header才落body，二者均无合法值则不猜等待时间。** 这与报告建议body优先不同：标准header可覆盖非ABP/网关响应和既有503，并保持协议一致；管理旧字段仍兼容。header的delta-seconds/HTTP-date须有界解析，客户body新字段按lowerCamelCase，不能直接复制管理PascalCase。**不采纳“503 header/data任一处可得即可”降低兼容要求**，原有503头仍须保留。 |
| Q27 / validationErrors结构 | 91 | 吸收 | 官方[converter][abp-converter]的GetValidationErrorInfos返回message和members字符串数组，members转camelCase；不是ValidationProblemDetails.errors字典。现有管理AbpError类型未声明members属实。明确客户形状并测试多成员、空成员、未知成员、嵌套公开字段和超限，不放宽字段白名单。 |
| Q28 / §9.1审计converter | 51、211 | 已覆盖，补直接依据 | 官方[审计转换][abp-audit]的ConvertAsync读取auditLogInfo.Exceptions，调用同一IExceptionToErrorInfoConverter后序列化入库。全局Replace会影响审计持久化，不能为响应投影全局替换；保留原对象与成对响应/诊断测试。 |
| Q29 / §9.4未知配置字段 | 214 | 吸收 | v1.0第5.4节已禁放宽字段，新增明确验收：**仅ClientErrorHandling节**的未知/宽松字段、非法限制值导致启动校验失败。**不扩大成整个宿主配置任何未知字段都拒绝**，避免无关配置兼容破坏。 |
| Q30 / §9.7 canvas表格 | 217 | 部分吸收 | 可将配置默认值、可调范围、不可配置内容并入Markdown同一表，使文档独立。canvas路径 `C:\Users\Administrator\.cursor\projects\e-code-abp-admin\canvases\client-error-policy.canvas.tsx:30`已有optionRows。**不把canvas作为规则事实源，也不把仅文档任务扩大成UI改造。** |
| Q31 / §2.1.10 Umi描述 | 55 | 吸收措辞 | `W\src\.umi\plugin-request\request.ts:192-236`证明安装版本支持将单请求拦截器临时挂共享实例；业务源码未发现此类调用。写成“当前Umi实现提供这种机制”，**不暗示业务正在使用**；独立fetch决定保持。 |
| Q32 / 其余正面确认和附加清单 | 47-54、61-74、209-216、225-233 | 已覆盖 | 分层引用、匿名settings裁剪、公开JSON/脚本、csproj单层嵌入、样例生产关闭、原对象不修改、固定版本与管理兼容方向成立。证据：`B\AbpAdmin.HttpApi\AbpAdmin.HttpApi.csproj:19-20`、`B\AbpAdmin.Domain\AbpAdmin.Domain.csproj:12`；匿名裁剪贡献者:42-44仅清setting；Domain.Shared.csproj:44与Biz.Template.csproj:38仅各自单目录JSON；`M\biz\AbpAdmin.Biz.Template\Entities\BizProject.cs:11`无IMultiTenant，Services\BizProjectAppService.cs:41-59不按创建者过滤，故隔离样例限制必要。报告103-104关于配置贡献者晚于Localization构建、脚本注入具体类成立，见[配置服务][abp-app-config]GetAsync及[脚本控制器][abp-localization-script]构造器。报告第10节重复事项归入Q01—Q31；摘要“44条/若干P0P1”是作者计数与评价，不作为新验收门槛。 |

必要边界：本轮只读核验未运行后端或真实HTTP测试。前端22项和exchange21项是本聊天此前实跑的现状基线，不能证明新机制已实现。原报告关于其自身独立性、取证时间指纹、全部复测和期间文件未变等过程声明，不由本轮源码检查代为背书。统计量不作为安全门槛；没有取证到的运行时结论仍须通过计划中的实际HTTP与装配验收。

[abp-pipeline]: https://raw.githubusercontent.com/abpframework/abp/10.7.0/framework/src/Volo.Abp.AspNetCore/Microsoft/AspNetCore/Builder/AbpApplicationBuilderExtensions.cs
[abp-middleware]: https://raw.githubusercontent.com/abpframework/abp/10.7.0/framework/src/Volo.Abp.AspNetCore/Volo/Abp/AspNetCore/ExceptionHandling/AbpExceptionHandlingMiddleware.cs
[abp-middleware-base]: https://raw.githubusercontent.com/abpframework/abp/10.7.0/framework/src/Volo.Abp.AspNetCore/Volo/Abp/AspNetCore/Middleware/AbpMiddlewareBase.cs
[abp-mvc-options]: https://raw.githubusercontent.com/abpframework/abp/10.7.0/framework/src/Volo.Abp.AspNetCore.Mvc/Volo/Abp/AspNetCore/Mvc/AbpMvcOptionsExtensions.cs
[abp-filter]: https://raw.githubusercontent.com/abpframework/abp/10.7.0/framework/src/Volo.Abp.AspNetCore.Mvc/Volo/Abp/AspNetCore/Mvc/ExceptionHandling/AbpExceptionFilter.cs
[abp-localizer-factory]: https://raw.githubusercontent.com/abpframework/abp/10.7.0/framework/src/Volo.Abp.Localization/Volo/Abp/Localization/AbpStringLocalizerFactory.cs
[abp-status]: https://raw.githubusercontent.com/abpframework/abp/10.7.0/framework/src/Volo.Abp.AspNetCore/Volo/Abp/AspNetCore/ExceptionHandling/DefaultHttpExceptionStatusCodeFinder.cs
[abp-converter]: https://raw.githubusercontent.com/abpframework/abp/10.7.0/framework/src/Volo.Abp.ExceptionHandling/Volo/Abp/AspNetCore/ExceptionHandling/DefaultExceptionToErrorInfoConverter.cs
[abp-exception-options]: https://raw.githubusercontent.com/abpframework/abp/10.7.0/framework/src/Volo.Abp.ExceptionHandling/Volo/Abp/AspNetCore/ExceptionHandling/AbpExceptionHandlingOptions.cs
[abp-localization-options]: https://raw.githubusercontent.com/abpframework/abp/10.7.0/framework/src/Volo.Abp.Localization/Volo/Abp/Localization/AbpLocalizationOptions.cs
[abp-dictionary-localizer]: https://raw.githubusercontent.com/abpframework/abp/10.7.0/framework/src/Volo.Abp.Localization/Volo/Abp/Localization/AbpDictionaryBasedStringLocalizer.cs
[abp-audit]: https://raw.githubusercontent.com/abpframework/abp/10.7.0/modules/audit-logging/src/Volo.Abp.AuditLogging.Domain/Volo/Abp/AuditLogging/AuditLogInfoToAuditLogConverter.cs
[abp-app-config]: https://raw.githubusercontent.com/abpframework/abp/10.7.0/framework/src/Volo.Abp.AspNetCore.Mvc/Volo/Abp/AspNetCore/Mvc/ApplicationConfigurations/AbpApplicationConfigurationAppService.cs
[abp-localization-script]: https://raw.githubusercontent.com/abpframework/abp/10.7.0/framework/src/Volo.Abp.AspNetCore.Mvc/Volo/Abp/AspNetCore/Mvc/Localization/AbpApplicationLocalizationScriptController.cs


## 第二轮勘误说明

第二轮独立核实后更正H01、H03、H05—H08、H10—H12、H37的归因或标签：报告提供的可选分支按所选分支吸收，不再写成拒绝它没有提出的强制方案。Q20更新变量保留正文UPDATE_PUBLIC_ERROR_CONTRACT；文档版本化交接进入实施P0门禁，当前仍不执行Git提交。逐项证据与第二轮新建议见[第二轮审核处置](./client-error-handling-development-plan-review-disposition-r2.md)。
