# 客户端错误处理开发方案 v1.1 · 第二轮独立审核报告（hy4-r2）

| 项 | 内容 |
| --- | --- |
| 审核对象 | `docs/client-error-handling-development-plan.md` **v1.1**（652 行）、`docs/client-error-handling-development-plan-review-disposition.md`（212 行） |
| 对照材料 | canvas 定稿取舍；`docs/` 下 Grok / hy4 第一轮 / Qwen 三份审核；`E:/code/abp_admin` 当前源码；`.github/workflows/ci.yml`；ABP 10.7.0 tag 官方源码（本轮实取 4 个文件） |
| 上一轮报告 | `docs/client-error-handling-development-plan-review-hy4.md`（254 行，**保留不改写**） |
| 审核日期 | 2026-11-05 |
| 方式 | 全文读取 + 只读源码核查 + 官方 tag 源码抽查；未运行构建/测试，未访问生产，未修改业务代码 |
| 结论 | **v1.1 的技术质量显著高于 v1.0，hy4 第一轮的大多数意见已被正确处理。** 仍有 **2 项实质分歧**和 **1 项处置记录方法问题**需要辩论；另发现 v1.1 修订新引入的 9 个问题（1 阻断、2 重要、4 中、2 低）。**本审核不授权实施。** |

> **引用约定**：`§N.N` 一律指**方案 v1.1**章节（特别注明 v1.0 时除外）；本文单独出现的 `F*`/`B*`/`M*` 指 **hy4 第一轮报告**的条目；`H01–H37`／`G*`／`Q*` 一律指**处置记录**编号（hy4 第一轮自带的 `H01–H09` 验收项如需引用会写作「hy4 r1 H0x」）；`r1:111` 指第一轮报告第 111 行；`本报告 §N` 指本文。行号按本轮读取版本计数。

---

## 0. 先给结论

v1.1 不是措辞修补，而是一次实质升级：隐式 `AbpExceptionHandlingMiddleware`、ABP 默认回传 `Exception.Data`、Umi 生成 runtime 的临时拦截器、资源回退链、带码友好异常、控制器盘点口径——这些都纠正了 v1.0 及部分审核报告的事实错误。`A01–A34`、`P1` 真实管线前置、全局替换同提交回归、双快照分层、回退边界，都比我第一轮的建议更完整。

本轮我**撤回**一项被处置记录正确驳回的事实结论（Umi 拦截器），**修正**一项归因过窄的判断（exchange 工作树文件），并**让步**若干实现选择。剩下仍坚持的只有三件事：**交接文档的版本控制与链接可移植性**、**出口清单仍未闭合**、**处置记录把带限定条件的建议读成无条件要求后再拒绝的系统性倾向**。前两件会实质影响后续执行，第三件影响交接记录的准确性。

---

## 1. 审核方法与独立性

本轮没有把处置记录的结论当证据：逐条回读 hy4 r1 原文、对照 v1.1 实际改动、再到源码与官方 tag 复核。重点抽查了 4 个 ABP 10.7.0 文件：`AbpApplicationBuilderExtensions.cs`、`AbpExceptionHandlingOptions.cs`、`AbpCorrelationIdMiddleware.cs`、`DefaultExceptionToErrorInfoConverter.cs`；另用文件系统直查绕过了上一轮 Glob 忽略 gitignored 目录导致的漏查（`.umi`）。

**先承认两处我上一轮的错误**：

1. **hy4 r1 F4 判断错误，处置记录 H17 正确。** 上一轮子代理报告"`web/src/.umi*/**` 无实际生成文件"是工具限制造成的误判。本轮用 `find`/`ls` 直查确认 `web/src/.umi/plugin-request/request.ts` 与 `web/src/.umi-production/plugin-request/request.ts` 均存在（各 9440 字节），`:188–235` 确实在 `getRequestInstance()` 返回的**共享 axios 单例**上按请求 `interceptors.request.use(...)`、在 then/catch 中 `eject(...)`。`package.json:65` 声明 `@umijs/max: ^4.7.24`。v1.0 §2.1(10) 的事实描述成立，我「仓库源码中找不到对应代码」的结论撤回。
2. **hy4 r1 F1 归因过窄，处置记录脚注正确。** 本轮在 `exchange/exchange/` 直查到 `buildspec.yml`（13448 字节）与 `nul`（34 字节）。canvas「已存在的 … 及未跟踪 nul」应理解为**两个业务仓库合并的工作区状态**，不是凭空陈述。我「三项中仅一项属实」的结论撤回，改为「在 abp_admin 当前工作区内仅一项属实，但 canvas 的跨仓库归因成立」。v1.1 删除该陈述是稳妥处理。

