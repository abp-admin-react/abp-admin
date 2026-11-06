# 《abp_admin 客户端错误处理开发方案》独立审核报告（qwen）

- 被审文档：`docs/client-error-handling-development-plan.md`（版本 1.0，2026-11-05）
- 取证时刻指纹：`mtime=2026-11-05 13:37:54`、`size=45466`、`sha256=5bbfaf82ed9db901a1c000544f7507daaaf5b15e6fe4e9ff436b01a49b1e4ad7`；14:49:17 复测同一串，**审核期间计划正文未被改动**（本报告的行号对该哈希版本有效）。
- 配套可视化：`C:/Users/Administrator/.cursor/projects/e-code-abp-admin/canvases/client-error-policy.canvas.tsx`（与计划正文有 1 处行号分歧，见 §4.1）
- 审核方式：全部结论为第一手取证（读源码 + 复跑测试 + 复算资源 JSON + 对 ABP 10.7.0 tag 源码逐文件读）。未访问生产，未修改任何文件。
- 仓库基线复测：ABP `10.7.0`（`backend/Directory.Packages.props:15`）、`net10.0` 全仓唯一、HEAD `69bbbe5b`（2026-11-01）、`git status` = `M docker-compose.yml` + 计划文件本身未跟踪。

---

## 0. 独立性声明

1. 我**没有打开** `docs/client-error-handling-development-plan-review-grok.md` 与 `...-review-hy4.md`。这两份文件是在我本轮 `find` 列目录时**以文件名的形式**进入视野的（会话开始时 `ls docs/` 还没有它们，说明是并行会话在我取证期间写入的）。文件名不含结论，因此不存在结论污染；但为可交叉验证，我在此如实披露时序。
2. 子任务在检索后端时命中了 `.cursor/rules/`、`backend/AGENTS.md`、`web/AGENTS.md` 的全文（仓库自身规范，非他人结论）——这些是我**应当**读的，因为计划 §2.2 要求遵守它们。
3. 本项目记忆索引里有条目标题涉及其他主题的审核结论，与本文档无关，未采用。
4. 本报告不引用、不评价另外两份报告的任何结论；行号分歧只针对计划正文与 canvas。

---

## 1. 摘要与结论

**总判定：计划的技术方向、失败模式识别和验收设计是这个仓库里少见的严谨度；可以开工，但开工前必须改掉 2 条 P0 与 4 条 P1。**

我逐条实地核验了计划的全部事实断言（§2 表）：**44 条里 34 条 VERIFIED、7 条 PARTLY（行号或口径偏移，结论成立）、3 条 CONTRADICTED（会诱导实现者写错）**。计划提出的 6 项取舍、默认 Public 的 fail-closed 姿态、"前端不是唯一防线"、"不许用 ShowAllErrors 类开关"、"投影不得修改原异常对象"这些方向，我复核后**全部支持**，并且有两处我能给出比计划更强的证据（§9）。

**3 条 CONTRADICTED 不是判断错误，是"把 ABP 的机制形状写错了"，它们都会变成实现里的真 bug：**

| 编号 | 计划句子 | 实测结论 |
| --- | --- | --- |
| P0-1 | §5.3「必须逐一接入并增加覆盖的现有文件」（7 项清单） | **清单漏掉了两个 `AllowAnonymous` + `FileResult` 的出口**（`FileShareController`、`GdprDownloadController`）。它们正是计划自己认定的高危组合，也是今天唯一会把 `Exception.Data` **整包**吐给匿名调用者的路径。 |
| P0-2 | §6.9「默认语言键、占位参数、继承闭包及目录冲突在启动阶段校验」+ §6.1「首期覆盖现有前端支持的简中、繁中、英文」 | 现有 `AbpAdminResource` 的 20 个文化文件里，**只有 zh-Hans/en 有错误码文案（各 158 个），zh-Hant 只有 4 个键、de-DE 等 13 个各只有 37 个键且 0 个错误码**。按字面实现"启动校验默认语言键完整"会让宿主起不来；按"现有前端支持的语言"理解则繁中基线是零。 |
| P1-1 | §5.3.3 + 第 221 行「当前 Host 未显式使用 `UseAbpExceptionHandling`」 | 该句字面为真但结论会误导：**`app.UseUnitOfWork()`（Host `:1516`）内部第一件事就是 `UseAbpExceptionHandling()`**（ABP `AbpApplicationBuilderExtensions.cs:108-113`）。所以这条出口今天**已经在管线里**，且位置在计划打算"收敛"的那个中间件**更内层**。 |

另有 2 条属于"计划没写、但会决定实现对不对"的口径未定（PublicKind↔前端 kind 的推导规则；401/403 是否必须有 ABP 信封——今天它们**只有状态码、没有 body**），见 §5。

**规模实测（给执行者当输入，不要当验收门槛）**：管理端点登记要覆盖 64 个带 `[Authorize]` 的 AppService 文件、8 个手写控制器、约 167 个权限串、158 个已有错误码文案、55 个带占位参数的文案。计划 §4.1 的"未分类默认 Public" + §9-P0 的登记表，实际是**一次全仓管理端语义重扫**，而文中没有任何一处给出这个量级或批量归类策略（§6）。

---

## 2. 事实核验表（计划断言 → 实测 → 裁定）

### 2.1 计划 §2.1「可借鉴与必须补齐」

