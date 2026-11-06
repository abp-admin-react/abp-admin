# 《abp_admin 客户端错误处理开发方案》独立审核报告 · 扩大范围面扫描（qwen）

- 被审文档：`docs/client-error-handling-development-plan.md` v1.1，`sha256(前16)=c5f78192776dd322`、`mtime=2026-11-05 15:17:34`；本轮 16:5x 复测未变。
- 前两轮：`...-review-qwen.md`（第 1 轮）、`...-review-2-qwen.md`（第 2 轮）。**本轮不是第 3 轮正文复审，而是把审核面从"计划内部自洽"扩展到"它推翻了什么没表态 / 它落到哪些文件"**。
- 独立性：未打开 `-grok` / `-hy4` / `-disposition` 任何一份（本轮检索全程用 `--glob '!*review*'` 或排除式 grep；下 §1 披露一处文件名级暴露）。
- 取证方式：两路子任务做面扫描（后端与上游文档/前端落点），**其结论逐条回读原文后才进入本报告结论**；未回读的写入 §6 并明确标注。所有 P0/P1 的落点行号均为我本人 `sed -n` 实读。

---

## 0. 摘要

前两轮我审的是"计划说的话对不对"。这一轮审"计划说完之后，仓库里谁会被打到"。结论：

**计划正文内部已经收敛到 P0=0（第 2 轮判定），但扩大范围后出现 2 条新的 P0——它们都不在计划里，且都不是计划写错，而是计划没有提到那两个面的存在：**

- **新 P0-1（内部码可见性同时是"防卫生效的证据"）**：仓里有 31 KB 的 Python 黑盒安全探针 `sectest/`，它的判定函数**以"响应里能不能看到内部码"作为守卫生效的硬证据**——`sectest/sectest.py:103-108` 用 `"AbpAdmin:"`/`"Volo.Abp:"` 字符串扫全文取码，`:466-468` 用 `SmsEndpointBlocked`/`CredentialNotConfigured` 是否在码里来断言"SSRF/凭据守卫没有把内网端点打出去"。而 `docs/saas-module-test-plan.md:249` 把这套探针**登记为 API 层防线的 owner**（"sectest 出报告优先"）。本计划的 D1/§3.1 恰好要把内部码从客户响应里收掉。**计划全文 0 次提到 sectest**（实测 `grep -c sectest` = 0）。
- **新 P0-2（样板改名清单不含新词根，复制模块即启动炸）**：`.cursor/rules/framework/common/biz-module.mdc:12` 的改名清单只有三个词根（`BizTemplate` / `Biz` / `BizProject(s)`），并写着"**全部词根一个不落，漏一处即静默冲突**"。计划 §8 往样板里新增 `Client/Controllers/`、`ClientErrors/BizTemplatePublicErrorDefinitions.cs`、`Localization/BizTemplateClient/`、公开码前缀 `BizTemplate:Public:`、开关 `BizTemplate:EnableClientErrorSample`——这些新词根**不在清单里**，而计划 §5.2 第 219 行规定"同一公开码出现不一致定义必须启动失败"。⇒ 下一个按 `backend/AGENTS.md:15` 复制样板的模块会与 Biz.Template 争同一批公开码/资源名，**宿主启动即失败**，而 A18「模板可复制/移除」仍会判过。计划全文 0 次提到改名清单/`biz-module`。

另外有 **1 条规模自纠**和 **6 条 P1**（其中 3 条是我前两轮给出的数字或建议的下游后果）。

**是否停文档复审**：不停，但**换形态**。正文级 P0 已连续两轮归零（本轮 2 条 P0 都是"缺登记对象"而非"口径矛盾"，各改 1-3 行即可闭合）；文档复审的边际收益已经低于"读一遍 `sectest/` 与 `finance.ts`"。建议按 §7 补 6 处登记与 4 条 A 用例后**直接开工**，并把下一次审核改成**审代码 + 跑 sectest/A 矩阵**。

---

## 1. 独立性事件