这两条说明：处置记录的源码核实是可信的，本文的辩论只针对**我没有被说服的部分**，不针对其取证能力。

---

## 2. hy4 第一轮 37 项处置总账

| 处理 | 条目 | 说明 |
| --- | --- | --- |
| **撤回 / 让步** | F4(H17)、F1(H14)、M5 中「模板 DTO 进框架 SeedTypes」(H09)、M9「header 唯一事实源」(H13)、M1 中「熵下限/有效期/客服 SLA」(H05)、F5「ABP 版本硬锁断言」(H18)、H27「原头不进客户响应」、H14 中「历史从未存在」外推 | 事实错误或对方方案更优，明确认输，见 §1 与 §3 |
| **接受 v1.1 处理** | B2(H02)、B3(H03)、B4(H04)、M2(H06)、M3(H07)、M4(H08)、M6(H10)、M7(H11)、M8(H12)、F3(H16)、F6(H19)、F7(H20)、F9(H22)、F11(H24)、H01–H09 大部分、H25–H36 | 核心诉求已进 v1.1；部分处置标签仍需辩论，见本报告 §3.3 |
| **仍坚持并辩论** | F10(H23/H37) + F8(H21) → 辩论一；B1(H01/H25/H28) → 辩论二；处置记录方法论 → 辩论三 | 本报告 §3 |

特别说明三处**我本来就给了替代分支、v1.1 实际走了该分支**的情况：M1 的「二选一」第二支（v1.1 §4.3 落地）、M7 的「或至少在隔离环境完成全量回归」（v1.1 §4.1 落地）、M3 的「base 后替换 `context.Result`」（v1.1 §5.3(1) 落地）。这些在处置记录里被记成「拒绝」或附带拒绝语，属于标签错配而非技术分歧。

---

## 3. 辩论：仍坚持的条目

### 3.1 辩论一 · 交接文档必须纳入版本控制，链接必须可移植（F10/F8 → H23/H37/H21）

**对方理由**（逐条如实转述）：

- H23：「六项决定已经写在仓内 v1.0 §1.2，无须再造重复决策文件」；「不采纳把当前自动 Git 提交设为完成前提，用户本轮要求核实和改文档，没有要求提交」。
- H37：「不照搬全部 H/A 建议一律接受……或将本次 Git 提交设为前提」「审核报告本身不产生实施授权，也不能替用户施加额外审批边界」。
- H21：「本次桌面交付按当前文件链接约定保留绝对可点击路径」；「若另导出普通仓库 Markdown，应从文档所在目录计算链接，例如 `../backend/AGENTS.md`」。
- v1.1 §14：「链接使用当前工作区绝对路径，文件清单同时提供相对仓库根路径；迁移到其他机器时以文首仓库根＋相对路径定位」「本次未提交 Git」。

**我的让步**：① 不要求本轮执行 Git 提交——用户这轮只要审核报告，H23 的这一点成立；② 不要求「再造一份重复决策文件」，六项取舍写在 §1.2 足够；③ 接受 H21 的技术修正：docs 下的相对链接必须写成 `../backend/AGENTS.md`，不能照抄 `backend/AGENTS.md`，我第一轮的措辞不够精确。

**仍坚持的部分与论据**：