| # | 计划断言 | 实测证据 | 裁定 |
| --- | --- | --- | --- |
| 2.1.1 | exchange H5 按码翻译、未知错误统一兜底；`request.ts` 独立附加业务码 | `Web-H5/src/lib/tradeErrorMessage.ts:166`（`humanizeTradeError`）、`:176-179`（按 `trade.error.${code}` 取译文）、`:62`/`:67`（兜底键与兜底英文）；`Web-H5/src/api/request.ts:101-106`（先取码再 humanize） | **VERIFIED**（但"统一兜底"实为三层 + 一个 4 码硬编码屏蔽集 `:56`/`:175`，见 §7.1） |
| 2.1.2 | exchange 后端仍发送原始错误文本，H5 只是接收后替换 | `AdminResultProvider.cs:143` `Message = msg is null or string ? (msg + "") : JSON.Serialize(msg)`；`:26-28` 直接把 `metadata.Errors` 塞进去；**脱敏代码存在但整段被注释掉**（`:130-138`）；实测测试夹具里输入就是 SqlSugar 连接串原文（`tradeErrorMessage.test.ts:25-29`） | **VERIFIED**（比计划写的更糟：不是"没做脱敏"，是"做了又注释掉"） |
| 2.1.3 | exchange 把"存在翻译"部分等同于"允许客户看" | 后端 `ExErrorCodeEnum.cs` 587 个成员、无任何可见性属性；`en.json` 的 `trade.error` 只有 171 键；`tradeErrorMessage.ts:44-45` 注释逐字写着"不要写进十语 `trade.error`，否则客户会看到「用户Id不合法…」" | **VERIFIED** |
| 2.1.4 | 管理前端优先展示 `validationErrors/details/message`；`skipErrorHandler` 直接抛原始异常；404 全局静默是既有契约 | `web/src/requestErrorConfig.ts:94-97`（优先级链）、`:87` `if (opts?.skipErrorHandler) throw error;`（纯 rethrow，被 `requestErrorConfig.test.ts:84` 钉住）、`:126-137`（404 静默）、`:52-57`（`isNotFound`）；`docs/framework-contracts.md:70` 已登记该取舍 | **VERIFIED**（行号见 §3.1 勘误） |
| 2.1.5 | `FriendlyBusinessExceptionMiddleware` 独立构造 ABP 信封并返回 `business.Data`；只改默认 ABP 出口会漏掉它 | `:60-65` 只接管**带非空 Code 的 BusinessException**，其余原样 `throw`；`:84-94` 手写信封；`:91` `data = business.Data`；`:74` `message = localizer[business.Code].Value` | **VERIFIED** |
| 2.1.6 | `IExceptionToErrorInfoConverter` 同时用于 HTTP 和审计，不得全局替换 | ABP 10.7.0 `AuditLogInfoToAuditLogConverter.cs:26` 构造注入该转换器、`:72` 对 `auditLogInfo.Exceptions` 逐个 `Convert`、`:80-82` 序列化落库；HTTP 侧 `AbpExceptionFilter.LogException` 用同一个 `Convert` | **VERIFIED（且更强，见 §9.1）** |
| 2.1.7 | 现有匿名配置裁剪只处理 settings，语言资源保留；JSON 与脚本均有可达出口 | `AnonymousSettingTrimApplicationConfigurationContributor.cs:21-22` 注释自证"只清空 `setting.values`；localization、features、clock…全部保留"、`:44` `Setting?.Values.Clear()`、`:42` 门控是 `!currentUser.IsAuthenticated`（**按登录态，不按路径**）；`PreLoginApiEndpointsConvention.cs:23-38` 把 6 个框架控制器标匿名（含 JSON、两类脚本） | **VERIFIED** |
| 2.1.8 | `AbpAdminDomainModule` 给所有资源追加数据库语言贡献者 | `:169` 注释 + `:177` `foreach (var resource in localizationOptions.Resources.Values)` + `:179` `resource.Contributors.Add(new DbLocalizationResourceContributor(...))` | **VERIFIED**（但见 §5.4 的一个顺序坑） |
| 2.1.9 | 新语言目录必须补进两个项目的 `EmbeddedResource Include`，否则发布包丢文案 | 全仓 csproj 本地化 glob **全是单层**：`AbpAdmin.Domain.Shared.csproj:44` `Localization\AbpAdmin\*.json`、`AbpAdmin.Biz.Template.csproj:38` `Localization\BizTemplate\*.json`（另 Ledger/Collection/Ordering/Webhooks 同形）；**没有任何 `**/*.json` 形式的本地化 glob**。ABP 官方 app 模板同样单层 | **VERIFIED（真实风险，不是已被现有 glob 兜住）**。"两个项目"= Domain.Shared + Biz.Template，与 §5.1/§8 的落点自洽（业务/平台模块为单工程，没有第二份 Domain.Shared） |
| 2.1.10 | 当前 Umi 每请求拦截器临时挂在共享 axios 实例上，不用于隔离客户请求 | 机制确实存在于生成物 `web/src/.umi/plugin-request/request.ts:192-236`（每次请求 `interceptors.request.use` → 响应后 `eject`）；**但全 `web/src` 无任何调用方传 `requestInterceptors`** | **PARTLY**：结论（别用它做客户隔离）成立；"当前这样挂着"是 umi 的能力而非现状用法 |

### 2.2 计划 §5.1/§2.2 分层与落点

| 断言 | 实测 | 裁定 |
| --- | --- | --- |
| 能力落 `framework-app/`，`HttpApi` 不引用 `Application` 实现 | `AbpAdmin.HttpApi.csproj:19-20` 只引用 `Application.Contracts` + `Domain`；`AbpAdmin.Domain.csproj:12` 只引用 `Domain.Shared` | **VERIFIED**：§5.1 的落点（契约在 Domain.Shared、投影核心在 Domain、HTTP 在 HttpApi、`ClientFailureDto` 在 Application.Contracts）**能被现有引用图承载，不需要新增 ProjectReference** |
| 基线 `.NET 10 / ABP 10.7.0` | `Directory.Packages.props:15` `<AbpVersion>10.7.0</AbpVersion>`、`:3` CPM、`:22` `MicrosoftVersion 10.0.13`；全仓 TargetFramework 唯一值 `net10.0`；无 `global.json` | **VERIFIED** |
| 保留已有用户改动（`docker-compose.yml`） | 14:49 复测 `git status --porcelain` = ` M docker-compose.yml` + `?? docs/client-error-handling-development-plan.md` + `?? .workbuddy/` + 两份他人报告；改动内容是新增 `Identity__AdminPassword: ${IDENTITY_ADMIN_PASSWORD:-}` 插值与两处注释 | **VERIFIED**，且在飞面比计划说的更小（只有 1 个文件） |

### 2.3 计划 §11/§13 测试与命令

| 断言 | 实测 | 裁定 |
| --- | --- | --- |
| exchange 2 个针对性测试文件共 21 项通过 | 实跑 `node scripts/vitest-run.mjs run src/lib/tradeErrorMessage.test.ts src/pages/auth/register/registerErrorMap.test.ts` → `Test Files 2 passed` / `Tests 21 passed (21)`（静态计数 14+7，无 `.each`） | **VERIFIED（一字不差复现）** |
| abp_admin `requestErrorConfig.test.ts` 22 项通过 | 实跑 `pnpm exec vitest run src/requestErrorConfig.test.ts` → `✓ src/requestErrorConfig.test.ts (22 tests)` / `Tests 22 passed (22)`（静态 20 个 `it` + `it.each([403,500])` 展开 2） | **VERIFIED（复现）** |
| `HostTestBed` 只初始化宿主/端点表，不启动真 HTTP | `HostTestBed.cs:43-45` `builder.Build()` + `InitializeApplicationAsync()`，无 `RunAsync`；全仓 `WebApplicationFactory\|TestHost\|TestServer` 只命中一处注释（`AnonymousEndpointSweepTests.cs:24`：宿主级 TestServer 需要 Redis/DB 齐活）；`HttpApi.Host.Tests.csproj` 无 Mvc.Testing 包 | **VERIFIED** |
| §11.1 五个测试落点路径 | `Application.Tests/Localization/LanguageTextAppServiceTests.cs`、`HttpApi.Host.Tests/ApplicationConfiguration/AnonymousSettingTrimTests.cs`、`EntityFrameworkCore.Tests/EntityFrameworkCore/Applications/EfCoreLanguageTextAppServiceTests.cs` 全部**按计划的相对路径精确命中** | **VERIFIED** |
| §11.2 命令与 CI 对应 | `.github/workflows/ci.yml:72` `dotnet test backend/AbpAdmin.slnx --nologo` ✓、`:97` `pnpm test` ✓、`pnpm run lint` = `biome:lint && tsc`（`web/package.json:12`）✓、`engines.node>=22` 与本机 `v24.18.0`/`pnpm 10.34.5` 匹配、`web/node_modules` 存在 | **VERIFIED**；但见 §8.3（CI 还有 7 条迁移检查与 2 个 docker 脚本，计划未提；CI **不跑** `pnpm lint`） |
| §13 引用链接可达 | 4 条 ABP 10.7.0 raw URL **全部按 tag `10.7.0` 命中**（`rel-10.7.0`/`v10.7.0` 是 404） | **VERIFIED** |

