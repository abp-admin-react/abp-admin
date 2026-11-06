# 《abp_admin 客户端错误处理开发方案》独立审核报告 第 2 轮（qwen）

- 被审文档：`docs/client-error-handling-development-plan.md` **v1.1**
- 取证指纹：`mtime=2026-11-05 15:17:34`、`size=69407`、`sha256(前16)=c5f78192776dd322`；**16:14:14 临发布复测同一串未变**（全文 653 行本轮通读）。
- 处置表：`docs/client-error-handling-development-plan-review-disposition.md`（同 mtime、62186 字节），其中 Q01—Q32 针对本报告第 1 轮，**逐条回核**。
- 基线：HEAD `69bbbe5b`（2026-11-01）、ABP `10.7.0`（`backend/Directory.Packages.props:15`）、`git status` = `M docker-compose.yml` + 六份未跟踪（含本文与两份他人报告、`.workbuddy/`）。16:14 复测无变化。

---

## 0. 独立性事件

1. 本轮**通读了处置表**（用户明确要求"拒绝的你还坚持必须辩论"，这是任务本体，无法回避）。因此**其中转述的 grok/hy4 结论已进入我的视野**——这是二次暴露，属不可回避的一类。处置方式：**凡他人结论一律不用作证据，也不评价他人报告**；对指向 grok/hy4 的拒绝条目（如 `hy4 182 行 Umi 拦截器`、`Grok 62/79 行 49 处无码`），我只核**处置表给出的理由是否与代码一致**，不判原报告对错。
2. 指向本报告的 Q01—Q32 全部按处置表所引"原报告行号"回读了我自己第 1 轮的原文（见 §5）。
3. 除处置表正文外，未打开 `...-review-grok.md`、`...-review-hy4.md`。
4. 本轮所有新结论均为第一手：本地源码 + ABP `10.7.0` tag 原文 + exchange 源码复算 + 复跑。

---

## 1. 摘要

**v1.1 的合并质量高：第 1 轮我提的 32 条里，实质主张被吸收 25 条（含两条 P0 的修复正文落地），我的 6 条错误被处置表用可复现的证据纠正，且纠正全部成立。**

本轮结论分三块：

**A. 我认错（6 条完全让步 + 5 条部分让步）**。我第 1 轮最严重的一条是我自己造的：**"ABP 原生路径今天一个 Data 都不发"**（第 1 轮 L106、L136、L240）。真实情况是框架默认就发——我拿"仓库里没有显式配置"这个正确观察，推了一个默认值方向的错结论。复测见 §2 表 Q10 行。这条错误还**反过来使我低估了自己 P0-1 的严重性**（详见 §3.1 的加强）。

**B. 我坚持（0 条坚持原样 + 2 条把战场移到新位置）**。第 1 轮被拒的条目里没有一条需要我以原形态坚持——但其中两条的**残余部分**在新正文里长成了新缺陷，且是我上轮建议被采纳之后才出现的：

- **P1-1（新引入）**：`§5.2` 把 `PublicKind` 定成注册表字段，而 `§3.2/§119` 又给"图片繁忙 503"写了一句 kind 例外；对同一个既有异常 `AbpAdmin:ImageProcessingBusy`，两条给出 `rateLimited` 与 `server` 两个值，而 §5.2 的启动一致性规则只覆盖"公开码"不覆盖 kind。
- **P1-2（吸收后剩的洞）**：§2.1.12 已把"原生链也要投影"写进正文，但 `§4.3` 对 AdminCompatibility 的措辞仍是"保持既有字段形状"，`§10` 没有一条断言检查管理响应的 `data` 键集合——于是"未批准键整包透传"在管理路径上变成了**被默许的默认行为**。

**C. 新版正文 1 条自相矛盾**：处置表 Q20 承诺把新快照变量统一为 `PUBLIC_ERROR_CONTRACT_SNAPSHOT_UPDATE`，正文 §7.4/§11.2 两处仍是 `UPDATE_PUBLIC_ERROR_CONTRACT`。

计数：**P0 = 0，P1 = 3，P2 = 6**。P0 归零，且剩余都是补句子/加断言级别——**本轮判定：可以开工**。开工前 30 分钟内可改完 §6 那 3 条。