1. 我用了两个并行只读子任务做面扫描。**其中一个子任务的输出末尾带回了 `docs/client-error-handling-development-plan-review-disposition.md` 的一条引用行**（它在读计划 §14 时顺带命中，内容是"纠正 ABP 默认 Data / Umi 拦截器 / 资源回退 / 控制器盘点等错误判断"这一句的转述）。这是文件名级/标题级暴露，发生在我的本轮结论成型**之后**，且不含任何具体技术结论；我本轮的 2 条 P0 与 6 条 P1 全部有独立第一手取证链（见各行 `path:line`），不依赖该句。
2. 处置表里 grok/hy4 的结论本轮未读，也不作为任何条目的依据。
3. 子任务给出的下述断言我**未回读原文**，因此不进入结论强度，只列在 §6：`Program.cs` 的入口点签名、`HostTestBed.cs:10-15` 的 Quartz 静态问题、`HostUnderTest.cs` 各段、`culture.ts`/`tenant.ts` 的 cookie 交叉污染路径、`OperationRateLimitingContext.cs:11-13` 的分区键告警句。

---

## 2. A 表 — 上游已钉的决定（计划继承或隐性覆盖）

| 出处:行号 | 原句（截短） | 撞计划哪一句 | 严重度 |
| --- | --- | --- | --- |
| `sectest/sectest.py:103-108` | `for marker in ("AbpAdmin:", "Volo.Abp:"): … return t[i:i+80]…`（以内部码存在为判定） | §3.1 第 91 行"`code`：稳定公开码…内部码不返回客户" + A02 | **P0** |
| `sectest/sectest.py:466-468` | `no_egress = … or (r.status == 500 and ("SmsEndpointBlocked" in code or "CredentialNotConfigured" in code))` | 同上；且这是**SSRF/凭据守卫的唯一自动证据** | **P0** |
| `docs/saas-module-test-plan.md:249` | 「`sectest/`（Python 黑盒探针）已覆盖 API 层防线复现…sectest 出报告优先」 | §9-P4 第 448 行"完成第 10 节矩阵"未把 sectest 列为必须一并跑/一并改的对象 | **P0** |
| `.cursor/rules/framework/common/biz-module.mdc:12` | 「改名清单(**全部词根一个不落，漏一处即静默冲突**)：① BizTemplate ② Biz ③ BizProject(s)」 | §8 第 364 行新增 `Client/`、`ClientErrors/`、`Localization/BizTemplateClient/`；§5.2 第 219 行"同一公开码不一致定义必须启动失败" | **P0** |
| `docs/framework-contracts.md:69` | 「## 5. 已知取舍登记（**有意为之，勿当缺陷修**）」（含 `:70` 404 静默、`:76` 监控页透出原始异常、`:85` 模块 Contracts DTO 不进快照） | §4.3 第 173 行、§7.4 第 356 行与这三条同向；但 §9-P4 第 451 行只写"更新 framework-contracts.md…保持管理 404 说明准确"，**未说这张表要新增哪几行**（客户 404 反馈 / 管理 404 静默双口径、公开资源视图、双快照分工） | **P1** |
| `docs/framework-contracts.md:105` | 「别动：§1 表中的保证测试与被它们钉住的实现；…**改这些先改契约测试，让红测试带你走**」 | §5.3.1 第 225 行替换 `AbpExceptionFilter`、§11.1 第 493 行只处理 `AnonymousSettingTrimTests`，**未安排 `AnonymousEndpointSweepTests`**（下表）与 `MaskingPipelineTests` 的次序 | **P1** |
| `backend/test/…/Authorization/AnonymousEndpointSweepTests.cs:95-99` | 「…LocalizationScript **只含公开文案**；ServiceProxyScript 的 API 面与仓库随附的 web/swagger.json 同构，**无边际泄露**」 | §2.1.7 第 54 行"语言资源保留…错误信封与资源目录必须同时治理"——**同一件事在仓里既被当作"已安全"的论据，又被列为待修** | **P1**（裁定见 §4.3） |
| `AnonymousEndpointSweepTests.cs:286` | `templates.Count.ShouldBeGreaterThan(200, "ABP 宿主通常数百条")` | §4.1 第 151 行"完成最终宿主装配中…的分类"——**必登记面下限是 >200 条端点**，不是我第 2 轮给的 64 | **P1**（我的数字自纠，§5） |
| `.cursor/rules/web/abp-frontend.mdc:9` | 「权限来自 `GET /api/abp/application-configuration` 的 `auth.grantedPolicies`，在 `src/access.ts` 映射成 `canManageXxx`」 | §6.4 第 279 行"通用 `application-configuration`…固定下发**公开资源视图**"——措辞只保证资源段，从未把 `auth`/`currentUser`/`features`/`setting` 列为"投影必保留" | **P1** |
| `docs/ledger-f1-decisions.md:20-21`、`docs/abp_admin-ledger-f1-paths.md:80-88` | 六个 `Finance:Adjust:*Denied → 403` 是钉给角色的产品语义；`data.Fields=reason` 与建单同构 | §4.1 默认 Public + §4.3 第 173 行"管理端既有字段形状保持兼容" | **P0**（与 B 表第 1 行合并） |
| `docs/abp_admin-collection-withdraw.md:98` | 「提现侧用户 API 已备（`/api/app/collection-withdraw`），用户端 H5 未在本仓库」 | D5 第 27 行"账务/收款/订单的公开错误随客户 API 接入逐个整理" + §12.1 第 604 行"不先开放客户端点"——**该端点不是"未上线"，而是"已上线但无客户码表"**，全局替换当天它的可操作原因会被收敛成 `Client:OperationFailed` | **P1** |
| `docs/harnessbot-token-gateway.md:46` | 「错误体统一：`{"error":{"code","message","type"}}`；INSUFFICIENT_BALANCE(402)/INVALID_KEY(401)/RATE_LIMITED(429)」 | §1.3 第 42 行已把 TokenGateway/SSE 列为"保持原协议但必须进出口登记"（吸收）；缺的是 402 与无 `data` 形状在 §5.2 `PublicKind` 八值里**没有归属位** | **P2** |
| `docs/dr-runbook.md`（OpenIddict issuer 单域名铁律） | 「切换机房 = 切 DNS，永远不改 issuer」 | §7.1 第 301 行"可信页面 origin 归一化后匹配同源"——同源判定的前提正是这条铁律，计划未列为依据 | **P2** |