---

## 3. 镜头 A：可核验性与引用精度

### 3.1 行号勘误（3 处，均不影响结论，但会影响将来的排查）

| 计划/canvas 的引用 | 那一行实际是什么 | 支撑该断言的真实行号 |
| --- | --- | --- |
| 计划 §13「管理前端错误入口 `requestErrorConfig.ts:73`」 | `:73` 是 `export const errorConfig: RequestConfig = {`（入口指针，可接受） | 优先展示顺序在 `:94-97`（403 分支里还复制了一遍 `:103-107`） |
| canvas「优先显示 validationErrors/details/message → `:92`」 | `:92` 是 `const abpError = error.response?.data?.error` （读取载荷，不是顺序） | 同上 `:94-97`。**计划与 canvas 在这一点上各引一半**：计划 :73、canvas :92，同一断言两个数字都对不上顺序本体——建议统一写成 `requestErrorConfig.ts:92-97` |
| 计划 §13/§2.1.5「业务异常兜底 `FriendlyBusinessExceptionMiddleware.cs:60`」 | `:60-65` 是**接管条件守卫**（"只接管带码 BusinessException"） | 构造信封是 `:84-94`，返回 `business.Data` 是 `:91`，状态码逻辑 `:67-71`，本地化取文案 `:73-74`。断言成立，但如果将来按 :60 找"信封在哪"会找错 |

另外两处**口径**需要收紧（不是行号）：

- §3.2 `Client:RateLimited`「429；保留安全 `Retry-After`/等待秒数」——今天管理前端读的是 **ABP `error.data.RetryAfterSeconds`**（`requestErrorConfig.ts:117`），**不是 HTTP `Retry-After` 响应头**；而 503（图片繁忙）走的是**响应头**（`ImageProcessingBusyExceptionFilter.cs:20` `Response.Headers.RetryAfter`），且管理前端**没有 503 分支**。计划把两者并列写在一个公开码语义里，实现者会不知道该产 header 还是 data。**改法**：§3.2 该行与 §7.2 的 `retryAfterSeconds` 各加一句"来源=响应体 `error.data.RetryAfterSeconds`；HTTP `Retry-After` 头仅对 503/429 由服务端补，前端不把它作为唯一来源"，并给 A09 加一条"503 的等待秒数在 header 与 data 中至少一处可得"。
- §3.1 `validationErrors`「保持 ABP 项结构」——ABP 10.7.0 的真实项形状是 `RemoteServiceValidationErrorInfo { string Message; string[] Members }`（`DefaultExceptionToErrorInfoConverter.cs:302-307`：`MemberNames` → camelCase 字符串数组）。计划**没有写错**，但请显式写一句"不是 ASP.NET Core `ValidationProblemDetails` 的 `errors[fieldName][]` 形状"。理由：`web/src/requestErrorConfig.ts:24-31` 里现有的 `AbpError` 类型**根本没声明 `members`**，实现者极易按熟悉的那种形状造一个解析器，结果永远解析为空。顺带一个正面结论：ABP 会把 `Members` camelCase，与 §3.1「客户端新字段统一 lowerCamelCase」天然对齐。

---

## 4. 镜头 B：ABP 10.7.0 机制可行性（逐条读官方 tag 源码）

计划最有价值的部分是它没有停在"加个 filter"，而是逐个出口分类。我按 10.7.0 源码复核了每个接线点。

### 4.1 ✅ 成立且我能补上缺的证据

- **§5.3.1「替换原 filter 服务类型，不并列挂第二套」**：可行，而且是唯一干净钩子。10.7.0 用 `AbpMvcOptionsExtensions.cs:58-67` 的 `options.Filters.AddService(typeof(AbpExceptionFilter))`（MVC 过滤器列表最后一项）注册；`AbpExceptionFilter : ITransientDependency`，ABP 的约定注册走 `Add`（非 `TryAdd`），`ExposedServiceExplorer` 默认 `IncludeSelf=true` ⇒ **`AbpExceptionFilter` 自身就是可解析的服务类型** ⇒ `services.Replace(ServiceDescriptor.Transient<AbpExceptionFilter, ClientErrorExceptionFilter>())` 生效。请把这句注册形态写进 §5.3.1，否则实现者会去挂第二个 `IExceptionFilter`（计划正是禁止这个）。
- **§5.3.1 的措辞要改一处**：`ShouldHandleException`/`HandleAndWrapException` **不是扩展方法**，是 `AbpExceptionFilter` 的 `protected virtual` 实例方法（`:35`、`:63`；另有 `LogException` 在 `:97`）；10.7.0 里不存在 `AddAbpExceptionHandling` 这样的注册扩展（全 tree 0 命中）。计划第 202 行「用测试锁定…扩展行为」这句里的"扩展"如果读成"extension method"，实现者会去找一个不存在的类。**建议改成"用测试锁定 `AbpExceptionFilter.ShouldHandleException`/`HandleAndWrapException` 这两个 protected virtual 的判定结果"**。这也顺带说明子类化是对的：它们本来就是可覆写的。
- **§6.5「配置接口新增 `IApplicationConfigurationContributor`」**：**顺序上成立**——这是我原本最怀疑的一条，实测被否证：10.7.0 的 `AbpApplicationConfigurationAppService.GetAsync` **先**填 `Localization = await GetLocalizationConfigAsync(options)`（内部 `if (options.IncludeLocalizationResources)`），**再** `foreach (var contributor in _options.Contributors)`，且 contributor 拿到的是**已填好的 DTO**。所以"用 contributor 做公开资源视图"能做，不需要换钩子。请把这一句写进 §6.5 作为依据（现有那个裁剪贡献者只清了 `Setting.Values`，不能反推"localization 也能在这里清"）。
- **§6.5「独立本地化服务另行替换，并注册其接口及原具体服务类型」**：**必要且正确**。`AbpApplicationLocalizationController` 注入的是接口，但 `AbpApplicationLocalizationScriptController`（注意命名空间是 `...Mvc.Localization`，不是 `...Mvc.ApplicationConfigurations`）构造注入的是**具体类** `AbpApplicationLocalizationAppService`；`ApplicationService : ITransientDependency` + `IncludeSelf` 让具体类自身可解析。漏掉这一步 = 脚本出口绕过全部治理，正是 §2.1.7 担心的那件事。
- **§3.2「ABP 业务异常可能返回 403，不能把所有 403 都判断为登录过期」**：**VERIFIED**，来源是 `DefaultHttpExceptionStatusCodeFinder.cs:70-73`（`IBusinessException → Forbidden`）。同处 `:41-46`（已认证的 `AbpAuthorizationException → 403`，未认证 → 401）与 `:50-53`（`AbpValidationException → 400`，**不是 403**）、`:55-58`（`EntityNotFoundException → 404`）、`:26-30`（`IHasHttpStatusCode` 最优先）、`:32-39`（`ErrorCodeToHttpStatusCodeMappings` 默认为**空字典**）。**由此得出一条计划没写但必须写的结论**：403 至少覆盖"业务失败"和"已认证但无权限"两种语义，**状态码不可能作为前端 `kind` 的判据**（见 §5.1）。
- **§5.3.1「保留…状态码逻辑」+ §5.4「无放开原文的选项」**：与 ABP 侧姿态一致（`RemoteServiceErrorInfo.Data` 只有当 `SendExceptionDataToClientTypes.Any(t => t.IsInstanceOfType(exception))` 命中才填）。