---

## 2. 对处置表"拒绝"条目的三态结论（逐条，附第一手复测）

| 处置 ID | 处置表对我的拒绝理由 | 我的独立复测 | 三态 |
| --- | --- | --- | --- |
| **Q10**（拒"ABP 原生路径不发 Data"） | 框架默认 `SendExceptionDataToClientTypes` 含 `IBusinessException`；仓里没配置推不出"默认不返回" | **对方正确，且我给的是决定性证据**：`AbpExceptionHandlingOptions.cs`（10.7.0 构造函数）逐字为 `SendExceptionDataToClientTypes = [ typeof(IBusinessException) ]`；`AbpAdminOperationRateLimitingException.cs:16` 与 `AbpAdminImageProcessingBusyException.cs:12` **都继承 `BusinessException`**（后者 `:19` 带 code、`:22` `WithData`）；全仓 `WithData` 调用 **337 处** | **完全让步**（我的建议方向也错：见 §3.1） |
| **Q11**（拒"繁中缺键必现回显内部码基线"） | 本仓资源以 `"en"` 为基线，ABP 默认文化回退先于最终缺键 | **对方正确**：`AbpAdmin.Domain.Shared/AbpAdminDomainSharedModule.cs:64` `.Add<AbpAdminResource>("en")`；`AbpLocalizationOptions` 构造函数 `TryToGetFromDefaultCulture = true`、`TryToGetFromBaseCulture = true`（10.7.0）；`AbpDictionaryBasedStringLocalizer.cs:154-156` 确有 `Resource.Contributors.GetOrNull(Resource.DefaultCultureName!, name)` 这一跳。我把"某文化文件缺键"当成了"没有回退" | **完全让步**（残余风险已被 §34 覆盖，§6.4） |
| **Q13**（拒"模块控制器 0"） | `TokenGateway/Http/` 下有 4 个具体 Controller；按 `Controllers` 目录名统计漏检 | **对方正确，且我能指出我错在哪条命令**：我第 1 轮用的是 `find backend/modules -path "*/Controllers/*.cs"`，该谓词要求路径里存在名为 `Controllers` 的目录；`ls backend/modules/platform/AbpAdmin.TokenGateway/Http/` → `AdminPlansController.cs / AuthController.cs / ChatController.cs / GatewayControllerBase.cs / WalletController.cs`。另 `AbpAdminController.cs:8` 确为 `abstract`，我第 1 轮把它算成"8 个控制器"也偏高 | **完全让步**（这是"检索面小于断言面"老毛病第 7 次命中我本人） |
| **Q14**（拒"开发/测试未登记端点 dry-run 保留原响应"） | 违反已确认 D1 与默认 Public | 理由成立，无代码可反证。处置表并保留了替代形态（离线预览、影子分类计数、旧版本隔离基线采样），正文 §155 逐字写着"禁止用'开发 dry-run 不改原响应'绕过默认安全规则；可以离线预览分类差异或在隔离测试中对照新旧对象"——**我的意图的另一半已被采纳** | **完全让步**，无残余 |
| **Q07**（拒"一律禁止由 HTTP 状态推导 kind"） | 空体 401/403/429 仍需安全分类；"拒绝原文"与"允许本地安全兜底"是两个问题 | **这个区分是对的**，我第 1 轮的"一律禁止"过头。正文 §332 给出的四级序（公开码 PublicKind → 已登记协议安全字段 → HTTP 状态安全兜底 → 无响应时本地分类）比我的"禁止"更可执行；§333 还补了我没想到的码/状态冲突处理 | **部分让步**：措辞上认输；**残余=新冲突**，见 P1-1 |
| **Q20**（拒"模板客户 DTO 也进框架 SeedTypes"） | 违反依赖方向；既有递归只限种子程序集 | **对方正确**：`AbpAdmin.Application.Tests.csproj` 无 Biz.Template 引用；框架 `ClientFailureDto` 位于 Application.Contracts，§356 已把它请回原 SeedTypes——**我那半被采纳**，被拒的只是"让框架测试反向引用业务模块"那一支 | **部分让步**：模块 DTO 归模块测试这条我完全接受；**残余=变量名未统一**，见 §4.1 |
| **Q26**（对我"data 优先还是 header 优先"给了不同选择） | 最终选 header 优先、批准 body 字段兜底 | 这是实现选择而非事实分歧，且 §119 的写法（含 HTTP-date 解析、0 合法、>86400 不显倒计时、冲突不拼值）比我的两行更完整 | **完全接受其选择**，仅提 §6.5 的一条验收缺口 |
| **H17/F4**（hy4 被拒：Umi 无临时拦截器机制） | 生成 runtime 确有挂载/卸载 | **理由与代码一致**：正文 §57 现引 `web/src/.umi/plugin-request/request.ts` 的 `getRequestInstance`/`opts.requestInterceptors` 为证；我第 1 轮给子任务的检索面**显式排除了 `.umi`**，因此双方在这一条上共享同一个漏检面 | 不评价原报告；**我自我更正**：我第 1 轮的 PARTLY 判词同样建立在排除 `.umi` 的检索面上（见 §3.3） |
| **G04**（Grok 被拒："49 处全无码"） | 49 个构造点中 12 处显式带 code | **理由内部自洽**：正文 §176 已改成"有 code 的 `UserFriendlyException` 同样可能嵌入 `ex.Message`……不把'49 个构造点'误称为'49 个无码异常'"。我只核这条改写是否与框架语义一致——一致（见 §3.2） | 不评价原报告；此条**支持**我的 P1-2 |
| **Q16**（拒"SettingUi 当前仍匿名可达"） | 本仓已有替换服务 + 宿主默认拒绝 | **对方正确**：`AbpAdminHttpApiHostModule.cs:1213` `FallbackPolicy = RequireAuthenticatedUser()`，且替换类带类级 `[Authorize]`。我第 1 轮引用的是该文件**自己注释里对上游 EasyAbp 的描述**，把它写成本仓现状是我的错（注释在 `AbpAdminSettingUiAppService.cs:40-43`，说的是上游） | **完全让步**；我的**核心洞见未被否掉且已被采纳**（§163/§384 仍要求测服务代理授权、§168 登记细节） |
| **Q18**（拒"exchange 全部业务失败走 HTTP 200"） | `AdminResultProvider.cs:60-79` 仍有 `SetResponseStatusCodes` 与 401/403 处理 | 复测方向一致：该文件确有 `OnResponseStatusCodes` 分支；我第 1 轮的"全部 200"是由单个 `JsonResult` 未设状态码外推的绝对化表述 | **完全让步**；正文 §15 已吸收我真正关心的那半句（"只借鉴机制，不迁移 200+body 状态码用法"） |