---

## 3. B 表 — 计划新规则的下游落点（计划未点名）

| 落点 `path:line` | 现状事实 | 计划哪一条打到这里 | 严重度 |
| --- | --- | --- | --- |
| `web/src/abp/finance.ts:235-245` | `FINANCE_ERROR = { SelfApproveForbidden: 'Finance:Adjust:SelfApproveNotAllowed', SubmitDenied…, ApproveDenied… }`——**管理端按内部码 switch 中文提示**，注释还写明"旧 SelfApproveForbidden 码后端已废弃不再发" | §4.1 第 151 行默认 Public；A17 第 467 行"原管理错误用例继续通过"未含台账 | **P0** |
| `web/src/abp/finance.ts:930-935` | `ADJUST_REQ = { skipErrorHandler: true }`，注释：「页面 onActionError 已按错误码弹中文…skipErrorHandler 关掉全局 403/400 提示」 | ⇒ 码一旦变通用，全局提示又被 skip 关掉 = **用户点了没反应**，正是 `framework-contracts.md:70-72` 对 404 警告过的失效形态 | **P0** |
| `web/src/abp/finance.ts:850-856` | `financeErrorFields()` 兼容 `error.data.fields`（**小写**、逗号分隔）与 `validationErrors[].members` 做表单定位 | §3.1 第 94 行只举了 `data.RetryAfterSeconds`（PascalCase）一例；两种键名形状并存这一事实计划未登记 | **P1** |
| `backend/framework-app/AbpAdmin.HttpApi.Host/wwwroot/libs/abp/jquery/abp.jquery.js:270-281` | `abp.configurationInitialized` 里 `const l = abp.localization.getResource('AbpUi'); abp.ajax.defaultError401.message = l('DefaultErrorMessage401')`…（401/403/404/通用共 9 个键）——**Razor 登录/账户页 AJAX 错误弹窗的唯一文案来源是 `AbpUi` 资源** | §6.6 第 281 行"按资源及键白名单构建"；§6.8 第 283 行只泛说"Razor 脚本需要的公开 UI 文案必须保留"；A20 第 470 行只测"登录/注册/租户选择正常"，**不测错误弹窗分支** ⇒ 白名单漏 `AbpUi` 则登录失败弹窗显示原始键名 | **P1** |
| `backend/framework-app/AbpAdmin.Domain.Shared/AbpAdminDomainSharedModule.cs:64-79` | 同一份 `/Localization/AbpAdmin` JSON 被挂到 **4 个资源**：`AbpAdminResource`（且 `AddBaseTypes(typeof(AbpValidationResource))`）、`SettingUiResource`、`NotificationServiceResource`、`PaymentServiceResource` | §6.1 第 276 行"客户资源不能继承/挂载包含内部错误模板的资源" + §6.6 第 281 行按资源+键白名单 ⇒ **按资源裁剪会连带裁掉设置页/通知/支付的显示名，按目录裁剪会把内部码带进公开视图**；A20 未覆盖 `pages/settings` | **P1** |
| `:81`（同文件）`options.DefaultResourceType = typeof(AbpAdminResource)`；`:25-28` `options.Languages.Add(...)` **共 20 个语言** | 语言宇宙是**代码里注册的 20 项**（不是 3 项），且 `DefaultResourceType` 就是混有 158 个内部码的那个资源 | §5.4 第 268 行"显式 `FallbackCulture` 必须属于公开资源支持集"——"支持集"未定义指向哪一个集合（`AbpLocalizationOptions.Languages` 20 项 / 前端 `culture.ts` 3 项 / 客户资源 3 项） | **P1** |
| `backend/modules/platform/AbpAdmin.Collection/Services/CollectionNetworkAdminAppService.cs:523-530` | 方法注释：「配置 RPC 连接串（**含项目密钥；加密入库，明文不落库不留日志**）」；下一行 `throw new BusinessException(CollectionErrorCodes.InvalidRpcUrl).WithData("RpcUrl", rpcUrl);` | 注释与实现直接矛盾：**含密钥的原串进了会回客户端的 Data**（§2.1.12 第 59 行已承认框架默认回传 Data）。属存量缺陷，本期 A03 夹具应收录 | **P1（存量）** |
| `backend/framework-app/AbpAdmin.Domain.Shared/RateLimiting/AbpAdminOperationRateLimitingException.cs:8-13` | 「Data 字典包含以下键（**前端与运维都依赖它们**）：PolicyName、MaxCount、CurrentCount、RemainingCount、RetryAfterSeconds、RetryAfterMinutes、RetryAfter、WindowDurationSeconds、WindowDescription、RuleDetails」+「ExtraProperties 每项都会被复制进 Data…不要放密钥」 | §3.2 第 110 行只处理"等待秒数"；**计划从未点名其余 9 个键对 Public 必须裁剪**，而它们今天就出现在匿名可达端点的 429 上（`AnonymousEndpointSweepTests.cs:127-134` 白名单含 `send-phone-number-confirmation-code`） | **P1** |
| `web/nginx.conf:56-60` | `client_max_body_size 210m;` + 注释「大文件在 nginx 层先被 413 掐掉，**后端限额根本没机会生效**」 | §7.3 第 348 行"上传适配…覆盖 413/415"、A25 第 475 行 ⇒ 生产的 413 是 **nginx 产出的 HTML 页**，不经过应用；A25 在应用层夹具会通过而在生产不成立。§12.1 第 607 行把网关列为"另行核查"，但 §7.2 第 331 行"非标准网关错误一律本地兜底"**没有指定判定方法** | **P1** |
| `web/src/abp/identity.ts:303`（及同文件另三处 `window.open`） | 直接 `window.open('/api/app/identity-user-admin/export…')`、import-template、import-failure-report、export-file 共 **4 个直开出口** | §5.3 第 242-245 行的文件清单只列 `UserExportController`/`AuditLogExportController`/`FileShareController`/`GdprDownloadController`；`import-template`、`import-failure-report/{id}`、`export-file/{id}` 三个动作未点名 | **P2** |
| `web/src/requestErrorConfig.ts:94`（及 `:104`、`:139` `title: 'HTTP ${status}'`） | 管理端把 `details` **优先于** `message` 上屏；`:139` 还把状态码拼进通知标题 | §4.3 第 173 行"保留…原结构"与 §3.1 第 93 行"客户端 details 始终 null"两态并存；A17 未断言管理 `details` 仍可得 | **P2** |
| `.github/workflows/ci.yml`（无 `sectest` 作业）＋ `sectest/docker-compose.sectest.yml`（存在） | 探针不在 CI 里，靠人工跑 | §9-P4 第 449 行"执行第 11 节现有 CI 检查" ⇒ **sectest 不在任何自动门禁里**，因此它的失效不会被 CI 抓到，只会以"安全回归看不见"的形式发生 | **P1** |