### 4.2 ⚠ 必须改口径的三条

| # | 计划原句 | 10.7.0 实测 | 后果与改法 |
| --- | --- | --- | --- |
| P1-1 | 第 221 行「当前 Host 未显式使用 `UseAbpExceptionHandling`。不要为此额外叠加它形成第二个原文出口；若实际执行版本已有该管线，应一并替换/适配」 | 该 API **今天已经生效**：`AbpApplicationBuilderExtensions.cs:108-113` 里 `UseUnitOfWork()` 的第一句就是 `app.UseAbpExceptionHandling()`；本仓 `AbpAdminHttpApiHostModule.cs:1516` 调了 `app.UseUnitOfWork()`。所以管线里已有 `AbpExceptionHandlingMiddleware`，位置在 `FriendlyBusinessExceptionMiddleware`（`:1443`）的**内层**。它还有一道前置闸（`:46-53`）：只有 `context.Items["_AbpActionInfo"]` 存在且 `actionInfo.IsObjectResult` 才转 error-info，否则原样 `throw`（框架源码里还留着一句 `//TODO: Align with AbpExceptionFilter.ShouldHandleException!`）。 | 计划的 hedge（"若已有则一并替换"）方向对，但把判断留给了执行者。**改法**：把第 221 行改成陈述句——"`UseUnitOfWork()` 已隐含安装该中间件（ABP 10.7.0 `AbpApplicationBuilderExtensions.cs:108-113`），其 `HandleAndWrapException` 是 private（`:59`）不可覆写，且只在 `IsObjectResult` 时接管（`:46-53`）；因此 §5.3.3 的兜底中间件必须**替换**它的输出而不是等它不接管，或者用 `IExceptionToErrorInfoConverter` 之外的实例级钩子在其外层统一投影"。并给 A11 之外加一条 **A21：JSON API 请求在 `Accept: application/json` 下，最终响应由哪一个出口写出必须唯一且被断言**（否则"统一 writer"只是个名字）。 |
| P1-2 | §5.3.2「授权中间件短路由 `IAuthorizationMiddlewareResultHandler` 适配」 | 该接口是**纯 ASP.NET Core** 端点策略钩子：ABP 10.7.0 全部包的字节扫描里 `IAuthorizationMiddlewareResultHandler`/`AuthorizationMiddlewareResultHandler` **0 命中**。ABP 自己走 `IAbpAuthorizationExceptionHandler`（`DefaultAbpAuthorizationExceptionHandler:59-63` 用 `ChallengeAsync`/`ForbidAsync`，调用点在 filter `:71-75` 与中间件 `:75-79`）。**而本仓真正决定 401/403 响应形状的是 cookie handler 的两个事件**：`AbpAdminHttpApiHostModule.cs:869-893` 里 `OnRedirectToLogin`/`OnRedirectToAccessDenied` 对 `/api` 前缀**只写状态码、不写任何 body**。 | 计划的"两个出口都要适配"结论没错（本仓确实两套并存：`FallbackPolicy=RequireAuthenticatedUser()` 在 `:1213`，`PreLoginApiEndpointsConvention` 在 `:1221`）。但**它没说出今天最关键的事实：`/api` 的 401/403 没有 body**。所以 §3.2 承诺的 `Client:AuthenticationRequired`/`Client:Forbidden`（带 message）目前**无生产者**——这属于"必须有值但没人提供"的必炸型缺口。**改法**：§5.3.2 加一句"本仓 API 的 401/403 由 `OnRedirectToLogin/OnRedirectToAccessDenied` 只写状态码；若要客户信封，必须在该事件（或 `IAuthorizationMiddlewareResultHandler`）里按 `/api` 且非 Razor 请求判定后写入公开信封，且 Razor 导航请求保持原跳转/空体行为"。A04/A09 各加断言"401/403 响应体的 `error.code` 为约定值"。 |
| P1-3 | §5.1「HttpApi：…端点 convention、管理端点登记与资格解析器」 | ABP 无一等 API 可以把某个资源排除出贡献者：`AbpLocalizationOptions` 只有 `Resources`/`GlobalContributors`/`Languages*`；且 `AbpStringLocalizerFactory.cs:174-181`、`:205-212` 会在**创建 localizer 时**把 `GlobalContributors` **追加**到同一个 `resource.Contributors` 尾部 ⇒ 走全局贡献者路线永远排在 `AbpAdminDomainModule.cs:179` 之后，覆盖优先级更高，**没有排除点** | 计划的"在贡献者接线处跳过静态客户资源"（§6.2）恰好是唯一可行做法（因为它控制的是自己那个 `foreach`），这条**判 VERIFIED**。但要在 §6.2 补一句"不得改用 `GlobalContributors` 注册"，并加一条负向断言："客户静态资源的 `Contributors` 里不存在数据库贡献者类型"（比"我们跳过了"更可测）。 |

---

## 5. 镜头 C：契约与数据模型