---

## 3. 复测中挖出的、比原条目更硬的事实（用来支持计划的决定，而非翻案）

### 3.1 我那条错的推断，其"正确版本"比我的更危险

我当时把"整包下发 `Exception.Data`"限定在手写中间件一条路径。真实情况：

1. 框架默认把**所有** `IBusinessException` 的 Data 发给客户端（`AbpExceptionHandlingOptions.cs` 构造函数）；
2. 本仓大量异常正是 `BusinessException` 子类，例：`AbpAdminOperationRateLimitingException.cs:16`（`PolicyName/MaxCount/CurrentCount/RuleDetails` + 全量 `ExtraProperties`）、`AbpAdminImageProcessingBusyException.cs:12`（503，`:22 WithData(RetryAfterSeconds)`）；
3. 全仓 `WithData` 使用 **337 处**；
4. 中间件（`FriendlyBusinessExceptionMiddleware.cs:91`）只是**第二条**同样整包下发路径。

⇒ 结论：**不是"某条旁路漏了"，而是两条主路都把未批准字段默认送到客户端**，而 `ExtraProperties` 那个放大器（`AbpAdminOperationRateLimitingException.cs:12-14` 自证）把键集合变成开放集合。计划 v1.1 §2.1.12 + §3.4 + §4.3 的处置方向因此**不是保守，而是必需**——这也解释了为什么处置表能把我"只改 data 白名单就够"的判断拒掉。请把这四步链条作为一句话写进 §2.1.12 的依据栏（现在只写了"默认包含 IBusinessException"，没写本仓有 337 处 WithData 与两个具体子类命中），否则下一个读者仍可能低估面。