---

## 4. C 表 — 三个来源合读才成立的断链

### 4.1 「默认 Public」× 前端按内部码 switch = 管理端功能当天失效（P0，与 B 表前 3 行同一条）
`docs/abp_admin-ledger-f1-paths.md:80-88`（六个 403 码是产品语义）× `web/src/abp/finance.ts:235-245`（前端拿码 switch 中文）× `web/src/abp/finance.ts:930-935`（这些请求 `skipErrorHandler:true` 关掉了全局提示）× 计划 §4.1 第 151 行（未登记默认 Public，含 Development）× 计划 §4.2 第 166 行（不把"已登录"等同管理者）。
⇒ **台账/调账/提现管理页在全局替换提交当天，写操作失败只剩一条兜底文案，且全局提示被 skip 关掉**。计划 §4.3 承诺保留管理语义、§9-P0 第 384 行也建了登记表，但**登记义务的具体对象只举了 SettingUi 一例**。
**改法（具体到句）**：§9-P0 第 384 行的任务里加一句"**首批必登记集合按消费者反查**：`grep -rn "FINANCE_ERROR\|error.data.fields\|errorCode ===" web/src` 命中的每个码族（`Finance:Adjust:*`、`Collection:Withdraw:*`）必须先登记 AdminCompatibility，并在 A17 增加"按公开码 switch 的管理页面在替换后文案不变"这一类用例"。理由：消费者驱动比"全端点分类"早收敛，且能直接测。