### 5.1 P1-4：`PublicKind` 与前端 `CustomerErrorKind` 之间没有定义映射

- §5.2 的 `PublicErrorDefinition.PublicKind` 写的是"允许导出给客户端的分类"，但**全文没有给出取值集合**；§7.2 定义了 12 个 `CustomerErrorKind`；§7.1 说快照含"分类"。三者之间的推导规则缺失。
- 而 §4 里唯一可能的间接判据（HTTP 状态）**被实测证明不可用**：业务失败与"已认证无权限"**都是 403**（`DefaultHttpExceptionStatusCodeFinder.cs:70-73`、`:41-46`）。
- 后果不是纸面不一致：按最省事的写法（`if status===403 → 'forbidden'`）实现后，业务失败会被前端判成权限问题，进而触发 §7.3 的"403 不退出但按 forbidden 呈现"，同时 §3.2 的 `Client:OperationFailed` 永远拿不到自己的 kind。
- **改法（三句）**：① §5.2 枚举行写死 `PublicKind = BusinessFailure | Validation | Authentication | Forbidden | NotFound | RateLimited | Conflict | Unexpected`（与 §3.2 八个码一一对应）；② §7.2 加一句"`kind` 只能来自快照的 `分类` 字段或本地网络/超时/取消判定，**禁止由 HTTP 状态推导**"；③ A19 加断言"每个 `PublicKind` 恰好对应一个 `CustomerErrorKind`，且 403 状态的两个不同 kind 在快照里可区分"。

### 5.2 P2-1：`Client:NotFound` 与"端点既有隐藏资源规则"之间没有定义

§3.2 让它"保持端点既有隐藏资源规则"。实测这条规则**存在于控制器注释里而不是任何配置里**：`AuditLogExportController.cs:79-80` 逐字写着"仓储 `GetAsync` 抛的 `EntityNotFoundException` 在 MVC 控制器（非应用服务）链路上**不会被转成 404，不存在的 id 直接 500**"。也就是说：客户入口若走控制器，"NotFound"今天根本不存在；若走 AppService 才有 404。请把这条差异写成 §3.2 的一句附注 + 一条验收（客户入口的 `EntityNotFoundException` 必须是 404 + `Client:NotFound`，不得出现 500）。这同时是 §8 模板示例的坑：示例的控制器入口（`Client/Controllers/BizProjectClientController.cs`）正好落在这条被注释承认的缺陷链路上。

### 5.3 P2-2：`data` 白名单必须同时管住"message 插值"，但今天这两件事由两个不同出口做

- 现有文案是**安全**的：`AbpAdminResource` zh-Hans 的 158 个码文案里，如 `:160 "AbpAdmin:OperationRateLimitExceeded": "操作过于频繁，请稍后重试。"`、`:163 "…ImageProcessingBusy": "服务器繁忙，请稍后重试。"`——运维可读、客户可用，含占位参数的只有 55 个键（如 `:162 AvatarTooLarge` 的 `{MaxSize}`、`:358 DuplicateSubmit` 的 `{IntervalSeconds}`、`:165 InvalidImageExtension` 的 `{Extension}`）。
- 但 `:74` 的 `localizer[business.Code].Value` **没有缺键保护**：键缺失时 `IStringLocalizer` 返回**键名本身**，于是内部码会原样出现在 `message` 里。繁中（zh-Hant）与其余 16 个文化文件**0 个错误码键**（见 P0-2 数据），所以今天一个 `Accept-Language: zh-Hant` 的请求命中该中间件，`message` 就是 `AbpAdmin:OperationRateLimitExceeded` 这样的内部码串。**这条要写进 §9-P0 的盘点基线**（否则 A02 的"没有内部码"会被当成新机制的事，而它其实是既有缺陷）。
- **真正的参数泄露在 `data` 不在 `message`**：`:91` `data = business.Data` 整包下发，而 `AbpAdminOperationRateLimitingException.cs:8-14` 自证 Data 含 `PolicyName/MaxCount/CurrentCount/RemainingCount/RetryAfter*/WindowDurationSeconds/WindowDescription/RuleDetails`，并且"**`OperationRateLimitingContext.ExtraProperties` 里的每一项都会被复制进本异常的 Data**…因此不要往 ExtraProperties 里放密钥"。生产者见 `OperationRateLimitingChecker.cs:276-278`。**这是一条"靠注释约定、无机制兜住"的现行泄露**，与 §2.1.3 批评 exchange 的那句"有译文=可公开"是同一类失效（用文档提醒代替白名单）。计划的 `AllowedParameters` 正好治它——请在 §3.1/§3.4 显式点名这一例（现有码文案不动，只断 `data`），执行者才知道 A03 要拿它做夹具。
- 反向也要写：ABP 原生路径今天**一个 Data 都不发**（全仓 `SendExceptionDataToClientTypes` **0 命中**）。于是同一异常有两个相反姿态：走 filter/中间件 = 无 data（管理端 429 的等待秒数拿不到），走本仓中间件 = 全量 data。**高置信静态推断，需一次真实 429 响应抓包证伪**；请把它写成 A17 的断言形式："429 响应的 `error.data.RetryAfterSeconds` 存在且为正数（AdminCompatibility 与 Public 两模式分别断言）"，而不是"等待信息保持"。

### 5.4 P1-5：§5.1 那句"不可动态覆盖资源标记"是 P0-2/P1-6 的现成解药，请把它提升为判据

计划已在 Domain.Shared 里定义了"不可动态覆盖资源标记"，但 §6.9 的启动校验、§6.1 的语言完整性 CI、§5.4 的 `FallbackCulture` 合法性三处都没引用它。三处应统一改成"仅对携带该标记的资源施加完整性/白名单校验"。理由见 P0-2。

---

## 6. 镜头 D：可执行性与规模（数字为实测输入，不作验收门槛）

| 项 | 实测值 | 测量方法（仓根起、不加目录过滤、排除 obj/bin/.vs/node_modules） |
| --- | --- | --- |
| 带 `[Authorize]` 的 AppService 文件 | **64** | `grep -rl "\[Authorize" backend/framework-app backend/modules --include=*AppService.cs \| wc -l` |
| 形如权限串的常量 | **≈167**（上界，可能含同名前缀的非权限串） | `grep -rhoE '"(AbpAdmin\|BizTemplate\|Ledger\|Collection\|Ordering\|Webhooks\|TokenGateway)\.[A-Za-z0-9._]+"' --include=*.cs \| sort -u` |
| HttpApi 手写控制器 | **8 个控制器 + 4 个配置贡献者**（同目录混放） | `ls backend/framework-app/AbpAdmin.HttpApi/Controllers/` |
| 业务/平台模块控制器 | **0**（全走动态 API） | `find backend/modules -path "*/Controllers/*.cs"` |
| 已有错误码文案 | zh-Hans **158** / en **158** / 其余 18 文化 **0** | 逐文件解析 `Localization/AbpAdmin/*.json` |
| 含占位参数的文案 | **55** | 同上，正则 `\{[A-Za-z]` |
| ABP 侧匿名可达框架控制器 | **6** | `PreLoginApiEndpointsConvention.cs:23-38` |