### 3.2 计划第 134 行（§3.4"先筛参数再插值"）的依据在 10.7.0 里确实成立

处置表 O2 说转换器会**用完整 Data 替换文案占位符**。我按 `TryToLocalizeExceptionMessage` 的方向复核过这条逻辑与 §176 的新句一致：`zh-Hans.json:162` 的 `AbpAdmin:AvatarTooLarge` 文案就是**带 `{MaxSize}` 占位**、值来自 `WithData`。⇒ **"先插值后删 data"确实撤不回已经进句子的值**，§3.4 第 134 行这条禁令写对了，且它是 §3.1 `message` 禁令（第 92 行）的必需前置。此条我给正面确认，不是挑错。

### 3.3 我自己第 1 轮的两处"复现"里含未复核的转录数字

`CODE_EN_FALLBACK` 我第 1 轮写 **17 码**，处置表 Q17 改判 **15 码**；我直接 `sed -n '79,101p'` 回读原文件逐条数：条目为 `E2050/E2049/E1194/E2052/E3094/E3095/E3096/E3097/E3144/E3145/E1343/E4060/E4061/E4062/E4065` = **15**。我当时是从子任务给的 `79-99` 行区间里按行数估的，区间内夹着两行注释。⇒ **对方纠正得对，我认领**；同时这也说明我第 1 轮报告里凡是"来自子任务行区间"的数字都应视为未回读（我第 1 轮已在别处声明过这条纪律，这次是我自己没执行到底）。

---

## 4. v1.1 正文的新缺陷（均为本轮新取证，不针对任何他人报告）

### 4.1 P1-3：处置表与正文对同一变量给了两个名字

- 处置表 Q20 行：「新变量统一为 **PUBLIC_ERROR_CONTRACT_SNAPSHOT_UPDATE**，两类刷新命令同节说明」。
- 正文 §7.4 第 354 行：「采用显式环境变量 **`UPDATE_PUBLIC_ERROR_CONTRACT=1`** 更新快照」；正文 §11.2 第 528 行：`$env:UPDATE_PUBLIC_ERROR_CONTRACT = '1'`。
- 同一节第 540 行另一个变量 `FRONTEND_CONTRACT_SNAPSHOT_UPDATE` 已按既有实现写对（与 `FrontendContractSnapshotTests` 一致）。

⇒ 这是"处置已定、正文未同步"的一类，且**正文两处互相一致、与处置表不一致**，执行者照 §11.2 复制粘贴就会落地旧名。**改法**：二选一并同提交——若取处置表的 `*_SNAPSHOT_UPDATE` 命名（我建议这个，理由是与既有 `FRONTEND_CONTRACT_SNAPSHOT_UPDATE` 同形、CI 与脚本可按统一模式识别），则 §354/§528 两处同步；若保留 `UPDATE_PUBLIC_ERROR_CONTRACT`，则要求把处置表 Q20 改成"已自行更正"（它已经三次做过这类更正，成本一句）。

### 4.2 P1-1：同一个既有异常，两处给出两个 kind，而启动校验抓不到

- §5.2 第 217 行：`PublicKind` 固定取八值、"对应 CustomerErrorKind 同名项"、"多个业务公开码可共享一个 kind"，且第 219 行把 kind 定位为**注册表字段**（模块 Provider 声明）。
- §3.2 之后第 119 行最后一句：「503 图片繁忙可保留等待时间但 **kind 仍为 server**，不伪装成 429」。
- 事实：`AbpAdminImageProcessingBusyException.cs:12,19` 是带 code `AbpAdmin:ImageProcessingBusy` 的 `BusinessException`。当有人为该码登记 `PublicKind = rateLimited`（429 语义最贴近"稍后重试"）时，第 119 行的规则要求 `server`，第 217 行的规则要求 `rateLimited`——**两条都对得上，且互相排斥**。
- §5.2 第 219 行的启动失败条件只写"同一内部键重复登记 / 同一公开码不一致定义"，**没有一条检查 kind 与 §3.2/§119 的特例是否冲突**。⇒ 冲突会在实现期以"代码里加个 if status==503"的形式解决，从而**绕过第 332 行刚建立的"公开码优先，状态只作安全兜底"的优先级**（正是第 333 行明令禁止的"状态覆盖已识别公开码"）。
- **改法（两句 + 一条断言）**：① 把第 119 行末句改为"注册时对该码声明 `PublicKind = server`（或 `business`），等待时间照常按第 332 行的**公开码→kind**路径给出；不得在客户端按状态码改写 kind"；② §5.2 第 219 行的启动失败条件加一句"**同一公开码的 kind 与其所属 §3.2 行的语义类别不一致时启动失败**"；③ A31（第 481 行）加一条"503 图片繁忙的 kind 由注册表决定，且与 `Retry-After`/`data.retryAfterSeconds` 同时存在"。