1. **「目录内」不等于「版本控制内」。** 本轮核查时 `git status --short` 显示：` M docker-compose.yml` 之外，方案、处置记录、Grok / hy4 / Qwen 三份审核**全部为 `??`（未跟踪）**；核查过程中 docs/ 下又新增两份第二轮审核文档，同样未跟踪。本主题相关文档没有一份进入版本控制。v1.1 §14 自己也写明「本次未提交 Git」。换机器、克隆、CI checkout 之后，这些文件一个都不存在。H23 说「已经写在仓内」在文件系统意义上成立，在交接意义上不成立。
2. **被引用文档自身未跟踪，构成断链。** v1.1 抬头写「逐项审核结论、拒绝理由及证据行号见[审核处置记录](./...)」，而该处置记录是 `??` 文件；§13 的 7 条本地依据、§14 的链接全部是 `E:/...` 绝对路径。加上 canvas 在 `C:/Users/Administrator/.cursor/projects/...`（用户目录，不在仓库），v1.1 所自称的「独立、可执行的交接文档，不依赖聊天记录或可视化方案」目前**依赖本机文件系统与用户目录**。
3. **H21 自己已经承认不可移植。** 「若另导出普通仓库 Markdown，应从文档所在目录计算链接」这句，等于承认当前 `E:/...` 写法在普通 Markdown 环境不可点击。这不是「另导出时才需要」的额外要求，而是「可交接」的定义本身：交接对象不该被要求先改一遍链接才能读。
4. **H37 混淆了「放行条件」与「审批边界」。** 提出放行条件是审核的本职；我第一轮 §8 列的是「建议放行条件」，不是替用户设审批。而且「设计定稿 ≠ 开工授权」恰恰是 canvas 自己写的（「本次仅确认规划，不授权启动代码实施」），v1.1 抬头也已改成「本轮只核实审核并修订文档，不启动代码实施」——这与我的建议同向，不是外加边界。
5. **成本极低。** 改相对链接、把 5 份文档纳入一次提交，不改变 D1–D6、不改任何代码、不影响本轮「不实施」的约定。

**具体请求**（不要求本轮执行）：把「文档纳入版本控制 + docs 内链接改为 `../` 相对路径」列入**实施放行条件**，而不是判定为「已解决」或「拒绝」。本轮我只出具报告，不提交、不改方案。

### 3.2 辩论二 · 出口清单仍未闭合，且处置记录与 v1.1 正文自相矛盾（B1 → H01/H25/H28）

**对方理由**：H01 认为「这些异常确定经过此中间件、已可复现客户泄漏」未被只读证据证明，「MVC 通道可能先处理」；TokenGateway/SSE 应登记为独立协议而非泄漏源；`CacheMonitorAppService` 有管理权限，拒绝自动定性为客户泄漏。H25/H28 表示已补「响应写出者清单」与合成夹具。

**我的让步（两处，明确撤回）**：

1. **撤回「确定性经过 `FriendlyBusinessExceptionMiddleware`」的链路措辞。** 本轮拿到精确管线顺序后，这个说法确实过强：`UseCorrelationId`(1427) → `UseFriendlyBusinessException`(1443) → `UseFileUploadContentGuard`(1446) → `UseRouting`(1448) → `UseUnitOfWork`(1516，内含 `UseAbpExceptionHandling`) → `UseAuthorization`(1538) → `UseAuditing`(1540) → `UseConfiguredEndpoints`(1542)。MVC `AbpExceptionFilter` 在最内层，Controller/AppService 抛出的带码 `BusinessException` 会先被它 `ExceptionHandled = true` 消费，**不会冒泡到外层 Friendly 中间件**。处置记录的反驳在这一点上成立。
2. **撤回 TokenGateway「泄漏」暗示。** 本轮核实 `UpstreamErrors.FromException`（`Core/UpstreamErrors.cs:28-34`）把未知异常固定映射为 `GatewayException.Internal()`，`GatewayError.cs:22-26` 全为固定中文串，SSE 错误帧（`ChatController.cs:157,165-169`）经过该映射，`GatewayControllerBase.Error(e)` 的 `e.Message` 是 `GatewayException` 的固定文案；`GatewayError.BadRequest(string m)` 虽允许任意串，但当前全部调用点传固定串。**当前网关不回显上游原文**。v1.1 §1.3/A30 按「独立协议/非目标」登记是正确的，理由应是「形状与覆盖」而非「泄漏」。

**仍坚持的部分与论据**：

