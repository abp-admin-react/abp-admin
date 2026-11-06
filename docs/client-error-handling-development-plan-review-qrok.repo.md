# 客户端错误处理开发方案二次审核报告（qrok）

| 项 | 内容 |
| --- | --- |
| 审核对象 | `docs/client-error-handling-development-plan.md` 版本 1.1（2026-11-05） |
| 对照 | `docs/client-error-handling-development-plan-review-disposition.md`；上一份 Grok 报告；仓库源码与 ABP 10.7.0 固定标签 |
| 审核日期 | 2026-11-05 |
| 结论 | **通过，可作为实施基线。** D1–D6 不重开。上一轮四条门禁已写入 v1.1，并经源码复核成立。处置记录拒绝的三项原 Grok 断言，复核后不再坚持。 |
| 仍要补的口径 | 两句执行范围，见第 4 节。不改变产品决定，也不构成另一次方案否决。本文不授权改业务代码。 |

## 1. 总评

v1.1 把上一轮「有条件通过」里缺的落点补进了正文：宿主最终装配的登记门禁、管理端先过滤再插值、关联号在 `UseCorrelationId()` 入口定一次、`ClientErrorHandling` 原始配置节严格绑定。随后三份审核合并进来的事实也站住了，尤其是 `UseUnitOfWork()` 已经安装 `AbpExceptionHandlingMiddleware`，以及 ABP 默认把 `IBusinessException` 的 Data 发给客户端。

当前正文、验收矩阵 A01–A34 和 P0/P1 的顺序可以交给执行者。第 4 节的两句是为了防止「原协议」和「401 冲突组合」被按字面收窄，建议在开工时直接补进第 5.3 节和第 7.2 节，不必再开一轮产品确认。

## 2. 核对方法

全文阅读 v1.1 与处置记录，再核对会改变结论的事实。未重跑测试，未访问生产，未改代码。

已核对并与 v1.1 一致的事实：

- ABP 10.7.0 `UseUnitOfWork` 先调用 `UseAbpExceptionHandling()`，再用工作单元中间件。本仓 `AbpAdminHttpApiHostModule` 在友好异常中间件之后调用 `UseUnitOfWork()`。内层中间件对 `IsObjectResult` 的异常直接写出转换器结果，不会冒泡到外层。`HandleAndWrapException` 为 private。v1.1 把替换该中间件写成必做项，正确。
- `AbpExceptionHandlingOptions` 构造函数把 `SendExceptionDataToClientTypes` 设为 `IBusinessException`，`SendExceptionsDetailsToClients` 默认为 false。仓库没有覆盖这项配置，不能推断 Data 默认不返回。
- `AbpCorrelationIdMiddleware.GetCorrelationIdFromRequest` 只在头为空时生成新值，不校验字符集；响应头在 `OnStarting` 里回写同一个值。入口先改成合法单值，再交给这个中间件，响应、日志、审计才能是同一个数。
- `UserFriendlyException` 构造点 49 处、11 个生产文件。其中带显式 `code:` 的是 12 处：`AuditLogAppService` 11 处，`GdprRequestAppService` 1 处。其余 37 处没有显式 code。带 code 的恢复失败仍把 `ex.Message` 插进本地化文案（`AuditLogAppService` 约 498、572 行）。
- `AbpAdminResource` 的默认文化是 `en`。繁中缺键会先走默认文化，不会仅因缺繁中文件就回显内部码。
- 已安装的 Umi 生成物 `web/src/.umi/plugin-request/request.ts` 会把单次请求的拦截器挂到共享 axios 上，完成后再卸下。这是 runtime 能力；业务层仍用静态错误配置。独立 fetch 的决定仍然成立。
- Cookie 的 `OnRedirectToLogin` / `OnRedirectToAccessDenied` 对 `/api` 只写 401/403，不写 body。`/signalr-hubs` 不在这个分支里。
- `ImpersonationTokenExtensionGrant` 在 `BusinessException` 上使用 `ex.Code ?? ex.Message`。同目录其余 grant 目前是固定英文字符串。
- `FileShareController` 对非限流 `BusinessException` 直接 `NotFound()`。`GdprDownloadController` 返回 `FileResult` 且无 catch。上传拒绝的 `data` 含 `fileName` 和 `reason`。用户同步导出走 `window.open`，失败体是 `error` 字符串。
- `AbpAdminSettingUiAppService` 已 `ReplaceServices`、`RemoteService(false)`，类级 `ShowSettingPage`，写方法另有 `AbpAdmin.SettingUi.Update`。不能再写成当前匿名可写。
- `HostTestBed` 写明 Quartz 把 `LoggerFactory` 缓存在静态字段，同进程 Dispose 后再初始化会失败。按环境拆进程、每进程一个宿主，与这条限制一致。
- `.github/workflows/ci.yml` 含 restore 闭包、solution restore、工具 restore、七套 EF 模型漂移、后端全量测试、前端 frozen install / tsc / test、Host 与 DbMigrator 镜像。web job 没有 `pnpm run lint`，也没有 `pnpm run build`。
- TokenGateway 的 `UpstreamErrors.FromException` 把未知异常收成固定网关错误；`ChatController` 在流开始前和开始后分两条写出。开发故障头只接受两个固定码，非 Development 忽略。