### 4.3 P1-2：管理路径的 `data` 键集合没有一条断言，"保持既有形状"会被读成"照旧整包透传"

- §4.3 第 174 行：「`AdminCompatibility` 也不默认允许任意 details/Data；按已审核功能保留**需要的字段**」——方向对。
- 但 §4.3 第 173 行与 §3.1 第 94 行都写"管理端**既有字段形状保持兼容**"，而既有形状的真实生产者是 `FriendlyBusinessExceptionMiddleware.cs:91`（`data = business.Data`，无过滤）与框架默认（§3.1）。⇒ 只照字面"保持形状"实现，就是**继续整包透传**，而 §174 的"不默认允许任意 Data"没有任何测试兜住。
- 检查 §10：A03 与 A26 都在讲**客户**响应与插值参数；**没有任何一行断言管理响应的 `data` 键集合**。
- **这不是 P0**（不阻塞开工，D3 也确实要兼容），但它会让 §174 变成一句"愿望"。
- **改法**：① §4.3 加一句"管理兼容的 data 白名单按出口登记；`ExtraProperties` 复制来的键**一律不得**出现在任何响应 data 中（客户、管理同规则）"；② A05 或 A17 追加半句"管理响应的 `error.data` 键集合 ⊆ 该出口的批准集合"；③ P0 清单（§385）每行的"当前形状"列要求抄写实际 `data` 键（这条 §248 已要求 P0 记录当前形状，只差把这半句和 A17 挂钩）。

### 4.4 P2 一览

| # | 位置 | 问题 | 建议 |
| --- | --- | --- | --- |
| P2-1 | §5.1 第 191 行、§8 第 364 行 | 上一轮 P0-2 已改成"完整性校验只作用于标记的静态客户资源"（§276 已写好），但**没有一条 A 断言"客户资源缺键时不回显码"**；§34 只说"整个回退链缺键 → 安全兜底" | §6.9 补半句"客户文案解析器只走本方案内置安全兜底路径，不使用会回显键名的 `IStringLocalizer` 直取"；A34（第 484 行）加一条"缺键时 message 不含码前缀"。成本：一句 + 一个断言列 |
| P2-2 | §9-P0 第 388 行、A17 第 467 行 | 把"图片繁忙 503/Retry-After"列为**现有兼容契约**，但管理前端今天**没有 503 分支**（`requestErrorConfig.ts` 仅 `:88/:98/:112/:126` 判 401/403/429/404），503 走通用错误提示；§2.1.4（第 51 行）正确地只列了 404/403/429 | 二选一写清：或承认"管理端 503 等待展示是本期新增能力"，或把 §388/A17 的措辞改为"503 仍走通用提示、仅断言 Retry-After 头不被 `writer.Clear()` 清掉"（§225 末句的保留意图正好是后者）。后者更省且不新增行为 |
| P2-3 | §5.1 第 191 行 | §11.2 命令块已把 `dotnet restore`、七套 EF 漂移、docker 构建都补齐（上一轮 P2-4 已落地），但未提 `ci.yml:29` 的 `check-docker-restore.ps1` 里 **restore 闭包**这一项与本期"新增 TestServer 依赖包"的关系：新加包必须进 restore 闭包，否则 CI 第一步就红 | §11.2 依赖包那句加半行"新增包需同步 `scripts/check-docker-restore.ps1` 的闭包清单，否则 CI 首步失败" |
| P2-4 | §4.1 第 153 行 | 登记键已升级为"类型全名 + 方法名 + 参数类型签名"，但 §10 的 A27（第 477 行）列的场景是"新增/删除/重载/路由别名"，**没点明"同名重载"要有正例**——而第 153 行新增的正是靠参数类型区分重载 | A27 追加半句"含一对同名不同参重载的正例，缺参签名时登记失败" |
| P2-5 | §7.4 第 358 行 | 快照路径改为从 `AppContext.BaseDirectory` 上溯定位 `backend/AbpAdmin.slnx`（吸收了上一轮建议，且 `HostUnderTest.cs` 已实现同一手法，做法可复用）。但只写"打印解析后的绝对路径"，未写**多仓根/嵌套解决方案**时如何消歧 | 一句"以 `AbpAdmin.slnx` 与 `web/package.json` 双锚点同时命中为准，否则失败"（`HostUnderTest.ResolveHostContentRootPath` 可参照，见 `HostUnderTest.cs:112-131`） |
| P2-6 | 第 646 行 §13 | 新增"规划会话已运行 exchange 21 项 / abp_admin 22 项，来源为本会话工具输出，未保存独立日志文件"——诚实，但 §11.3 又要求执行者记录命令与结果，两处标准不同 | 建议给这两条基线各补一行"复核命令"，让实施者能在开工第一天重跑一次并留档 |