1. **实质结论不是「某条链路」，而是「ABP 默认就会输出 Data 与由 Data 插值出的 message」。** 本轮实取官方源码：
   - `AbpExceptionHandlingOptions` 构造函数：`SendExceptionsDetailsToClients=false`、`SendStackTraceToClients=true`、`SendExceptionDataToClientTypes = [typeof(IBusinessException)]`。
   - `DefaultExceptionToErrorInfoConverter.CreateErrorInfoWithoutCode`：`errorInfo.Data = exception.Data`（命中 `IBusinessException` 时）；`TryToLocalizeExceptionMessage` 遍历 `exception.Data.Keys`，把 `{key}` 替换为 `exception.Data[key]?.ToString()` 后赋给 `errorInfo.Message`。
   - `AbpExceptionFilter.LogException` 与 `AbpExceptionHandlingMiddleware.HandleAndWrapException` 都把宿主 options 传给同一个 converter。
   
   因此**三条异常出口（MVC filter、隐式中间件、手写中间件）默认都会携带未批准 Data，且 message 可能在转换器内部已被 Data 污染**。这不是「需要证明某条具体链路」的推测，而是框架默认配置下的行为。
2. **处置记录与 v1.1 正文对同一事实给出相反判定。** v1.1 §2.1(12) 已写入：「ABP 10.7.0 的 `SendExceptionDataToClientTypes` 默认包含 `IBusinessException`。仓库没有显式配置不等于默认不返回 Data；原生异常链和手写链都要投影」；§4.3 也写了「管理路径也先过滤批准参数再用管理资源插值；不能复用默认转换器已经用完整 Data 格式化的 message」。**这正是我 B1 的核心结论**，且 v1.1 采纳得比我原文更准确。但 H01 仍写「未被只读证据证明」并把处置标为「部分吸收」。同一份交付物里，方案正文承认、处置记录否认，后续执行者按哪一句为准？这是我坚持要求修正记录的原因。
3. **「已补写出者清单」尚未闭合。** v1.1 §5.3 的 10 条 bullet 之外，本轮仍找到以下实际写出点未被点名（均为只读核实）：
   - `Application/Account/ImpersonationTokenExchanger.cs:70-72` `.WithData("reason", response.Result.ReadOAuthError() ?? ...)` 与 `OpenIddict/OpenIddictTokenExchanger.cs:56`：**把上游 OAuth `error_description` 写进 `BusinessException.Data`**，这是比短信发送器更贴近 OAuth 路径的新原文源。
   - `ExtensionGrants/ExtensionGrantSupport.cs:35-46`：4 个 grant 共用的 `CreateErrorResult`，是 `error_description` 的唯一汇合点；v1.1 只点名 `ImpersonationTokenExtensionGrant.cs:133`（经核实该目录确实只有这一处 `ex.Code ?? ex.Message`，其余 3 个 grant 全用固定文案）。
   - `UserExportController.cs:109,132` 与 `AuditLogExportController.cs:90,106`：`return Forbid()` / `NotFound()` 的**空体 403/404**，不是异常、不是 `BadRequest(error字符串)`，统一 writer 碰不到；v1.1 §5.3:248 与 A24 只覆盖 BadRequest 与 FileShare 显式 404。
   - `AuditLogAppService.cs:498-501,573`：`L["AbpAdmin:EntityRestoreUpdateFailed", ex.Message]`，带 code 的友好异常预插值原文，§4.3 按类别提及但 §5.3 清单未列文件。
   - `TokenGateway/Http/WalletController.cs:92` `StatusCode(501)`、`ChatController.cs:169` 已开始响应后的 SSE 错误帧。
4. **`AllEndpointsHaveAudienceTests` 覆盖不到这些写出点。** v1.1 §4.1 要求「枚举 EndpointDataSource 与 IActionDescriptorCollectionProvider」，但本轮核实仓库现有范例只有 `AnonymousEndpointSweepTests.cs:293-296` 的 `EndpointDataSource.Endpoints.OfType<RouteEndpoint>()`，`IActionDescriptorCollectionProvider` 全仓零匹配。两者都只枚举 **MVC/路由端点**；inline 中间件、SSE、OAuth grant、直接 `ActionResult` 的写出点仍需人工清单。v1.1 §4.1 说「手写中间件、OAuth、SSE/Hub…另列原协议/非目标行」，是**人工登记**，没有机器断言防止新增写出点被漏掉——这正是我第一轮 B2 担心的漂移，只是从「端点登记」平移到了「写出点登记」。

**具体请求**：① 把 H01 的「未被证明」改为「链路措辞不成立、但默认 Data/message 结论成立并已由 §2.1(12) 采纳」；② P0 清单补上述 5 类 ExitId，并给「写出点」也加一条静态扫描或装配级断言，不能只靠人工列行；③ 保留 v1.1 §2.1(12)/§4.3 不改。