### 4.2 「样板改名清单」不含新词根 × 启动失败规则（P0）
`biz-module.mdc:12` 三个词根 + "漏一处即静默冲突" × `backend/AGENTS.md:15`"新增业务模块 = 复制 Biz.Template 改名" × 计划 §8 第 364 行新增的 `Client/`、`ClientErrors/`、`BizTemplateClient` 资源、`BizTemplate:Public:` 前缀、`BizTemplate:EnableClientErrorSample` × 计划 §5.2 第 219 行"同一公开码不一致定义必须启动失败"。
⇒ **计划把 Biz.Template 变成了"复制即冲突"的样板**，且 §9-P4 第 451 行的文档更新清单不含 `.cursor/rules/**`，规则会长期停留在三个旧词根。
**改法**：① §8 末尾加一条"同步 `.cursor/rules/framework/common/biz-module.mdc:12` 改名清单，新增词根 `Client`（目录/命名空间）、`BizTemplateClient`（资源名与 `Localization/BizTemplateClient` 目录）、`<模块>:Public:`（公开码前缀规范）、`EnableClientErrorSample`（开关名）"；② A18 的"模板可复制/移除"改成**真复制一次再启动**的断言（现有措辞测不出）。

### 4.3 「LocalizationScript 只含公开文案」这句安全论证（裁定，不是简单选边）
`AnonymousEndpointSweepTests.cs:95-99` 把"LocalizationScript 只含公开文案"当作匿名可达的**可接受性依据**；计划 §2.1.7 第 54 行说语言资源保留 = 待治理；ABP 10.7.0 `AbpApplicationLocalizationScriptController.cs:60` 实际是 `$.extend(true, abp.localization, JsonSerializer.Serialize(localizationDto))`。
⇒ **两边都对了一半，也都没说清是哪一半**：ABP 的 `ApplicationLocalizationDto` 同时含 `Resources[].Texts`（**键名 + 值**）与 `Values`。值的语义确实是人话（我第 1 轮实测 `zh-Hans.json:160-165`），但**键名就是内部码本身**（158 个 `AbpAdmin:*` + 各模块码），且值可被管理员经语言管理页改写。所以测试注释在"键名"上不成立，计划 D1 在"值"上不必收紧到"整个资源不可达"。
**改法（三句）**：① 该测试注释补成"只含公开**文案值**；键名仍为内部码形态，本期由公开资源视图治理"；② A07 第 457 行的断言从"不可取得内部模板"改成**两条可判定的**："匿名 JSON/脚本中不出现任何 `<命名空间>:<名>` 形态的资源键名" + "出现的所有值均来自登记为公开的键白名单"；③ §6.2 第 277 行保留，但把"当前管理资源有默认英语回退"的适用范围写明（见 §4.4）。