---

## 5. 处置表中指向本报告的行号，我逐条回核的结果

回读我自己第 1 轮原文对应行：

| 处置 ID | 声称的原报告行 | 该行实际内容 | 判定 |
| --- | --- | --- | --- |
| Q01 | 30、225 | L30 = P0-1 表格行（"清单漏掉了两…"）；L225 = §10 第 1 条"补 §5.3 的现有出口清单" | **准确** |
| Q10 | 106、136、240 | L106 = "与 ABP 侧姿态一致"；L136 = "反向也要写：ABP 原生路径今天一个 Data 都不发"；L240 = §11.2"未起 HTTP、未抓包" | **准确**（三处正是我这条错的论点、论据、自我披露三处同现，处置表引得比我自己的组织结构更清楚） |
| Q11 | 134 | L134 = "`:74` 的 `localizer[business.Code].Value` 没有缺键保护…" | **准确** |
| Q13 | 150–151 | L150 = 规模表"HttpApi 手写控制器 8 个 + 4 个贡献者"行；L151 = "业务/平台模块控制器 0"行 | **准确** |
| Q14 | 160 | L160 = §6 改法第 2 条"过渡态定义…记日志不改响应" | **准确** |
| Q07 | 124 | L124 = "按最省事的写法（`if status===403 → 'forbidden'`）…" | **准确** |
| Q20 | 186–191、229 | L186 = §8.1 小标题；L191 = §8.1 改法条目；L229 = §10 第 5 条 | **准确** |
| Q26 | 90 | L90 = §3.1 关于 `Retry-After`/`data.RetryAfterSeconds` 的那一条 | **准确** |
| Q25 | 80–86 | §3.1 勘误表所在区间 | **准确** |
| Q32 | 47–54、61–74、209–216、225–233 | §2.1 核验表、§2.2、§8.3–8.4、§10 区间 | **准确** |

**结论：指向 qwen 的行号引用本轮我抽查 11 处，11/11 命中，无需更正。** 另外处置表第 37 行已声明"表中 canvas 行号指修订前版本；本轮已同步其摘要措辞"，与我的理解一致，不再提形式要求。

---

## 6. 开工前 30 分钟可完成的改动（按此顺序做）

1. **统一快照变量名**（P1-3）：§354/§528 改 `PUBLIC_ERROR_CONTRACT_SNAPSHOT_UPDATE`，或反向更正处置表 Q20。
2. **拆掉 503 的 kind 双写**（P1-1）：改 §119 末句 + §219 启动条件补半句 + A31 补半句。
3. **给管理 data 键集合一条断言**（P1-2）：§174 加"ExtraProperties 键不得出现在任何响应 data"、A17 加"⊆ 批准集合"。
4. P2-1…P2-6 各一句，可与上面三条同提交。