### 3.3 辩论三 · 处置记录存在系统性「稻草人化」与标签错配（H03/H05/H07/H08/H10/H11/H12、H37）

这不是要求改方案正文，而是要求修正**交接记录**：逐条处置表是后续执行者判断「哪些已处理」的依据，标签错会有实际后果。举证如下（左侧为 hy4 r1 原文，行号经本轮回读确认）：

| 处置 | 记录中的「拒绝对象」 | hy4 r1 原文 | 实际情况 |
| --- | --- | --- | --- |
| H05 | 「新增关联号有效期、客服检索 SLA 作为**强制交付**」 | r1:111 明写「……**或显式把它降级为『本轮非目标，D3 中的该子承诺后移』并同步 canvas。二选一，不要留白**」 | v1.1 §4.3 落的正是第二支（复用现有审计页、`AbpAdmin.AuditLogs` 权限、不新建客服平台、不新增 SLA）。**按建议执行却记为拒绝**，且会让执行者误以为 D3 诊断入口仍未处理 |
| H11 | 「**强制新建影子模式框架**」 | r1:159「替换前以影子模式记录新旧响应差异、**或至少在隔离环境完成管理端全量回归**」 | v1.1 §4.1「全局替换提交与登记表及管理回归同提交通过」= 替代支。我从未要求强制新建 |
| H07 | 「**无条件** base 后再写一次响应」 | r1 M3「调用 `base.HandleAndWrapException(context)` 后**替换 `context.Result`**（不是写响应）」 | v1.1 §5.3(1) 照此实现，且自己加了「不是授权分支时／HasStarted 预先短路」的限定 |
| H03 | 「**P2/P3 全部工作必须等待**的过度串行安排」 | r1 B3「P1 的投影器与真实夹具冻结后，P2/P3 **才可并行**」 | v1.1 §9「契约冻结后…纯函数可并行；全局替换合并及集成验收依赖 P1 真实基线」= 同一意思 |
| H10 | 「首期**同时完成独立 H5 跨源认证传输**」 | r1 M6 要求「定义 `credentials`／CORS／CSRF／URL 归一化**策略**」 | v1.1 §7.1 已定义「首期跨源明确拒绝 + `credentials: same-origin` + 未来扩展点」= 采纳了策略要求，我未要求首期建跨源产品 |
| H08 | 「**扩大成全量管理 DTO 重写**」 | r1 M4 明确写「不得全局递归替换成功 JSON 中的字符串」 | v1.1 §3.3 补了无码字符串映射规则，未全量重写 |
| H12 | 「全部出口 × 全部异常 × 全部语言 × UI 机械笛卡尔积」 | r1:165 写的是「**出口 × 错误类型**」两维 | v1.1 §10 的「适配器覆盖＋高风险具体出口＋语言完整性」是合理细化，我接受；但记录把两维扩成四维再拒绝 |
| H37 | 「审核报告……**替用户施加额外审批边界**」 | r1 §8 标题是「最终结论与**放行条件**」（建议） | 见辩论一第 4 点 |

另外 H06 拒绝「永久禁止且未确认」这一我未提出的前提，而我的 M2 核心是「**未作为取舍登记**」——v1.1 §1.3 已补「首期不支持租户在线覆盖客户错误文案」，正是我建议的落地。

**具体请求**：不改 D1–D6、不改 v1.1 正文，只在处置记录中把这些条目的「拒绝」改为「按报告给出的替代分支执行」或「部分吸收（对方建议含限定条件，本处拒绝的是未提出的极端形式）」。若不便修订原记录，本报告 §3 可作为对照留档。

---

## 4. 多角度复审：v1.1 修订新引入的问题