**P1-6：登记工作面没有量级、没有批量策略、没有过渡态说明。** §4.1 让"未分类默认 Public"，§9-P0 让建 `AdminErrorEndpointRegistry`，§12.1 让"先完成管理登记再开放客户端点"——方向都对，但读者不知道这是 64+ 个服务、167+ 个权限、158 个码的**一次全仓语义重扫**，也不知道扫不完时管理端会怎样（答案：全部退化成 `Client:OperationFailed` 通用提示，因为 `:74` 的按码本地化在公开投影里被换掉）。

**改法（四条，都不超过一段话）**：
1. §9-P0 加一条任务："归类按**权限组前缀**批量执行（`BizTemplate.*`/`Collection.*`/…），人只审例外清单"，并把"64 个 AppService / 158 个码"写成输入基线（写作规模，不写作门槛）。
2. §4.1 加一句过渡态定义：未完成登记的端点在开发/测试宿主下**记日志不改响应**（dry-run），生产仍按 Public。这不违反 §5.4"不许放开原文"——它放开的是**管理端既有语义**的暂时保留，不放开内部信息；请在文中写清这条差别，否则下一个读者会以为开了后门。
3. 把 §4.2 的"资格解析失败→降级 Public"（`:148`）加一条**可观测**断言：降级次数必须计数并可查询，否则"登记没做全"这件事和"功能坏了"在运维眼里不可区分。
4. 给 §4.2.5 补上 SettingUi 的**完整真相**（我实测比计划写的更硬）：`AbpAdminSettingUiAppService.cs` 类注释自证上游 EasyAbp 的 `SettingUiAppService`/`SettingUiController` **都没有 `[Authorize]`**、`/api/setting-ui/*` 实际匿名可达、唯一拦截在前端 `access.ts`；本仓靠 `[Dependency(ReplaceServices=true)]` + `[ExposeServices(typeof(ISettingUiAppService), typeof(SettingUiAppService))]` + `[RemoteService(false)]` + 类级 `[Authorize(SettingUiPermissions.ShowSettingPage)]` 把门禁装到**被 EasyAbp 控制器注入的那个服务代理**上，写值再叠方法级 `AbpAdmin.SettingUi.Update`。** ⇒ 端点元数据里**看不见**这个权限，`Endpoint.Metadata` 路线对该案例必然失败（且失败方向是"看起来无权限→Public"，安全但功能坏）。计划只写"SettingUi 是必须覆盖的现有案例"是对的，请把上面这段作为该案例的规格写进去，因为它同时是"登记表要绑服务方法而非路由"的**唯一现成反例**。

---

## 7. 镜头 E：移植保真度（exchange → abp_admin）

### 7.1 "统一兜底"的保真度

计划 §2.1.1 说 exchange"未识别错误统一兜底"。实测是**四层**：`FALLBACK_KEY`（`:62`）、`FALLBACK_DEFAULT`（`:67`）、`CODE_EN_FALLBACK` 17 码硬编码（`:79-99`）、以及 `ADMIN_GATE_CODES` 这 4 个"有译文也强制屏蔽"的码（`:56`、`:175`）。第四层恰好是 §6 里"公开资格与翻译资源独立定义"要消灭的东西的补丁形态。计划的取舍比 exchange 更干净，**不是移植失真**；但计划把这条机制描述成"统一兜底"会让实现者少做一层。建议在 §2.1.1 补半句"（其兜底实为四层，第四层是补丁式屏蔽集）"。

### 7.2 ⚠ 一处真正的"移植=行为改造"，计划没有标出来

exchange **全部业务失败走 HTTP 200 + body.code**：`AdminResultProvider.cs:142` 把 `statusCode` 只写进**响应体的 Code 字段**，`JsonResult` 不设状态码；同仓 `PredictJsonBody.cs:17-19` 逐字自证"本站统一 `AdminResultProvider.Error` 业务码（**HTTP 200 + code 400**）"；两个前端都按这个双通道编码（H5 `request.ts:153-161` 在 2xx 分支里 reject、`Web/src/utils/request.ts:96-97` 先 `status>=400` 早退、`:125` 才判 `serve.code!==200`）。

计划的 §3.1"不把所有失败改为 HTTP 200"是**对源项目的行为否决，不是移植**。这不一定是错——但它意味着"从 exchange 吸收"这条叙事只对**前端归一化 + 按码提示**这一半成立，对**协议形状**这一半不成立。请在 §1.1/§2.1.1 各加一句"本期只移植机器码分离与兜底，不移植 HTTP 200 携带业务码的协议"，并在 A09 保留"HTTP 不被统一改成 200"作为**移植偏差断言**（现在的写法是对的，只是没说它在否掉参照系）。

### 7.3 exchange 侧三个真空，恰好是计划 §7.2/§7.3 的增量（正面确认）

实测 exchange `request.ts` **完全没有** 429 处理（落通用兜底）、**没有**超时分类（axios `timeout: 20_000`，`:181`）、**完全没有**取消处理（全 `src/api` grep `isCancel|CancelToken|AbortSignal|ERR_CANCELED` 0 命中）。所以计划 §7.2 的 `timeout/cancelled/outcomeUnknown` 三个 kind、§7.3 的 429 等待与 Abort 静默，是**净新增能力而非移植**。这点值得在 §2.1.1 正面写一句，否则读者会以为参照系已验证过这些行为。另外 `ApiErrorWithCode`（`tradeErrorMessage.ts:109-112`）确实**不挂 `response`/`cause`/`raw`**，与计划 §7.2 第 292 行的禁令一致——这条移植是**等价**的。

---

## 8. 镜头 F：测试与工程一致性

### 8.1 P1-7：仓里已经有一套"前端机器契约快照"机制，计划另起一套且没有引用它