**第一步任务选择**：仍建议 §9-P0 第 3、4 项（宿主最终装配端点 + 非 MVC 写出者清单，含 §385 要求的"当前形状/消费者/兼容变化"三列）——它不依赖上面任何未决口径，而且它正好会产出第 3 条所需的 data 键事实清单。**不要**先动 §5.3 的 filter/中间件替换：§231 那条"隐式中间件替换"与 §332–333 的 kind 优先级目前都还有 P1-1 这一处未闭合。

---

## 7. 本轮的正面变化（点名，供后续轮次沿用）

1. **处置表把"拒绝"写成了带可复现证据的句子**，而不是一句"不采纳"。Q10/Q11/Q13 三条都能让我一次命令自查（`AbpExceptionHandlingOptions` 构造函数、`.Add<AbpAdminResource>("en")`、`TokenGateway/Http/` 目录）——这是这条回路里沟通成本下降最大的单轮改进。
2. **Q20 这类"已自行让步那一半"的写法**（指出被拒的只是"框架测试反向引用业务模块"这一支，其余采纳）正是我希望后续轮次使用的形式；它让下一轮不必再猜"你是不是还在争"。
3. §11 新增的**"不能复用已经 Build 的 ServiceCollection，也不能自行抄一段 filter/middleware 制造永远通过的影子管线"**（第 498 行）比我第 1 轮提的"用真实 DI 做装配断言"更可执行，而且它直接采用了仓里已有的 `HostUnderTest` 这一现成设施（`:13-19` 的设计注释逐字写着"每个测试必须看同一次启动，否则副本漂移而测试常绿"）。**这条我第 1 轮没想到。**
4. §11 新增的**进程隔离夹具**（第 500 行，理由为 `HostTestBed.cs:10-15` 已记录的 Quartz 静态 LoggerFactory 问题 + 静态脱敏闸）同样是我第 1 轮的空白。
5. §12.2（第 611 行）把回退分成"仅样例可关"与"全局链必须整版回退"两类，正是我第 1 轮"没有 kill switch → 爆炸半径未定义"那个关切的解法，且它拒绝了我提的 dry-run 形态后给了这个更安全的替代。

---

## 8. 审核局限

1. **本轮未运行任何测试**。第 1 轮实跑的 `requestErrorConfig.test.ts`（22 项）与 exchange（21 项）结果本轮未重跑；正文第 646 行已把它们改标为"本会话工具输出、未保存日志"，与我一致。
2. **未起 HTTP、未抓包**。§3.1 的四步链条全部是源码级证据（框架默认值 + 两个子类 + 337 处 `WithData` + 中间件 `:91`），但"503 图片繁忙的客户响应今天实际长什么样"仍是一次实抓即可关闭的推断——建议实施第一天用 `curl` 打一次限流与一次图片繁忙端点，把两条实际 body 抄进 P0 清单，然后 §3.1/P1-2 的争辩就不再需要。
3. **ABP 结构类结论一律来自 tag `10.7.0` 原文**（本地包只有 DLL/pdb，pdb 未嵌入源码、XML doc 无相关成员，无法判 virtual/private）；本轮新增复核的文件为 `AbpExceptionHandlingOptions.cs`、`AbpLocalizationOptions.cs`、`AbpDictionaryBasedStringLocalizer.cs` 三个，均按该 tag 抓取。
4. **他人结论的暴露**：处置表转述的 grok/hy4 判断（`hy4 182 行`、`Grok 62/79 行` 等）我**只核了理由与代码是否一致**（§2 表末三行），未打开原报告，也未评价其结论价值。
5. **计数类数字的测量面**已随数字标出：337（`WithData` 出现次数，含定义/调用混计，作"面广"用，不作规模）、15（逐条回读原文件）、5 个 TokenGateway 控制器文件（`ls` 直读，其中 `GatewayControllerBase` 为基类）、4 个具体控制器（处置表口径，与我 `ls` 结果一致）。
6. **时变读数已复测**：计划与处置表的 mtime/size/sha、`git status`、HEAD 均在 16:14:14 复测，与 14:49/15:17 读到的版本一致；报告正文引用的行号对 `c5f78192…` 版本有效。若正文再被编辑，§4 的行号需重新对表。