### 4.4 我对子任务一条结论的校正（避免把对方的错带进计划）
子任务判"计划 §6.2 的回退链结论对 `FriendlyBusinessExceptionMiddleware` **不成立**（它不走 ExternalLocalizationStore 回退链）"。**这条我回读后判为误**：该中间件用 `IStringLocalizer<AbpAdminResource>`，`AbpAdminResource` 以 `"en"` 为基名注册（`AbpAdminDomainSharedModule.cs:64`），ABP 的 `AbpDictionaryBasedStringLocalizer.cs:154-156` 默认文化回退与 `AbpLocalizationOptions` 的 `TryToGetFromDefaultCulture=true`（构造函数）同属 `IStringLocalizer` 路径，**与是否经过外部资源库无关**。⇒ 处置表 Q11 的驳回继续成立，**不要**按子任务那句去改 §6.2。剩下的真风险只有"整条回退链都缺键"，已被计划第 277 行覆盖。

### 4.5 网关/OpenIddict 的"错误体"落在 `PublicKind` 八值之外（P2）
§1.3 第 42 行要求 TokenGateway/SSE 进登记（吸收），但 §5.2 第 217 行的 `PublicKind` 八值里没有 402/额度不足的归属位，§3.1 信封也没有 `type` 字段的位置。⇒ 登记后这些出口只能标"原协议/非目标"，而 §151 第 151 行说"只有明确的非目标原协议条目允许保持其既有协议，不得以豁免绕过客户信息过滤"——**两句之间缺一条"非目标条目仍要断言其自身安全"的验收**。A30 第 480 行已经写了这条断言，实际闭合，只差点名"非目标 ≠ 无验收"。

### 4.6 规则示范代码 = 计划要禁的写法，而规则不在更新清单（P1）
子任务给出 `.cursor/rules/framework/common/abp-core.mdc:139/143/154/180` 四条（`WithData("Name", bookName)` 示范、`MapCodeNamespace` 接线、"Always localize user-facing messages and exceptions"、"Manual HTTP calls from UI" 反模式表）与 `application-layer.mdc:164`（`EntityNotFoundException(typeof(Book), id)` 暴露类型+主键）。**我本人未逐条回读这 5 处**（见 §6），但 `backend/AGENTS.md:4-5` 确实把 agent 引向 `.cursor/rules/framework`，而计划 §9-P4 第 451 行只更新 `framework-contracts.md` 与 `web/AGENTS.md`。⇒ 若子任务行号属实，**后续任何按规则写的模块都会新增计划要禁的泄露点**；请按"回读后补 §9-P4 清单"处理，不要直接照抄我这行。

---

## 5. 规模数字自纠（这是我第 2 轮给的数，本轮被上游证据推翻）