| ID | 级别 | 角度 | 问题与证据 |
| --- | --- | --- | --- |
| **N1** | **阻断（P1 前必须补）** | 架构可行性 / 可执行性 | **§3.1 关联号规则的落点与 ABP 机制不匹配，且 §5.1 无对应文件。** 官方 `AbpCorrelationIdMiddleware.InvokeAsync` 由 `GetCorrelationIdFromRequest(context)` 从请求头取值（缺失时生成 `Guid("N")` 并**写回 `Request.Headers`**），再用 `_correlationIdProvider.Change(correlationId)` 建立 AsyncLocal 作用域，`CheckAndSetCorrelationIdOnResponse` 通过 `Response.OnStarting` 写响应头。`ICorrelationIdProvider` 是**消费方**，不是「传入后由 provider 生成」的工厂；§3.1「先确定一次再传给原 ABP provider」措辞与之不符。本仓（agent E19 全仓检索）无 `AbpCorrelationIdOptions` 配置、无自定义 provider、无关联号请求头改写，`UseCorrelationId()` 前后也没有校验中间件。可行路径应是：在 `:1427` **之前**加中间件改写 `Request.Headers[X-Correlation-Id]`，或替换 `AbpCorrelationIdMiddleware` 服务类型并 override `protected virtual GetCorrelationIdFromRequest`（`UseCorrelationId()` 用 `UseMiddleware<T>`，DI 可替换）。§5.1 Host/ClientErrors/ 只写「关联号入口校验」，未给文件名、未说明响应头由 `OnStarting` 单点写出（其条件是 `!Response.Headers.ContainsKey(...)`，多写一处的顺序需明确）。**这是可直接照着写代码的缺口。** |
| **N2** | **重要** | 范围 / 测试基建 | **测试基建规模已超过 D5「框架机制与示例」的边界。** §11.1 新增「进程隔离 runner + 父进程传合成配置/测试容器连接 + 收集结果检查退出码」、`ClientErrors/HostProcessFixture`、非 Development 受控环境参数与测试专用证书接缝；§11.2 要求跑完整 CI（7 套 EF 漂移、双镜像构建）；A 矩阵 34 项；再加静态导入门禁与生产构建产物断言。`HostUnderTest.cs:64-78` 固定 Development 与 `HostTestBed.cs:14-15` 的 Quartz 静态 LoggerFactory 问题属实（本轮核实），但**为一个错误处理框架首期建设跨进程测试宿主**是可独立立项的工程。建议显式分层：P1 必做 = Development 真实管线 + 核心 A 项；非 Development / 进程隔离 / 完整 CI = 可延后或由现有 CI 承担，并写明「未完成的分支不计入 A 项通过」。否则 P1 极可能被测试基建而非框架代码阻塞。 |
| **N3** | **重要** | 出口完整性 | 见辩论二第 3、4 点：5 类未点名写出点 + 写出点无机器断言。v1.1 §5.3 从 7 个文件扩到 10 条 bullet 是明显进步，但 P0 清单的「完整」仍依赖人工枚举。 |
| N4 | 中 | 配置安全 | `AbpExceptionHandlingOptions` 默认 `SendStackTraceToClients = true`（本轮官方源码核实）。虽然 `CreateErrorInfoWithoutCode` 仅在 `SendExceptionsDetailsToClients=true` 时才走含堆栈的 detailed 分支，默认不泄漏，但**这是 ABP 自有 options，不受 §5.4「ClientErrorHandling 节严格绑定」约束**。若有人为管理兼容打开 `SendExceptionsDetailsToClients`，`details` 与堆栈会一起进入 converter 结果，且统一投影只替换响应对象、不影响日志序列化。建议加启动断言：客户模式下 `SendExceptionsDetailsToClients` 必须为 false（含 Development），并写进 A21/A23。 |
| N5 | 中 | 信任边界 | §3.1 接受客户端传入的 1–64 位 `[A-Za-z0-9._-]` 关联号，只校验格式不校验来源。攻击者可故意填入**他人请求的关联号**，把自己的失败日志挂到别人的关联链上，制造排障混淆／审计假关联。关联号虽非授权令牌（H05 的论证成立），但客户入口的**可信来源**应区分：建议 Public/Shared 入口一律忽略传入值、由服务端生成；内部/受权管理端保留链路传递。v1.1 未区分。 |
| N6 | 中 | 协议契约 | §7.2 只定义了 `business`+401、`authentication`+403 两种码/状态冲突。未覆盖 `forbidden`+401、`rateLimited`+非 429、`notFound`+非 404、`server`+2xx 任务条目等组合。建议改为「每个 PublicKind 声明允许的状态集合，越界一律丢弃码与 message、按状态安全兜底」，与现有 A15 合并。 |
| N7 | 低 | 前端契约 | §7.1 的 catalog 含「契约版本」，§7.4 说「不要求前后端原子同步上线、不新增每个响应带 version」，但全文未定义前端如何使用该字段（不比对、不协商）。应注明「仅用于构建期诊断与人工排查」或删除，避免执行者以为存在运行时版本校验。 |
| N8 | 低 | 事实精度 | ① §4.1「枚举 EndpointDataSource 与 IActionDescriptorCollectionProvider」——后者全仓零匹配，现有范例只有前者（`AnonymousEndpointSweepTests.cs:293-296`），措辞易被读成已有实现；② §2.1(10) 说「当前**已安装** Umi 4.7.24」，`package.json:65` 是 `^4.7.24`（caret 范围），生成 runtime 的内容才是事实依据；③ §5.3 「必须逐一接入」有 10 条 bullet，其中末条附带「同目录其他 grant」，实际共 13 个 .cs，行数口径宜写清楚；④ §7.1「只接收相对地址」却又要求「用标准 URL 解析器与可信页面 origin 归一化后匹配同源」——相对地址解析后 origin 恒等于页面 origin，「匹配同源」是冗余条件，建议改成「相对地址 + 拒绝绝对/协议相对地址」。 |
| N9 | 中 | 缓存/资源 | §6(6) 已要求缓存键纳入文化、租户、权限视图版本，方向正确；但 `AbpStringLocalizerFactory` 会在创建 localizer 时追加 `GlobalContributors`（处置 Q06 已提及），v1.1 §6(2) 只写「不得改用 GlobalContributors 把数据库贡献者加回」与「测试实际 localizer 创建后客户资源不含数据库贡献者」。建议把该负向测试写成 A34 的独立断言项，而非散在 §6 文字里，避免 P2 只测配置不测运行态。 |