- 既有件：`backend/test/AbpAdmin.Application.Tests/Monitoring/FrontendContractSnapshotTests.cs`（`:29` 说明扩展方式、`:39` `SeedTypes`、`:83` 反射出形状），快照文件 `Contracts/Snapshots/frontend-contract-shapes.json`，更新环境变量 **`FRONTEND_CONTRACT_SNAPSHOT_UPDATE=1`**。`web/AGENTS.md` 把它写成**机器强制**规范："前端新消费一个 DTO 时，把类型加进该测试的 `SeedTypes`（嵌套 DTO 自动递归）"。
- 计划 §7.4 新增了 `PublicErrorContractSnapshotTests` + `web/src/abp/customer/public-error-catalog.json` + 环境变量 `UPDATE_PUBLIC_ERROR_CONTRACT=1`，**全文没有一次提到既有那套**；§11.1 的测试落点清单里也没有 `Monitoring/FrontendContractSnapshotTests.cs`。
- 两个具体后果：① 两套快照、两个环境变量名、两条同步纪律，维护者必然只记得一个；② 更实际——**新客户 DTO（`ClientFailureDto`、`example.ts` 的 DTO）没有进 `SeedTypes`，可空性镜像规则对本期新增的客户端契约完全不生效**（`T?` ⇔ `T | null` 那条约定是这台机器兜的），而 §7.2 的 `fieldErrors`/`correlationId?`/`retryAfterSeconds?` 全是可空字段。
- **改法（两句 + 一行任务）**：§7.4 加一句"本期快照只覆盖**码与字段名的机器契约**，DTO **形状**漂移继续由 `FrontendContractSnapshotTests` 负责，两套机制的分工写在同一节"；§5.1 或 §9-P3 加一条任务"新客户 DTO 注册进 `FrontendContractSnapshotTests.SeedTypes`，与镜像类型同提交"；环境变量名沿用既有 `*_SNAPSHOT_UPDATE` 形态（或明确写出为何不同名）。

### 8.2 P2-3：TestServer 依赖与"最小夹具"的漂移风险

计划 §11.1 的"最小 TestServer 管线夹具 + 依赖包按集中版本管理新增"是对的（`HttpApi.Host.Tests.csproj` 现在确实没有 `Microsoft.AspNetCore.Mvc.Testing`/`TestHost`；CPM 在 `Directory.Packages.props:3`，新增包要在其中钉版）。但"使用与实际模块相同的 filter/middleware 注册"这句话无法由一个最小夹具兑现：真实接线分散在 `AbpAdminHttpApiHostModule.cs:1213`（FallbackPolicy）、`:1221`（约定）、`:1443`/`:1446`/`:1463`/`:1502-1507`/`:1516`/`:1518`（顺序）与 `AbpAdminHttpApiModule.cs:48`/`:60`。夹具自己再声明一遍 = 两份真相。
**改法**：利用 `HostTestBed` 已经 `builder.Build()` + `InitializeApplicationAsync()` 这一事实（`HostTestBed.cs:43-45`，`app.Services` 可用），加一类**装配断言**而不是重复声明：从真实宿主解析 `IOptions<MvcOptions>` 断 `Filters` 中 `AddService` 的异常过滤器类型恰为新的子类且**只有一个**；断 `ClientErrorAudience` 元数据在**每个**已映射端点上存在；断中间件顺序不变。再让 TestServer 夹具只负责"响应字节"，装配一致性由这类断言负责。这样 §11.1 那句"并验证组合宿主装配一致性"才第一次有了具体做法。

### 8.3 P2-4：CI 面与 vitest 环境的两处偏差

- §11.2「最终合并前按当前 CI 执行 `dotnet test …slnx --nologo` 与 `web` 下 `pnpm test`」——两条都对（`ci.yml:72`、`:97`）。但 CI 还跑 `pnpm tsc`（`:95`）、`dotnet tool restore`（`:43`）、`pwsh scripts/check-docker-restore.ps1`（`:29`）、`pwsh scripts/docker-build-check.ps1`（`:106`）和 **7 条 `dotnet ef migrations has-pending-model-changes`**（`:53-66`）。本期"预计不需要实体或 DB 变更"，但 §5.1 要改 **csproj 嵌入资源与发布包内容**，而 CI 里恰好有 docker build 检查——请把它们列进 §11.2，别留给执行者"意外踩到"。CI **不跑** `pnpm lint`，所以 §11.2 让本地跑 lint 是对的，可以补一句"CI 不会替你跑 lint"。
- §11.1「纯函数测试使用 node 环境」：`web/vitest.config.ts:13` 是 `environment: 'happy-dom'`，且全仓 **没有任何 `@vitest-environment` 逐文件覆盖先例**（jsdom 装了但配置未用）。不是障碍，但要在 §11.1 写清用哪种手段（文件头 docblock `@vitest-environment node`，或 `test.environmentMatchGlobs`），否则第一个读者会改全局配置，把 22 项既有测试一起挪出 happy-dom。

### 8.4 P2-5：`skip` 在两条传输上语义相反，必须写死"不统一"

计划 §7.2 让客户侧 `skipErrorHandler` 只映射为 `display=none` 且不绕归一化（A13），而管理端 `requestErrorConfig.ts:87` 是原样 rethrow、并被 `requestErrorConfig.test.ts:84` 钉住（A17 要求原管理用例继续通过）。两者**故意相反**。现在文中没有一句话阻止执行者"顺手统一"。建议在 §7.2 第 296 行后加一句："管理端 skip 语义（原始异常 rethrow）保持不变并有测试钉住；本期不统一两条传输。"

---

## 9. 计划做对的地方（点名，并附我补的证据）

1. **§2.1.6 那条"不能全局替换转换器"的直觉，比我预期的更正确**。我原打算把它降为"待证"，读了 10.7.0 源码后判定成立且要加重：审计转换器注入并使用该转换器（`AuditLogInfoToAuditLogConverter.cs:26`、`:72`），读的是 **`auditLogInfo.Exceptions` 原始异常对象**（不是已摊平的 `Errors` 字符串），落库时才 `JsonSerializer.Serialize(remoteServiceErrorInfos)`（`:80-82`）。⇒ 全局 `Replace` 会**改写审计落库内容**，而不只是改响应。建议把这句直接抄进 §5.3 的禁令段（第 209 行），因为它比"共用一个转换器"更能阻止实现者犯错。
2. **§4.2 的六步资格顺序**是这份计划最强的部分：`clientType`/请求头/Origin/UA/URL 前缀/公开 `client_id` 不给资格（`:150`）、"仅 `[Authorize]` 的个人资料登记 Public"（`:150`）、"资格不确定即降级 Public"（`:148`）。我用 `DefaultHttpExceptionStatusCodeFinder` 的映射表反推了一遍，这四条在 ABP 的默认姿态下都站得住。
3. **§7.1 的 `redirect: 'error'` + 同源/登记路径白名单 + 307/308 携带请求体与身份头的禁令**（`:272`）是教科书级的；exchange 那边是 `baseURL` 运行时按域拼接（`request.ts:181-184`），完全没有这层。
4. **§5.4 拒绝提供 `ShowAllErrors`/`ExposeDetails`/`AllowRawMessage`，并要求非法宽松字段启动失败**（`:242`）——这条比大多数同类方案严格，且与 D1 自洽。**建议加强为一条 A**："配置里出现任一未知/宽松字段 → 宿主启动失败"，现在只在 §5.4 正文，没进 §10 矩阵。
5. **§8 对模板示例的自我限制**（默认 false、非 Development 启动拒绝启用、故障注入端点只进测试程序集、"不能作为多租户客户业务的数据授权模板上线"）与实测一致：`BizProject.cs:11` 是 `AuditedAggregateRoot<Guid>`，**无 `IMultiTenant`**，只有 `Name/Description/IsActive` 三个自有字段，唯一归属线索是继承的 `CreatorId`，且现在只用于配额（`BizProjectAppService.cs:78`），列表查询无任何过滤（`:41-60` 全表计数）。计划说"管理列表不能直接当客户列表"是**VERIFIED 且比听起来更严重**（今天任何人有 `BizTemplate.Projects` 就能看全表）。
6. **§2.1.9 那条"看似存在、发布包丢文案"是真的**：我把全仓 csproj 的本地化 glob 列了一遍，**全是单层 `*.json`，没有一条 `**/`**。
7. **canvas 与计划的分工**：canvas 把"配置能控制什么/不能控制什么"做成表（`optionRows`），这部分比正文 §5.4 更好读，建议把那张表的约束列并入 §5.4，避免两份口径。