第 2 轮我在 §6 表里写"必登记面 = 带 `[Authorize]` 的 AppService 文件 **64**、权限串 ≈167"。本轮证据：`AnonymousEndpointSweepTests.cs:286` 断言宿主端点表 `>200` 条并注释"ABP 宿主通常数百条"，而仓内自有 AppService 类合计约 66 个（框架 47 + 模块 19，含 1 个抽象基类）⇒ **差额来自 NuGet 包（Volo.Abp.\*、EasyAbp.\*）自己的 `ConventionalControllers.Create`**，它们的类型在仓内源码里枚举不到，但计划 §151 第 151 行要求"最终宿主装配中…全部"分类，§153 第 153 行的登记键又要求"具体控制器/服务类型全名"。
⇒ 我那个 64 是**候选生成面**，不是**必登记面**；真实下限是 **>200 条端点，其中约 2/3 的类型定义不在本仓**。
⇒ 更要紧的推论：`AnonymousEndpointSweepTests.cs:295-296` 的现有枚举套路是 `dataSource.Endpoints.OfType<RouteEndpoint>()`，而计划 §153 第 153 行依赖 `EndpointDataSource` + `IActionDescriptorCollectionProvider` 双枚举——**我没有实测这两条枚举各自漏掉哪些形态**（SignalR hub 端点、`MapHealthChecks`、静态资源端点是否属 `RouteEndpoint`）。请把这条实测放进 §9-P0，写成"dump 端点表并分类各形态计数"，而不是沿用现成 `OfType<RouteEndpoint>()`。

**同时保留我第 2 轮的纪律建议**：这些数字一律作工作量输入，不进验收门槛。

---

## 6. 已回读 / 未回读清单（子任务结论的处理结果）

| 条目 | 状态 |
| --- | --- |
| `sectest/sectest.py:99-110、464-470`、`docs/saas-module-test-plan.md:249`、`biz-module.mdc:8-18`、`finance.ts:235-245/850-856/930-935`、`abp.jquery.js:270-281`、`AbpAdminDomainSharedModule.cs:60-115`、`framework-contracts.md:60-110`、`nginx.conf:55-63`、`CollectionNetworkAdminAppService.cs:520-532`、`AnonymousEndpointSweepTests.cs:95-135/286-300`、ABP `AbpApplicationLocalizationScriptController.cs:60` | **已本人回读**（本报告 A/B/C 表中的 P0/P1 全部落在这批之内） |
| `Program.cs` 入口签名、`HostTestBed.cs:10-15`、`HostUnderTest.cs:61-67` | 未回读（第 2 轮我已回读过 HostUnderTest 的 Development 固定与"不复用已 Build 集合"，其余不采信） |
| `.cursor/rules/.../abp-core.mdc:139/143/154/180`、`application-layer.mdc:164`、`multi-tenancy.mdc:151` | 未回读，见 §4.6，只作"待补清单"提出 |
| `AbpAdminOperationRateLimitingException.cs:8-13`、`ImageProcessingBusy*`、`OperationRateLimitingChecker.cs:272-287` | 已回读（第 1、2 轮全文读过） |
| `wwwroot/libs/abp/core/abp.js:76-131`、`MaskingPipelineTests`、`SettingUiMaskingTests`、`web/swagger.json` 内容统计 | 未回读 |
| `Pages/Account/Login.cshtml:26-27` 内联 `fetch`、`culture.ts`/`tenant.ts` 同 origin cookie 交叉污染 | 未回读——**这条如果属实就是 P1**（计划 §7.1 第 301-303 行的"复用既有语言与租户约定"会让客户 transport 在同 origin 上改写管理 SPA 的文化/租户 cookie），请实施第一天实测 |

---

## 7. 处置建议（按可执行顺序，全部是"加登记/加断言"，不动已定方向）