---

## 5. 多角度评级：v1.0 → v1.1

| 角度 | v1.0 | v1.1 | 说明 |
| --- | --- | --- | --- |
| 产品取舍可追溯 | 中 | 良 | §1.3 补非目标、§4.3 补受权排障入口，D3 落点已明确 |
| 安全威胁模型 | 偏弱 | 中上 | 默认 Data、原生异常链、OAuth、空体认证、200 内失败均已纳管；仍缺关联号来源信任（N5） |
| 服务端出口完整性 | 不通过 | 中 | 清单大幅扩充、隐式中间件成为必接项；仍有 5 类遗漏、写出点无机器断言（N3） |
| ABP 扩展点可行性 | 基本成立 | 良 | filter/中间件双替换、`ShouldHandleException` override、严格绑定均有官方源码支撑；关联号落点除外（N1） |
| 端点分类与授权 | 设计对、门禁弱 | 良 | 稳定登记键、双向漂移测试、全装配覆盖、SettingUi 精确登记 |
| 协议与契约 | 中 | 良 | 无码字符串映射、等待时间双源、码/状态冲突、validationErrors.members 均补齐；组合规则不全（N6） |
| 多语言与资源 | 成立 | 良 | 支持集、回退链、GlobalContributors、缓存键、独立管理入口 |
| 前端设计 | 中上 | 良 | 同源/凭证/URL 解析/静态导入门禁/双快照分工 |
| 阶段与验证顺序 | 不通过 | 良 | 真实管线夹具前置 P1、全局替换同提交回归（B3 已解决） |
| 测试与验收矩阵 | 中 | 中上 | A01–A34 + ExitId 映射 + 公共断言器；基建过重（N2）、负向测试未编号（N9） |
| 发布与回退 | 偏弱 | 良 | 全局改动整版本回滚、非原子部署兼容、生产产物检查 |
| 文档工程与交接 | 不通过 | **仍不通过** | 6 份文档未跟踪、canvas 在仓外、全篇 `E:/...` 绝对路径、被引处置记录自身未入库（辩论一） |

---

## 6. 本轮核实确认成立的关键事实（供执行者直接引用）