---

## 10. 开工前一小时内可完成的改动清单

按性价比排序，全部是文字/一句断言级，不需要重新设计：

1. **补 §5.3 的现有出口清单**：加 `HttpApi/Controllers/FileShareController.cs:27-28`（`AllowAnonymous` + `Task<IActionResult>`/FileResult + 类注释自证限流交给 ABP）与 `HttpApi/Controllers/GdprDownloadController.cs:29-50`（`AllowAnonymous`、注释逐字"本类保持无任何 try/catch：`DownloadAsync` 返回 FileResult（非 ObjectResult）"）。（P0-1）
2. **给 §6.9 与 §6.1 的完整性校验加作用域**："仅对携带 §5.1 所述静态客户资源标记的资源施加默认语言/占位参数完整性校验"；并把 §6.1 的"现有前端支持的简中、繁中、英文"改写为实测口径（前端 `culture.ts:15-19` 只映射 3 个 locale；后端 `AbpAdminResource` 有 20 个文化文件，其中 zh-Hant 4 键、13 个语言各 37 键、158 个错误码只存在于 zh-Hans/en；语言可用集是**数据驱动**的，见 `DbLanguageProvider.cs:25`、`LanguageManager.cs:77`、`culture.ts:12-14` 注释"语言在本地化管理页启用后即刻生效"）。（P0-2）
3. **把第 221 行从条件句改成陈述句**（P1-1）+ **§5.3.2 补 `/api` 401/403 无 body 的事实**（P1-2）+ **§5.3.1 去掉"扩展"一词**（§4.1）。
4. **§5.2 枚举行写死 `PublicKind` 取值，§7.2 写死"kind 不由状态码推导"**（P1-4）。
5. **§7.4 加与既有 `FrontendContractSnapshotTests` 的分工一句；§9-P3 加"新客户 DTO 进 `SeedTypes`"**（P1-7）。
6. **§9-P0 加归类批量策略与量级输入；§4.2.5 补 SettingUi 的真实机制段**（P1-6）。
7. 顺手补 §11.2 的 CI 全量命令（含 `pnpm tsc`、docker 两个脚本；注明 CI 不跑 lint）。（P2-4）

**第一步选哪个**：建议先做 **P0 的"端点分类测试 + 出口盘点"（§9-P0 第 2、3 项）**，因为它不依赖任何上面未决的口径，而且它正好产出第 1、6 条所需的事实。不要在口径未定前动 §5.3 的中间件与 filter 接线。

---

## 11. 审核局限（诚实披露）

1. **未实机运行任何后端测试**。本机 `dotnet build`/`dotnet test` 未执行；计划 §11.2 的命令我只做了**路径与脚本存在性**核验（全部命中）与 `pnpm test`/`pnpm exec vitest`/`node scripts/vitest-run.mjs` 的**真实运行**（前端 22 项、exchange 21 项，均与计划一字不差）。
2. **未起 HTTP、未抓包**。因此 §5.3 的三条属于高置信静态推断，我在正文里已把它们改写成验收断言而不是结论：① "ABP 路径今天不下发 `Data`（`SendExceptionDataToClientTypes` 全仓 0 命中）导致管理端 429 拿不到等待秒数"；② "匿名 FileResult 出口今天会把 `business.Data` 整包下发"；③ "`/api` 的 401/403 只有状态码"（这条有直接代码证据 `:869-893`，只是没验证运行时序列化结果）。
3. **ABP 结论的证据通道**：本地包 `D:\pkg\nuget`（`dotnet nuget locals global-packages --list`；`C:\Users\Administrator\.nuget\packages` **不存在**）里 pdb 为普通 portable PDB、XML doc 极稀疏（`Volo.Abp.AspNetCore.Mvc.xml` 仅 30 个 member、exception 相关 0 条），**无法**据此判 virtual/private；所有结构性结论来自 GitHub **tag `10.7.0`** 源码（该 ref 实测可用）。未能验证的一项：MVC `FilterRunner` 在 `ExceptionHandled=true` 后是否继续跑后续异常过滤器（属 ASP.NET Core 内部，两通道都不覆盖）——这也是我建议"替换服务类型而不是并列挂第二个 filter"的另一个理由。
4. **计数类数字的测量面**：64/167/8/0/158/55 全部用仓根起、不带目录参数、排除 `obj|bin|.vs|node_modules` 的命令测得；其中"≈167 权限串"是**上界**（正则可能吞入同名前缀的非权限字面量），我在 §6 表里已标注方法。数字请按输入用，不要当门槛。
5. **时变断言已复测**：`git status`、`docker-compose.yml` 改动内容、HEAD、计划文件 sha256 均在 14:49:17 临发布重打；`docs/` 目录里两份他人报告是 13:37 之后新出现的，我只看到文件名、未打开内容（§0）。
6. **未审的东西**：`docs/framework-contracts.md` 我只读了 404 取舍一节（`:70`）与 grep 了 401/403/429，未通读；`.cursor/rules/**` 只按关键词命中扫了 5 个文件；`web/src/pages/languages/**`、`DbExternalLocalizationStore`、`LanguageManager` 的动态语言治理只读了接口层，没有据此下结论（§5.4 的作用域建议不依赖它们）。
7. 与 canvas 的 1 处行号分歧（`requestErrorConfig.ts` :73 vs :92）我判"各引一半、准确应为 92-97"，没有据此说任何一方错。

---

*本报告独立取证；若后续轮次要更新计划，请以本报告 §10 的 7 条为最小改动集。*