1. **§9-P4 第 451 行的文档更新清单**补三项：`.cursor/rules/framework/common/biz-module.mdc`（改名清单）、`sectest/`（判定函数改为公开码 + 新增"内部码不得出现"的正向断言）、`AnonymousEndpointSweepTests.cs` 的 LocalizationScript 安全论证句。**成本各 1-3 行；不补则本期一落地就有 1 条安全回归失明、1 类模块复制启动炸。**
2. **§9-P0 第 384 行**加"首批必登记集合按前端消费者反查"（§4.1 的 grep 口径），并在 **A17 第 467 行**加一条"按内部码 switch 的管理页面在替换后文案与字段定位不变"。
3. **§6.6 第 281 行**把白名单的最小必含项点名：`AbpUi`（9 个 `DefaultErrorMessage*`）、`AbpValidationResource`（因 `AbpAdminResource` `AddBaseTypes` 它）、以及 `/Localization/AbpAdmin` 同一目录被挂到的另 3 个第三方资源（SettingUi/NotificationService/PaymentService）；**A20 第 470 行**加"登录失败弹窗分支"与"设置页显示名不缺失"两条。
4. **A07 第 457 行**改写为 §4.3 的两条可判定断言（键名形态 + 值白名单）。
5. **§7.3 第 348 行 + A25 第 475 行**加"经代理的 413 是 HTML 页，客户 transport 必须按 §7.2 本地兜底"，并把 §7.2 第 331 行"非标准网关错误"给出**判定方法**（如：content-type 非 JSON 且 body 起始匹配 `<`/`<!doctype` → 归 network，不解析、不显示）。
6. **§5.4 第 268 行**"公开资源支持集"改为显式指向"客户资源登记的文化集合"，并注明它不等于 `AbpLocalizationOptions.Languages`（现注册 20 项，`AbpAdminDomainSharedModule.cs:83-102`）。
7. **§9-P0 第 386 行**把端点枚举改为"先 dump 并按形态计数（RouteEndpoint / Hub / 静态 / 健康检查 / minimal-API）"，作为 §153 登记键的依据（§5 推论）。
8. 存量缺陷单独登记（不进本期范围也不掩盖）：`CollectionNetworkAdminAppService.cs:530 WithData("RpcUrl", rpcUrl)` 与它自己 `:523` 的"明文不落库不留日志"矛盾，建议**立即单独修**（只回显长度/掩码），因为它与本仓 `pg-migration-runbook.md` 的"连接串永不回明文"口径同类。

---

## 8. 本轮我认可的新东西（扩大范围后才看得见）

1. 计划第 15 行「只借鉴上述机制，**不迁移 exchange 的响应包装或"HTTP 200 + body 状态码"用法**」——这句把我第 2 轮 Q18 的争点**从文档层面直接关掉了**，比我要求的"加一句移植偏差断言"更彻底。
2. 计划第 42 行把 Hub/TokenGateway/SSE **主动纳入登记义务**同时保留原协议——这正是 §4.5 缺的那半步，方向对。
3. 计划第 250 行「**不以全仓 grep 没有显式调用来判断管线不存在**」——这句把我第 1、2 轮栽了两次的那类失误（把"没搜到"当"不存在"）写成了纪律，且它紧跟在我第 2 轮自纠之后出现。
4. `HostUnderTest.cs` 的设计注释（"每个测试必须看同一次启动，否则副本漂移而测试常绿"）说明这个仓**已经有人在管装配漂移**这个问题；计划 §11.1 第 498 行接上了它，而不是另起炉灶。

---

## 9. 审核局限

1. **未运行任何东西**：`sectest/` 一次都没跑（我只读它的判定函数源码），因此"计划落地后 sectest 具体哪几条探针会翻红"是**静态推断**；请按 §7 第 1 条改成"先跑一次基线留档"。
2. **端点面规模是下界**：`>200` 来自 `AnonymousEndpointSweepTests.cs:286` 的断言阈值，不是实测计数；NuGet 包生成的端点数未测（需真实 boot）。
3. **ABP 侧结论通道**：`AbpApplicationLocalizationScriptController.cs:60`、`AbpExceptionHandlingOptions` 构造函数、`AbpDictionaryBasedStringLocalizer.cs:154-156`、`AbpLocalizationOptions` 构造函数均按 tag `10.7.0`/`raw.githubusercontent` 原文读；本地包只有 DLL/pdb（无嵌入源、XML doc 无相关成员），结构类结论无法由本地通道支持。
4. **子任务结论的污染控制**：见 §6 表；其中 `.cursor/rules/**` 那 5 条我未回读，明确不作为 P1 依据，只作为"待补清单"提出。
5. **他人报告**：未读其内容；§1 披露的那条引用行只含一句概括性转述，本轮无一条结论依赖它。
6. 时变读数：计划与处置表 mtime/size/sha、`git status`、HEAD 于 16:14:14 复测；本报告 §2/§3 的行号对 `c5f78192…` 有效。若 `sectest/`、`.cursor/rules/`、`finance.ts` 在开工前被改动（它们是活跃文件），第 4.1/4.2 条需重新命中一次再动手。