## 3. 对处置拒绝项的立场

上一份 Grok 报告里被拒绝或收窄的判断，这次逐条重核。**这三项不再坚持，也不再要求改回。**

| 原断言 | 处置 | 本次立场 | 依据 |
| --- | --- | --- | --- |
| 49 处都是无码 `UserFriendlyException`，并按这个数量做门禁 | 部分吸收。拒绝「49 处无码」 | **接受纠正。** 全量盘点仍然必要，因为有 code 也会把 `ex.Message` 插进文案。数量以执行时清单为准。 | 49 处构造点属实；带 `code:` 的是审计 11 处加 GDPR 1 处 |
| 首期就把独立 H5 的跨源 API 源白名单做进传输层 | 拒绝扩大首期 | **接受。** v1.1 写明首期只收相对地址、`credentials: 'same-origin'`、`redirect: 'error'`，未登记跨源直接拒绝；后续再加 origin、凭据、CORS/CSRF。这已经把扩展点留在安全模型里，而不是临时放开绝对 URL。 | D5 只做机制和模板示例；`web/AGENTS.md` 的管理端也是相对路径 |
| 依据里取消源码行号，只写文件名 | 不采纳 | **接受。** 行号会漂，但精确到当前优先级比只写文件更可复核。v1.1 改指 `requestErrorConfig.ts` 的实际优先级，合适。 | 配置对象起点和 `validationErrors/details/message` 优先级不是同一行 |

处置记录里其他拒绝，复核后同样同意，不提出翻案：

- 开发环境对未登记端点保留原文响应。这会绕过 D1。离线预览分类可以做，在线 dry-run 不能做。
- 禁止用 HTTP 状态做任何 kind。空体 401/403/429 仍要有本地安全分类；公开码优先于状态兜底，这两件事同时成立。
- 模板客户 DTO 塞进框架 `FrontendContractSnapshotTests` 的 `SeedTypes`。该测试工程不引用 Biz.Template，递归也停在种子程序集。框架 `ClientFailureDto` 进原快照，目录快照由 Host 组合测试对 `web` 文件负责。
- 把关联号有效期或客服 SLA 列为本轮交付。编号不是凭据；检索沿用 `AbpAdmin.AuditLogs` 和现有日志保留配置。
- 为 `session_revoked` 单设 kind。它和 ABP 401 同走 `authentication`，同会话只恢复一次。
- 模块控制器数量为 0、繁中缺键必然回显内部码、原生 ABP 路径不返回 Data。这三条与源码相反，v1.1 的纠正应保留。
- 强制影子发布或全出口笛卡尔积。全局故障回退整套版本；覆盖按适配器、高风险出口和语言完整性组合。

处置 Q20 写过环境变量名 `PUBLIC_ERROR_CONTRACT_SNAPSHOT_UPDATE`。v1.1 正文固定为 `UPDATE_PUBLIC_ERROR_CONTRACT`，并与 `FRONTEND_CONTRACT_SNAPSHOT_UPDATE` 分开。执行以方案正文为准。

## 4. 仍建议写进 v1.1 的两句口径

这两句不是推翻已合并的规则，是把已写规则的边界补全。

### 4.1 原协议只约束已处理的响应，未捕获异常仍不能进开发异常页

v1.1 要求 OAuth、TokenGateway/SSE、Hub 推送保持各自协议，同时要求受控 API 在 Development 也不返回开发异常页。`AbpExceptionHandlingMiddleware` 只消化带 `IsObjectResult` 的动作异常，其它异常继续向外抛。最外层在 Development 是 `UseDeveloperExceptionPage()`。

因此「保持原协议」只适用于已经写成 OAuth 体、SSE 错误帧或握手拒绝的那一次响应。`/connect/*` 上没被 grant 接住的异常、流尚未开始前没被 `ChatController` 接住的异常，仍会穿过友好异常中间件落到开发异常页。登录和网关客户端看得到这个响应。

建议在第 5.3 节加一句：原协议端点的已处理失败保持原形状；同一路径上未被业务代码接住的异常，按受控失败处理，响应体不得是开发异常页或生产错误页 HTML。A21 的「外层失败」明确包含 `/connect` 和一个尚未开始的 SSE 请求，而不是只测 `/api` 下的 MVC 动作。

`/signalr-hubs` 的 Cookie 挑战当前不走 `/api` 那个只写状态码的分支，可能是 302 登录页。A30 应断言全局 writer 不会把这条握手改成 ABP 信封，也不会在 Development 把未处理异常换成开发异常页。推送 payload 维持本轮非目标。