- **官方 `UseUnitOfWork()`** = `UseAbpExceptionHandling().UseMiddleware<AbpUnitOfWorkMiddleware>()`；`UseAbpExceptionHandling()` 用私有 marker `_AbpExceptionHandlingMiddleware_Added` 防重复，并通过 `UseMiddleware<AbpExceptionHandlingMiddleware>()` 注册 → **DI 替换服务类型可行**，v1.1 §2.1(11)/§5.3(7) 成立；v1.1「不得手写其私有注册标记」正确。
- **官方 `AbpExceptionHandlingOptions`** 默认 `SendExceptionDataToClientTypes=[IBusinessException]`、`SendStackTraceToClients=true`、`SendExceptionsDetailsToClients=false`。**官方 `DefaultExceptionToErrorInfoConverter`** 命中时 `errorInfo.Data = exception.Data`，且 `TryToLocalizeExceptionMessage` 用 `exception.Data` 替换 `{key}` 生成 `errorInfo.Message`；`GetValidationErrorInfos` 把 `Members` 转 camelCase。→ 处置记录 O1/O2、v1.1 §2.1(12)/§4.3 成立。
- **官方 `AbpCorrelationIdMiddleware`**：`GetCorrelationIdFromRequest` 读请求头、缺失生成 `Guid("N")` 写回请求头；`CheckAndSetCorrelationIdOnResponse` 在 `Response.OnStarting` 写响应头且仅当头不存在；`InvokeAsync` 用 `_correlationIdProvider.Change(...)` 建立作用域。→ N1 成立。
- **管线顺序**（`AbpAdminHttpApiHostModule.cs`）：1427 CorrelationId → 1433 UseErrorPage(非Dev) → 1443 Friendly → 1446 上传守卫 → 1448 Routing → 1463 Token 限流 → 1465/1466 认证 → 1470 多租户 → 1487 租户失配守卫 → **1516 UseUnitOfWork（隐式异常中间件）** → 1518/1519 会话/数据范围 → 1538 授权 → 1540 审计 → 1542 Endpoints。三层异常出口的相对位置由此确定，v1.1 §5.3「外层兜底只接它未处理的异常」判断正确。
- **本地**：`.umi` 生成 runtime（撤回 F4）；exchange 工作树 `buildspec.yml`/`nul`（修正 F1）；`FrontendContractSnapshotTests.cs:126,197-199` 递归只纳入首个种子程序集且 `AbpAdmin.Application.Tests.csproj` 无 Biz.Template 引用（撤回 M5 模板 DTO 部分）；`HostUnderTest.cs:64-78` 固定 Development、`HostTestBed.cs:14-15` Quartz 静态问题（N2 背景属实）；CI 仅 tsc/test 无 lint/build（H19 成立）；`requestErrorConfig.ts:94/117/87` 引用精度（v1.1 §13 已修正）；TokenGateway 固定文案（撤回泄漏暗示）。

---

## 7. 放行条件（不重开 D1–D6，不授权实施）

1. **补 N1**：在 §5.1 给出关联号校验的具体落点（前置中间件改写请求头，或替换 `AbpCorrelationIdMiddleware` 并 override `GetCorrelationIdFromRequest`），明确响应头由 `OnStarting` 单点写出；同步修正 §3.1「传给原 ABP provider」的措辞。
2. **补 N3**：P0 清单纳入辩论二列出的 5 类写出点，并给「写出点」加静态扫描或装配级断言；同时把 H01 的记录结论与 v1.1 §2.1(12) 对齐。
3. **明确 N2 分层**：把测试基建拆成「P1 必做」与「可延后/由现有 CI 承担」，未完成分支不计入 A 项通过。
4. **辩论一**：把「文档纳入版本控制 + docs 内链接改 `../` 相对路径」列为实施放行条件；本轮不执行提交。
5. **辩论三**：修订处置记录中 7 处标签错配，或把本报告 §3.3 作为对照留档。
6. **N4/N5/N6** 并入 A21/A23/A15/A34 或写入模块接入指南，不新增产品范围。

**保持不变**：P1 真实管线夹具前置、全局替换与登记表及管理回归同提交、ABP 信封与 OAuth 协议分离、不替换 `IExceptionToErrorInfoConverter`、不全局递归替换成功 JSON、双快照分层、示例默认关闭、管理 404 静默、不新建表/不改资金幂等。v1.1 在这些点上比第一轮建议更完整，不应因本报告的辩论而回退。

---

## 8. 审核边界

本轮为只读评审：未运行任何构建、测试或安装，未访问生产，未修改业务代码或前述任何文档；**仅新增本报告文件**。文中「属实／不成立」的判断依据为本轮读取的仓库源码与 ABP 10.7.0 tag 官方源码，运行时行为（响应体、中间件实际执行顺序、跨进程夹具可用性）仍须由 v1.1 §11 的真实 HTTP 管线验收证明，静态核查不代为背书。