TokenGateway 的 `X-HB-Dev-Fault` 只在 Development 接受两个固定码。A30 用它做反例即可：非 Development 忽略该头；错误帧里的 `message` 来自网关固定错误，不来自 `Exception.Message`。这与「不把网关重包成 ABP 信封」不冲突。

### 4.2 会触发登录恢复的 401，公开码只能是 authentication

v1.1 的会话规则是：受控请求只要 HTTP 401，就做同会话一次恢复；`PublicKind` 不能取消这条规则。码和状态冲突时丢掉码和文案的组合，正文只点了 `business`+401 和 `authentication`+403。

按字面实现时，`Client:Forbidden`、`Client:NotFound`、`Client:OperationFailed` 若与 HTTP 401 同时出现，前端会保留该码和文案，同时清会话。用户看到的是权限或未找到，实际发生的是登录恢复。

建议把服务端拒绝和前端丢弃收成一条：触发会话恢复的 401，公开码只允许 `Client:AuthenticationRequired`（或没有公开码）；其它码与 401 同时出现时丢弃码和 message，使用本地 authentication 兜底。403 仍不恢复会话；403 上的 `business` 码继续保留，不能改成 `forbidden`。HTTP 200 的任务条目即使带 authentication，也不清当前会话。这三句 v1.1 已经有，保持不变。

## 5. 分角度

| 角度 | 判断 |
| --- | --- |
| 安全边界 | 成立。客户正文、Data、资源、200 里的失败字段、匿名下载和管理端插值都有规则。第 4.1 节补的是未处理异常这条缝。 |
| ABP 扩展点 | 成立。替换过滤器服务键和异常中间件服务键，不并列第二套，不重写 private 方法，不替换 `IExceptionToErrorInfoConverter`。`IMiddleware` 由 DI 解析，替换具体类型可以生效；A27 用解析结果验收，而不是看 `MvcOptions` 里的 `ServiceType` 是否变成子类。 |
| 管理兼容 | 成立。未登记默认 Public，全局替换必须和全装配登记表同提交。第三方与平台模块算进装配，不再只用「框架 API」。选择 Public 的现有管理端要有文案变化清单。 |
| 多语言 | 成立。客户资源独立、跳过数据库贡献者、禁止改挂 `GlobalContributors`、整段回退链缺键才算缺键、配置和两种脚本对管理员也不放宽。 |
| 前端 | 成立。独立 fetch、导入门禁、`none`、PascalCase 查询与 camelCase 错误体分开、生产构建看路由产物而不是 `hideInMenu`。跨源留到后续，本轮同意。 |
| 测试 | 成立。P1 就用 `HostUnderTest` 把真实模块跑到 HTTP；非 Development 另进程并自备测试证书，不读本机 secrets。Docker 不可用则关键用例记未验收。 |
| 发布与回退 | 成立。样例开关救不了全局过滤器故障；禁止原文透传和开发 dry-run；前后端按兼容组合回退，旧前端遇新码走兜底。 |
| 文档 | 方案正文是执行事实源。处置记录保留拒绝理由。画布冲突以 v1.1 为准。 |

## 6. 已经有验收、实施时不要踩的陷阱

这些不是方案缺口。测试已经点名，写代码时直接避开即可。

- 现有友好异常中间件和 ABP 异常中间件都会 `Response.Clear()`。图片繁忙过滤器先写的 `Retry-After` 会被清掉。统一 writer 在 Clear 之后按批准值重写该头。A31 覆盖。
- 过滤器路径调用基类包装后，必须换成投影器的新 `ObjectResult`。基类已经放进日志和通知的 `RemoteServiceErrorInfo` 不要改，也不要把它的 `message` 返回给客户。授权分支不要再写一次 body。
- 快照路径从 `AppContext.BaseDirectory` 向上找 `backend/AbpAdmin.slnx`，再定位同仓 `web`。找不到就失败，不按当前工作目录悄悄生成。
- 客户纯函数测试文件头使用 `// @vitest-environment node`，不改全局 happy-dom。
- A34 的生产构建和示例缺席检查，今天的 `ci.yml` web job 不会跑。完成定义把 `pnpm run build` 和产物检查放进该 job，避免「现有 CI 已绿」被当成 A34 已过。

## 7. 结论

v1.1 可以指导 P0。上一轮要求的登记范围、管理端插值、关联号入口和严格配置绑定都已经落在正文和 A21–A34 里。被拒绝的「49 处全是无码」、首期跨源传输和取消行号，复核后放弃。

开工时把第 4 节两句补进第 5.3 节和第 7.2 节：原协议端点的未捕获异常不算协议豁免；401 的公开码只允许 authentication。其余按 v1.1 的任务顺序执行。本次审核不代替实施授权。
