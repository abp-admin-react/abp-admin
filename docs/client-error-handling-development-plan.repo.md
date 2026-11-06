# abp_admin 客户端错误处理开发方案

> 版本：1.2；日期：2026-11-06；状态：P0—P4 已实施。标准仓库渲染版见 [client-error-handling-development-plan.repo.md](./client-error-handling-development-plan.repo.md)。
>
> 本文提供完整实施规格。产品范围与六项设计取舍已经确认。逐项审核结论、拒绝理由及证据行号见[第一轮处置（含勘误）](./client-error-handling-development-plan-review-disposition.md)及[第二轮处置](./client-error-handling-development-plan-review-disposition-r2.md)。
>
> 仓库根目录：`E:\code\abp_admin`。下文文件清单中的相对路径均以此为根。参考项目：`E:\code\exchange\exchange`，仅供借鉴，不在本次改动范围。

## 1. 目标、已确认决定和交付边界

### 1.1 目标

将 exchange H5 的“按业务码提示、未知错误兜底、机器码与展示文案分离”吸收为 abp_admin 的通用框架能力，并补上服务端公开范围控制。客户不仅在页面上看不到内部异常，在浏览器网络响应、多语言资源和客户任务结果中也不应取得内部错误细节。

只借鉴上述机制，不迁移 exchange 的响应包装或“HTTP 200 + body 状态码”用法。取消、限流等待、会话并发和写结果未知是本框架新增的约束，不能视为已经由 exchange 验证。

完成后，业务模块通过代码声明公开错误，后端统一返回安全的多语言文案；客户前端复用统一错误协议和处理器，管理端保留已经批准的业务提示及页面行为。

### 1.2 已确认决定

| 编号 | 决定 | 执行含义 |
| --- | --- | --- |
| D1 | 公开业务原因，隐藏内部信息 | 保留余额不足、需要认证等可操作原因；内部码、SQL、堆栈、路径、配置及未批准参数不返回客户。 |
| D2 | 开发配置，随版本发布 | 公开目录、端点登记和客户文案在代码/配置文件中维护；首期不上在线配置后台。 |
| D3 | 管理端兼容，客户端严格 | 兼容响应结构、HTTP 语义和安全业务提示；完整诊断留在受权日志入口。 |
| D4 | 客户入口独立，业务逻辑复用 | 客户 Controller/AppService 入口与 DTO 独立；复用内部业务服务；共享接口明确登记且固定使用公开规则。 |
| D5 | 首期做框架机制与示例 | 完成通用错误、模块扩展、客户请求适配、模板接入及测试。账务/收款/订单的公开错误随客户 API 接入逐个整理。 |
| D6 | 后端统一客户多语言文案 | 客户文案独立、静态、随版本发布；前端只维护离线、超时、取消等本地提示。 |

### 1.3 首期交付物

- 公开错误目录、定义校验器和业务模块注册契约。
- 按端点和可信授权选择规则的 HTTP 对外响应投影器。
- 异常、认证/授权、校验、限流、文件传输、手写错误响应的适配。
- 客户语言资源隔离，以及通用配置、本地化 JSON 和脚本出口的统一裁剪。
- 独立客户请求适配器、纯 TypeScript 错误类型和前端公开机器契约快照。
- Biz.Template 的最小客户接入示例及测试，不建设完整 H5 产品。
- 自动化验收、模块接入说明及框架契约文档更新。

不在首期范围：全量翻译现有业务模块错误；修改 exchange；新建错误管理后台；建立完整客户身份产品；改造资金业务或幂等机制；生产访问、部署和数据库数据修复。预计不需要实体或数据库结构变更；不得为了错误目录新建业务表。

首期不支持租户在线覆盖客户错误文案，不建设跨源 Cookie/CSRF 客户认证模式或客服 SLA。现有 Hub 推送和 TokenGateway/SSE 保持各自协议，不统一改成 ABP 信封；但它们必须进入出口登记，并验证不会被新全局处理链误写。“保留协议”不豁免同入口的未捕获异常：/connect、/v1和Hub握手都不得落到详细异常页。SSE未开始前/已开始后的失败分别适配；现有HTTP200管理诊断DTO仍按原权限保留，不向客户复用。

## 2. 已核实的现状与实现约束

### 2.1 可借鉴与必须补齐的部分

1. exchange 的 `Web-H5/src/lib/tradeErrorMessage.ts` 按码翻译，未识别错误统一兜底；`src/api/request.ts` 独立附加业务码。这一机制值得保留。
2. exchange 后端 `AdminResultProvider` 仍发送原始错误文本，H5 只在接收后替换文案。因此不能直接移植前端函数后宣称完成响应过滤。
3. exchange 将“存在翻译”部分等同于“允许客户看”，有运营口径混入。新框架必须将公开资格与翻译资源独立定义。
4. abp_admin 的管理前端当前优先展示 `validationErrors/details/message`；`skipErrorHandler` 会直接抛出原始异常；404 全局静默是既有契约。
5. `FriendlyBusinessExceptionMiddleware` 独立构造 ABP 信封并返回 `business.Data`；只改默认 ABP 出口会漏掉它。
6. `IExceptionToErrorInfoConverter` 同时用于 HTTP 和审计记录。不得全局替换它来过滤客户响应，不得把内部诊断一并删掉。
7. 现有匿名配置裁剪只处理 settings，语言资源保留；JSON 与脚本均有可达出口。错误信封与资源目录必须同时治理。
8. `AbpAdminDomainModule` 给所有资源追加数据库语言贡献者。仅新增客户资源类不能保证“文案随版本”，必须排除动态覆盖。
9. 新语言目录必须补入两个项目的 `EmbeddedResource Include`，否则开发目录看似存在，发布包却丢文案。
10. 当前本机Umi生成runtime支持在共享axios实例上临时挂载每请求拦截器，见 `web/src/.umi/plugin-request/request.ts` 的 `getRequestInstance` 与 `opts.requestInterceptors/responseInterceptors`；生产生成物有同样行为。package.json的`^4.7.24`是声明范围，不能单独证明实际安装版本；执行时记录锁文件/包元数据版本。这与业务层静态拦截器配置不同。首期选用独立fetch，不修改生成物。
11. `UseUnitOfWork()` 已隐式安装 `AbpExceptionHandlingMiddleware`，位于外层友好异常兜底的内层。必须适配它，不能等待异常再次冒泡到外层。
12. ABP10.7.0的SendExceptionDataToClientTypes默认含IBusinessException，本仓限流异常及ImageProcessingBusy均继承BusinessException并带Data；原生转换器一般业务分支与Friendly手写分支都需投影，前者还可把Data插值到message。不能把该风险限定为某个异常必经Friendly，也不声称每个异常都会走同一运行时路径。第二轮在backend C#源码排除bin/obj后检得337个含WithData词元的源码行、311个.WithData(词元，仅说明盘点面，不是337个漏洞或调用点门槛。

### 2.2 仓库纪律

- 先阅读 [后端约定](../backend/AGENTS.md)、[前端约定](../web/AGENTS.md)及[框架契约](./framework-contracts.md)。
- 以当前 `.NET 10 / ABP 10.7.0` 为基线；若执行时版本变化，先重验异常和资源扩展位，不凭旧签名编写代码。
- 能力落在 `backend/framework-app/`，不能把业务模块实现反向引用进框架；`HttpApi` 不引用 `Application` 实现。
- 前端 API 主层只在 `web/src/abp/` 手写维护，不恢复已退役的 OpenAPI 服务生成器，不新增 `src/services/abpadmin` 消费者。
- 保留执行前已有用户改动；本次规划时已有 `docker-compose.yml` 修改。执行前重新检查状态，不能覆盖或提交无关改动。

## 3. 统一错误契约

### 3.1 客户 ABP 错误响应

沿用 `{ error: { code, message, details, data, validationErrors } }`，不引入另一套业务响应包装，不把所有失败改为 HTTP 200。

```json
{
  "error": {
    "code": "BizTemplate:Public:ProjectUnavailable",
    "message": "该项目当前不可用，请刷新后查看。",
    "details": null,
    "data": {
      "correlationId": "example-support-reference"
    },
    "validationErrors": []
  }
}
```

字段规定：

- `code`：稳定公开码，用于页面流程判断；不要求与内部码一一对应，允许多个内部码映射到同一公开码。页面默认不显示码本身。
- `message`：后端从客户专用资源和批准参数生成。不得直接使用 `Exception.Message`、内部本地化模板或第三方错误原文。
- `details`：客户端始终为 `null`。不开放将其恢复为内部详情的配置。
- `data`：新建字典，仅包含定义中批准的安全字段及关联编号。客户端新字段统一 lowerCamelCase；管理端既有字段形状保持兼容。
- `validationErrors`：保持 ABP 项结构，`members` 只允许客户 DTO 的公开字段；`message` 来自公开校验规则。未知规则用通用校验提示，不拼接框架原文。
- 关联编号：新增Host的 `ClientErrorCorrelationIdMiddleware : AbpCorrelationIdMiddleware`，DI替换原服务键并保留现有UseCorrelationId位置。override GetCorrelationIdFromRequest：所有外部HTTP入口（客户和管理均含）每请求生成新GUID("N")作为canonical关联号，覆盖 `Request.Headers[AbpCorrelationIdOptions.HttpHeaderName]`并返回该值；首期没有可信内部透传例外，不在认证前猜管理资格。基类InvokeAsync继续以ICorrelationIdProvider.Change建立作用域。投影仅读取，不另造编号；response body、日志、审计使用同一值。
- 传入关联号仅单值、1—64位`[A-Za-z0-9._-]`时可作为日志中的“不可信上游线索”单独记录；它不是主关联号、不控制审计归属或检索授权，也不作为指标标签。非法值不记录原文。相同传入值的两次请求必须生成不同主关联号。编号没有独立授权/有效期语义；未来确需可信服务间透传，单独建立由部署身份验证的入口规则。
- 同一子类override CheckAndSetCorrelationIdOnResponse注册唯一OnStarting回调：SetResponseHeader开启时把同名响应头设为捕获的canonical值，不接受下游冲突值；关闭时不写该头。此override不调用基类该方法，防止两个回调竞争；其他writer不得写同名头。回调晚于下游注册的回调执行，Clear响应后仍可写出。IncludeCorrelationId仅控制公开body字段，响应头沿用AbpCorrelationIdOptions.SetResponseHeader。A22断言真实执行及单点写出。

`validationErrors` 的项明确为 `{ "message": "安全文案", "members": ["公开字段"] }`，不是 `ValidationProblemDetails.errors[field][]`。样例和解析测试都必须覆盖 members。

### 3.2 框架基础公开码

以下为首期拟新增并固定的公开契约；代码前缀不表示信任，必须在目录内精确匹配。

| 公开码 | 语义 | HTTP 与展示 |
| --- | --- | --- |
| `Client:AuthenticationRequired` | 需要登录/会话不可用 | 401；客户会话协调器负责登录恢复。 |
| `Client:Forbidden` | 没有权限 | 403；保留会话，不当成退出登录。 |
| `Client:ValidationFailed` | 输入不满足公开校验规则 | 保持端点校验状态；表单优先接管。 |
| `Client:NotFound` | 资源不存在或不可见 | 保持端点既有隐藏资源规则；不暴露实体类型和主键。 |
| `Client:RateLimited` | 请求过于频繁 | 429；保留安全 `Retry-After`/等待秒数。 |
| `Client:Conflict` | 数据状态已变化/并发冲突 | 保持原状态映射；提示刷新，不暴露数据库约束名。 |
| `Client:OperationFailed` | 未公开登记的业务失败 | 保留原业务 HTTP 状态，通用提示。 |
| `Client:Unexpected` | 未知系统故障 | 按正常异常状态映射返回，通用提示。 |

HTTP 状态由现有 `IHttpExceptionStatusCodeFinder`、`IHasHttpStatusCode` 或端点协议决定，不仅凭公开码强制改写。ABP 业务异常可能返回 403，不能把所有 403 都判断为“登录过期”。认证、校验、限流等确定类型优先于未知错误归类。

OAuth/OpenIddict 返回仍遵循 `error`/`error_description` 等原协议；控制可公开内容并保留 challenge/forbid、`WWW-Authenticate` 与限流头，不能套进上述 ABP 信封。

等待时间按单一安全计算结果输出：客户writer有等待时间时同时填Retry-After和lowerCamelCase data.retryAfterSeconds，管理端继续兼容data.RetryAfterSeconds。客户解析优先合法响应头（非负整数秒或HTTP-date），其次是批准的body秒数；冲突优先header并记录无敏感诊断。非法值丢弃，0合法，超过86400秒不展示具体倒计时；无可靠值使用通用提示。HTTP-date按当前时间计算的负差归0，不自动重试请求。writer若Clear响应，必须从批准的等待值重新写头，不保留任意原头。不得要求每个429必有正数等待秒数。

首期明确将内部 `AbpAdmin:ImageProcessingBusy` 映射至 `Client:Unexpected`，目录kind固定server、实际HTTP503，基础server定义允许可选的有界retryAfterSeconds；其message仍是静态通用故障提示，不拿等待参数插值。不是让前端看到503后覆盖已识别kind。已有管理端503仍走通用提示，只保持后端503与Retry-After；本期不新增管理端503倒计时交互。

### 3.3 成功响应中的失败信息

异步任务、批量操作、导出状态等即使 HTTP 200，也不能原样返回内部 `ErrorMessage`。

新增客户专用 `ClientFailureDto`（放 `Application.Contracts/ClientErrors/`），只包含 `Code`、`Message`、`CorrelationId` 和确需的公开字段结果；业务 DTO 映射时调用同一投影器的非 HTTP 核心逻辑。允许部分成功，保留原逐项结构。

Code、Message 必填；CorrelationId 可空。只有失败字符串、没有结构化内部码时，一律映射 `Client:OperationFailed` + 该码的静态通用文案，不分析字符串反推业务原因。原串只留受权日志。后台作业沿用其执行时关联号；历史结果缺关联号时可保持空，不伪造一个无法关联到历史日志的新号；本次读取请求的关联号单独记录。

不得全局递归替换成功 JSON 中的字符串。每个客户 DTO 的失败字段需要显式接入；既有管理监控成功 DTO 不在本轮全量重写范围，也不得直接复用于客户 DTO。

### 3.4 参数和校验的公开规则

- 参数定义必须包含来源、类型、长度/范围；禁止任意对象 `ToString()`，禁止把 `ex.Message` 放进名为 `reason` 的允许字段。
- 模板插值前先筛选参数，不能先用全部 `Exception.Data` 生成文案，再仅从响应 data 删除敏感项。
- 参数缺失或类型非法时使用该错误的无参数安全兜底或通用提示，不抛出第二个格式化异常。
- 校验由“客户 DTO 字段 + 已知规则”映射到客户资源；不凭消息正则反推规则。不可识别的模型绑定错误只给通用校验提示。
- 登录、找回密码等匿名流程的公开码和消息不得额外区分账号是否存在；保留既有协议必要语义。

## 4. 端点分类与管理兼容

### 4.1 三类策略

新增普通 CLR 元数据 `ClientErrorAudience` 及 attribute/登记契约；不在轻量契约层引入 HttpContext 或 MVC 依赖。

| 分类 | 适用范围 | 输出 |
| --- | --- | --- |
| `Public` | 客户独立入口、匿名入口 | 仅公开错误目录。 |
| `Shared` | 明确共用的账户、配置及资源入口 | 固定使用公开规则，管理员调用也不放宽。 |
| `AdminCompatibility` | 已登记且经过授权的管理功能 | 保留经过审核的管理业务语义和原结构，不返回原始系统诊断。 |

未分类受控API运行时默认Public，包括Development和测试环境。全局filter/中间件生效前，必须完成最终宿主装配中framework-app、全部已装配platform/biz模块和第三方管理API分类。新增测试同时枚举EndpointDataSource和IActionDescriptorCollectionProvider，后者是待实现检查，不声称已有；手写中间件、OAuth、SSE/Hub、Razor、静态资源和健康检查另列协议/非目标行，不能把“没有MVC action”当成遗漏理由。原协议保持不豁免其未捕获异常，非API页面才按原异常页处理。

登记键采用“具体控制器/服务类型全名 + 方法名 + 参数类型签名”，同时记录 HTTP method/route 解析结果；仅 `(类型,方法名)` 不足以区分重载。非 MVC 出口使用稳定 ExitId + 生产者类型/调用位置。类型/方法元数据标记优先，批量扫描只生成候选；不以权限组前缀、命名或路径自动授予 AdminCompatibility。

新增 `AllEndpointsHaveAudienceTests`：实际装配与登记双向一致，登记失配、缺项、多余条目、非法分类、管理项未绑定资格均失败；已有管理项改为 Public 的提示变化必须列在兼容清单。全局替换提交与登记表及管理回归同提交通过，不能等到 P4。禁止用“开发 dry-run 不改原响应”绕过默认安全规则；可以离线预览分类差异或在隔离测试中对照新旧对象，但不向客户发送原响应。

端点登记之外新增 `ErrorResponseProducerInventoryTests`，扫描framework-app/modules生产源码中的WriteAsync/WriteAsJsonAsync、ObjectResult/JsonResult、BadRequest/Forbid/NotFound/StatusCode、Results及已登记协议helper调用。用语法树提取文件、包含类型/方法签名、归一化调用和出现序号，避免行号漂移作主键；与提交的 `ClientErrors/Snapshots/error-response-producers.json` 候选清单双向比较，新增/删除/调用变化使CI失败。每个候选必须关联ExitId，或明确“非错误输出/非目标”及理由；wrapper按符号登记并有扫描器自身的合成覆盖测试。WithData、ReadOAuthError和预插值等原文来源另列SourceId并关联出口，不能混称为写出点。这个门禁检测已知写出方式的漂移，不宣称静态扫描可证明动态/第三方全部无泄漏，仍须实际HTTP与协议用例。

### 4.2 管理资格判定顺序

1. 必须是登记表中的具体端点/服务方法，且声明 `AdminCompatibility`。
2. 会话、租户、认证状态已通过校验。
3. 已有的管理功能资格权限通过，例如该功能原有管理读权限；不新增需要给全部角色补授的全局权限。
4. 原类级、方法级、资源级授权全部继续执行；多个条件保持原 AND 语义。当前请求本身授权失败时不得升级错误输出。
5. 原权限只在 AppService 代理上时，登记必须关联实际服务方法/有效权限组合，不能只查看 Endpoint.Metadata。SettingUi 是必须覆盖的现有案例。
6. 资格无法确定、资格查询失败、会话/租户未就绪时均降级 Public。

`clientType`、任意请求头、Origin、User-Agent、URL 前缀和公开 OAuth `client_id` 不能授予管理资格。它们最多是已经授权后的辅助上下文。仅 `[Authorize]` 的个人资料、我的通知等登记 Public/Shared，不把“已登录”等同管理者。

SettingUi 具体登记现有替换 `AbpAdminSettingUiAppService`：它以 ReplaceServices/ExposeServices 提供原接口和具体类型，RemoteService(false) 防止重复 Auto API，类级 `SettingUiPermissions.ShowSettingPage` 叠加写方法的 `AbpAdmin.SettingUi.Update`。这已是当前修复后的门禁，不得描述为当前仍匿名可写。资格降级按 ExitId、原因类别、版本计数；不记录 token、输入原值和原始异常文本，不用用户 ID 作为无界指标标签。

### 4.3 兼容的具体内容

- 保留管理端合法的业务码、本地化业务含义、HTTP 状态及页面分支。
- 保留当前管理前端 404 全局静默、403 单提示、429 等待信息等契约。
- `AdminCompatibility`也不默认允许任意details/Data；按ExitId登记批准的字段名、大小写、来源、类型和范围，兼容的是已批准字段形状，不是原Exception.Data全集。管理响应键集合必须是该出口批准集合的子集，未批准字段丢弃。ExtraProperties没有自动公开资格，但也不按容器来源永久禁用：只有逐字段批准、来源受控且值通过校验时才可投影；同名键不能覆盖框架批准值。
- 含内部 `ex.Message` 的无码友好异常不自动放行。对确实需要保留的安全管理文案显式登记；其余使用管理通用提示和关联号。
- 管理路径也先过滤批准参数再用管理资源插值；不能复用默认转换器已经用完整 Data 格式化的 message。有 code 的 UserFriendlyException 同样可能嵌入 ex.Message，P0 对全部此类构造点逐条标明保留/收紧，不把“49 个构造点”误称为“49 个无码异常”。
- 管理监控页已批准的诊断 DTO 仍按既有权限和契约运行；不能因本轮错误策略顺手全仓重写，更不能让客户入口直接使用这些 DTO。

受权排障复用当前 `/audit-logs?correlationId=...` 筛选及详情，权限为 `AbpAdmin.AuditLogs`，继续执行租户/数据权限；没有审计行的认证前失败或后台故障由运维在已有受权服务端日志中按同一编号检索。P1 验收响应与这些记录能串联，不新建客服查询平台、不承诺全部原始诊断必经审计持久化，也不新增日志保留期限/SLA。

## 5. 服务端实现设计与文件落点

### 5.1 目录与职责

下表新增文件名作为执行基线；如因现有命名冲突调整，需在同一提交同步本文和测试，不改变职责。

| 区域 | 新增/修改路径 | 职责 |
| --- | --- | --- |
| Domain.Shared | `backend/framework-app/AbpAdmin.Domain.Shared/ClientErrors/` | `ClientErrorCodes`、公开定义/Provider 契约、端点分类标记、不可动态覆盖资源标记、无 HttpContext 的投影输入输出契约。 |
| Domain.Shared | `backend/framework-app/AbpAdmin.Domain.Shared/Localization/ClientErrors/` | 客户基础错误资源及 JSON；首期覆盖现有前端支持的简中、繁中、英文。 |
| Domain.Shared | `backend/framework-app/AbpAdmin.Domain.Shared/AbpAdminDomainSharedModule.cs`、`backend/framework-app/AbpAdmin.Domain.Shared/AbpAdmin.Domain.Shared.csproj` | 注册资源、虚拟文件及新目录 EmbeddedResource。 |
| Domain | `backend/framework-app/AbpAdmin.Domain/ClientErrors/` | `PublicErrorDefinitionRegistry`、`PublicErrorOptionsValidator`、静态文案解析器、非 HTTP 公开错误投影核心。 |
| Domain | `backend/framework-app/AbpAdmin.Domain/AbpAdminDomainModule.cs` | 给语言资源追加数据库贡献者时跳过静态客户资源。 |
| Application.Contracts | `backend/framework-app/AbpAdmin.Application.Contracts/ClientErrors/ClientFailureDto.cs` | 客户任务/批量失败结果契约，不依赖 HTTP。 |
| Application | `backend/framework-app/AbpAdmin.Application/Localization/LanguageTextAppService.cs` | 拒绝对静态客户资源新增/覆盖；资源列表标明不可编辑或隐藏。允许按既有权限清理历史覆盖，不能堵死恢复默认操作。 |
| HttpApi | `backend/framework-app/AbpAdmin.HttpApi/ClientErrors/` | `ClientErrorExceptionFilter`、HTTP `ClientErrorProjector`、`ClientErrorResponseWriter`、端点 convention、管理端点登记与资格解析器、授权出口适配。 |
| HttpApi | `backend/framework-app/AbpAdmin.HttpApi/ClientErrors/Localization/` | 配置贡献者、独立语言服务/脚本适配、公开资源投影。 |
| HttpApi | `backend/framework-app/AbpAdmin.HttpApi/AbpAdminHttpApiModule.cs` | 默认注册上述防线；保留现有图片限流 filter 与匿名 settings 裁剪。 |
| Host | `backend/framework-app/AbpAdmin.HttpApi.Host/ClientErrors/` | 严格Options绑定、`ClientErrorCorrelationIdMiddleware.cs`入口关联号、`ClientErrorAbpExceptionHandlingMiddleware.cs`服务替换及协议感知API兜底；不把领域规则放Host。 |
| Host | `backend/framework-app/AbpAdmin.HttpApi.Host/AbpAdminHttpApiHostModule.cs` | 管线顺序、租户失配手写响应、会话/授权出口与异常页边界。 |

### 5.2 公开定义注册

模块以 Provider 方式注册定义，模块移除即移除自身声明，框架不引用业务实现。至少定义：

```text
PublicErrorDefinition
  InternalCode           内部查找键，仅服务端保存
  PublicCode             公开稳定码
  ResourceType           静态客户资源
  MessageKey             客户文案键
  AllowedParameters      每个参数的来源、类型、边界、是否可回传
  ValidationRules        公开 DTO 字段与校验规则映射
  PublicKind             允许导出给客户端的分类
```

PublicKind 固定取 `business | validation | authentication | forbidden | notFound | rateLimited | conflict | server`，对应 CustomerErrorKind 同名项；网络、超时、取消、结果未知只由本地 transport 判定。多个业务公开码可共享一个 kind，不是每个 kind 只能对应一个公开码。

基础公开码与kind是框架常量映射：AuthenticationRequired→authentication、Forbidden→forbidden、ValidationFailed→validation、NotFound→notFound、RateLimited→rateLimited、Conflict→conflict、OperationFailed→business、Unexpected→server。模块不可重新定义基础码kind；重复内部映射、基础码kind不一致及已登记ImageProcessingBusy映射成rateLimited均启动失败。

每个HTTP出口声明允许的(code, kind, status)组合，校验目录与出口契约；不为整个kind武断绑定唯一状态。401认证协议统一Client:AuthenticationRequired/无公开码，403允许business/forbidden但不允许authentication，RateLimited基础码用于429；其余业务状态与资源隐藏规则由端点登记。HTTP200任务失败是结果项契约，不把外层200拿来校验server等失败kind，也不触发会话动作。非基础模块码的合法多状态组合显式登记并导出公开组合，不靠前端临时if修正。

该结构是新契约设计，不是现成 ABP API。示例：内部 `BizTemplate:ProjectUnavailable` 映射到 `BizTemplate:Public:ProjectUnavailable`，无任何异常原文参数。多个内部码可收敛到同一公开定义；同一内部键重复登记或同一公开码出现不一致定义必须启动失败。

启动时只做内存内的目录和静态资源校验，不为此访问数据库、Redis 或外部服务，遵守现有宿主建图约定。

### 5.3 HTTP 出口接线：选定方案

1. **MVC 异常**：新增 `ClientErrorExceptionFilter : AbpExceptionFilter`，在最终DI替换原服务键，不并列添加第二个filter。override ShouldHandleException，使受控API的void/FileResult/非JSON Accept也进入处理；先查端点协议登记，OAuth/网关入口交其协议适配器，不能因Accept为JSON误套ABP。在未开始且不是授权/独立协议分支时，调用基类完成日志、通知和状态后，以新ObjectResult/RemoteServiceErrorResponse替换context.Result，不修改日志对象。授权分支由专用handler负责；HasStarted预先短路。装配测试断言原ServiceFilter唯一且DI解析到新实现，并保留图片503 Retry-After。
2. **授权异常**：适配 `IAbpAuthorizationExceptionHandler`；端点授权短路由 `IAuthorizationMiddlewareResultHandler` 适配。当前 Cookie 的 OnRedirectToLogin/OnRedirectToAccessDenied 对 `/api` 只设置401/403，不写 body，不能推断所有鉴权失败均已有ABP信封。对受控普通API，挑战/拒绝后若响应未开始且无协议body，统一writer补安全信封一次；已写OAuth/会话协议体不改形状。Razor导航保持跳转，Bearer与Cookie两条认证方式分别验收，保留WWW-Authenticate。
3. **协议感知API兜底**：扩展/收敛FriendlyBusinessExceptionMiddleware，覆盖受控入口未捕获异常，不限带码BusinessException或/api前缀。中间件放在开发/生产异常页内层、路由与业务处理外层。启动时建立ControlledProtocolRegistry：普通/api为ABP，/connect具体端点为OAuth，TokenGateway已登记/api与/v1路由为网关协议，/signalr-hubs为握手协议；精确到方法/路由/路径段，未知/api仍按公开ABP兜底。路由前失败按登记的入口范围解析，不靠客户端Accept或自报类型选择管理资格。非API Razor保留原导航行为。
4. **手写响应**：逐个改为统一 writer 或协议专用适配器；不能靠全量缓冲并重写所有 HTTP 字节流补救。
5. **HTTP 200 内部失败字段**：客户 DTO 显式调用 Domain 的投影核心；异常 filter 不负责扫描成功业务对象。
6. **响应已开始**：记录原异常及关联信息并按传输语义结束/中止；不修改状态、不追加 JSON、不把截断文件算成功。
7. **UseUnitOfWork隐式中间件**：保留UOW，通过DI把原AbpExceptionHandlingMiddleware键替换为ClientErrorAbpExceptionHandlingMiddleware，override InvokeAsync。其private包装方法不可override；不能先让基类写出再补丁修改。所有受控入口先选协议适配器并投影，再写一次；只有明确非目标的非API页面才可委托原行为。已完成的协议响应保持，原协议入口的未捕获异常不能以“非目标”为由重抛到异常页。外层兜底只处理尚未被消费的异常，通知/日志不得重复。

未开始响应的协议失败：/connect使用该端点支持的OAuth错误体（如server_error和固定公开description），网关使用既有固定GatewayError形状，HTTP握手失败按原握手状态/体契约收敛；都不返回开发/生产错误页HTML。保留既有Cookie握手302导航，不把它改成ABP体；“不得返回错误页”不等于禁止协议已有登录重定向。流已开始后只由协议handler写允许的安全错误帧或中止，外层不得追加ABP JSON。现有X-HB-Dev-Fault仅验证已捕获的两个网关错误与非Development忽略，未捕获/已开始流测试使用测试程序集注入，不扩展生产故障头。

禁止在投影时修改原异常、`Exception.Data` 或共用 `RemoteServiceErrorInfo`。ABP 审计、异常通知和服务端日志继续接收原始诊断对象，日志已有敏感值脱敏规则保持生效。

必须逐一接入并增加覆盖的现有文件：

- `backend/framework-app/AbpAdmin.HttpApi.Host/FriendlyBusinessExceptionMiddleware.cs`
- `backend/framework-app/AbpAdmin.HttpApi.Host/Files/FileUploadContentGuardMiddleware.cs`
- `backend/framework-app/AbpAdmin.HttpApi.Host/Identity/IdentitySessionValidationMiddleware.cs`
- `backend/framework-app/AbpAdmin.HttpApi.Host/OpenIddict/TokenEndpointRateLimitingMiddleware.cs`
- `backend/framework-app/AbpAdmin.HttpApi.Host/AbpAdminHttpApiHostModule.cs` 中的租户失配等手写返回。
- `backend/framework-app/AbpAdmin.HttpApi/Controllers/UserExportController.cs`
- `backend/framework-app/AbpAdmin.HttpApi/Controllers/AuditLogExportController.cs`
- `backend/framework-app/AbpAdmin.HttpApi/Controllers/FileShareController.cs`（匿名文件分享）
- `backend/framework-app/AbpAdmin.HttpApi/Controllers/GdprDownloadController.cs`（匿名令牌下载）
- `backend/framework-app/AbpAdmin.HttpApi.Host/ExtensionGrants/ImpersonationTokenExtensionGrant.cs` 及同目录其他 grant 的错误创建点（禁止 ex.Code ?? ex.Message 直接成为 error_description）。
- `backend/framework-app/AbpAdmin.HttpApi.Host/ExtensionGrants/ExtensionGrantSupport.cs` 的共用CreateErrorResult：安全OAuth描述收口，不能把固定公开描述也误作原文丢失协议语义。
- `backend/framework-app/AbpAdmin.HttpApi/Controllers/UserExportController.cs`、`AuditLogExportController.cs` 的Forbid/NotFound空体分支：在生产者显式调用安全结果helper，保留403/404及隐藏资源语义；授权异常handler不会自动覆盖这些ActionResult。

另外两类必须登记但不等于新增ABP writer：① 原文来源：Application/Account/ImpersonationTokenExchanger.cs、Account/LinkedAccountTokenExchanger.cs、OpenIddict/OpenIddictTokenExchanger.cs的ReadOAuthError→Data，以及AuditLogs/AuditLogAppService.cs的预插值友好异常；覆盖到真正写出者的合成参数用例，不删除原始诊断。② 原协议出口：TokenGateway/Http/WalletController.cs的501、ChatController.cs已开始/未开始SSE分支及握手拒绝；保持协议，测试安全兜底。以上Application路径均在backend/framework-app/AbpAdmin.Application下，TokenGateway路径在backend/modules/platform/AbpAdmin.TokenGateway下。

P0 出口清单记录当前形状：上传拒绝是 ABP 信封且有 fileName/reason；两个同步导出会直接返回 BadRequest(error字符串)；会话撤销与租户失配是 OAuth 风格错误；匿名下载返回 FileResult。每行指定客户公开码/协议适配器、管理兼容形状和消费者（含 window.open），不把“文件列出”视为已覆盖。TokenGateway/SSE、Hub 推送与既有200管理诊断字段另标原协议/非目标，不能强行重包；/signalr-hubs 的租户/会话握手拒绝仍受对应中间件公开规则约束。

当前 Host 在 `AbpAdminHttpApiHostModule.cs` 调用 UseUnitOfWork；ABP 10.7.0 的该扩展自动调用 UseAbpExceptionHandling，因此第7项是必做项。不得删除 UseUnitOfWork、手写其私有注册标记、额外叠加第二套原文处理中间件，或替换全局 IExceptionToErrorInfoConverter。记录基线包版本并通过扩展位/DI解析/输出行为测试检测升级漂移，不以全仓 grep 没有显式调用来判断管线不存在。

不得把公开安全建立在AbpExceptionHandlingOptions详细开关为false上：在SendExceptionsDetailsToClients和SendStackTraceToClients的true/false组合下，受控响应仍必须只包含投影新对象。保持现有安全部署默认值；本期不全局强关这些选项改变审计converter的诊断序列化。A21覆盖详细选项开启时也无原文，并与原始审计诊断保留成对验证。

### 5.4 部署配置

新增 `ClientErrorHandling` 配置节与 `ClientErrorHandlingOptions`，代码默认安全；下面字段为本方案新定义：

```json
{
  "ClientErrorHandling": {
    "FallbackCulture": null,
    "IncludeCorrelationId": true,
    "MaxPublicMessageLength": 1000,
    "MaxValidationErrors": 20,
    "MaxPublicParameterLength": 256
  }
}
```

- `FallbackCulture = null` 表示跟随应用默认文化；显式值必须属于公开资源支持集。
- 三个长度/数量限制为防御性默认值，只允许正数且不能超过框架上限；前端镜像一致限制。超限文案/参数使用安全兜底，不能截断后意外改变业务含义。
- 端点资格和公开映射由代码 Provider 注册，不能在配置文件随意指向类型、脚本或表达式执行。
- 无 `ShowAllErrors`、`ExposeDetails`、`AllowRawMessage` 等放开客户原文的选项。Host 对原始 ClientErrorHandling 配置节使用严格绑定（.NET 10 的 ErrorOnUnknownConfiguration=true 或等价原始键验证），拒绝未知/宽松键；仅校验已经绑定的 POCO 不能发现被丢弃的键。Domain validator 负责文化、类型和范围，职责分开。
- 不使用在线 Settings 存公开资格；模板开关另放 `BizTemplate` 模块配置，错误安全策略本身不能关闭。

## 6. 多语言与资源下发

1. 客户错误资源单独建目录，不能继承/挂载包含内部错误模板的资源；首期静态客户支持集固定为 `zh-Hans`、`zh-Hant`、`en`。这是本期明确支持集，不是对现有全部前端/后端文化数量的断言。完整性/占位参数/默认文化校验只作用于登记并标记的静态客户资源，不倒查所有历史管理资源；未支持语言回退客户默认文化。未来扩充客户支持集时补齐所有客户资源，CI检查。
2. 在 `AbpAdminDomainModule` 的贡献者接线处跳过静态客户资源；不得改用 GlobalContributors 将数据库贡献者重新加回；测试实际localizer创建后客户资源不含数据库贡献者。`LanguageTextAppService.UpdateAsync` 拒绝动态覆盖；读取和错误格式化都使用同一静态资源目录。只缺某个语言文件的键不等于必然回显内部码，当前管理资源有默认英语回退；真正的缺键测试必须覆盖整个允许回退链均缺键。
3. `RestoreToDefaultAsync` 保留清理历史覆盖的能力，遵守既有管理权限；清理不是修改发布版客户文案。已有历史覆盖即使未清理，也不得影响客户响应。
4. 通用 `application-configuration`、`application-localization`、`ConfigurationScript`、`LocalizationScript` 固定下发公开资源视图，管理员调用也不放宽。
5. 配置接口新增 `IApplicationConfigurationContributor`，与原匿名 settings 裁剪共存；独立本地化服务另行替换，并注册其接口及原具体服务类型，覆盖脚本控制器的具体类注入。
6. 公开资源视图按资源及键白名单构建；不能因登录页需要某个资源，就把混有内部错误模板的整份资源全部放行。管理专用资源使用独立入口，不用用户可控 audience 参数提升公共入口；公共缓存仅保存已投影公开视图。任何包含管理/动态资源的缓存键必须纳入文化、租户和经验证的权限视图版本；不能把原始管理资源对象缓存后当公共视图复用。
7. P0 盘点管理端实际消费。如果管理 UI 需要内部资源，提供显式管理资源入口并接线，按已登记管理资格与资源范围返回；不放宽 Shared 出口，也不为了保持兼容把整个 `/api/abp/*` 豁免。
8. 登录页、注册页、租户选择及 Razor 脚本需要的公开 UI 文案必须保留，纳入登录回归。语言解析沿用现有 culture/Accept-Language 机制，不另外建立用户语言存储。
9. 资源缺失或读取异常使用内置安全兜底；默认语言键、占位参数、继承闭包及目录冲突在启动阶段校验。客户解析器可使用IStringLocalizer但必须检查ResourceNotFound及允许回退链，禁止直接返回未校验Value；缺键时message不回显资源键/内部码。

## 7. 客户前端实现

### 7.1 文件与唯一传输选择

首期统一采用**独立原生 fetch transport + 注入式客户 auth adapter**。管理端继续使用当前 Umi 请求配置；不使用 Umi 的临时每请求拦截器，不新增 axios 依赖，不修改 `.umi` 生成物。

| 新增路径 | 职责 |
| --- | --- |
| `web/src/abp/customer/errors.ts` | 纯 TS 类型、ABP/OAuth安全响应校验、错误归一；不依赖 React、Antd、Umi、OIDC 或 window。 |
| `web/src/abp/customer/public-error-catalog.json` | 后端公开机器契约快照：仅公开码、分类、允许的公开字段、契约版本，不含内部映射及文案。 |
| `web/src/abp/customer/request.ts` | `createCustomerRequest({ transport, auth, messages, notify })`；强制归一、会话动作、展示责任、始终保留失败 Promise。 |
| `web/src/abp/customer/browserAdapter.ts` | fetch、相对 URL、语言、租户、CSRF、超时/取消、身份快照与浏览器接线。 |
| `web/src/abp/customer/example.ts` | 模板接口手写函数与客户 DTO。 |
| `web/src/pages/framework/customer-errors-demo/index.tsx` | 开发/示例视图和状态展示，不加入正式管理菜单；生产不开放。 |

API URL 使用标准 URL 解析器与可信页面 origin 归一化后，匹配同源及已登记客户/共享路径。只接收相对地址，拒绝协议相对地址、userinfo、反斜杠和会改变路由匹配结果的编码路径歧义；测试点段、编码斜杠、重复解码差异，不能只用字符串 startsWith 判断边界，也不能粗暴拒绝所有含 @/% 的合法查询值。fetch 固定设置 `credentials: 'same-origin'`、`redirect: 'error'`，调用方不得覆盖，避免 307/308 等跳转携带请求体或身份头到未登记入口。Bearer/CSRF/租户头不得自动转发到任意绝对 URL。复用既有语言与租户约定，客户 auth 依赖由调用方注入；不在这个工具库新建 token 存储，也不硬编码跳管理登录地址。交互式 OAuth 导航由独立 auth adapter 处理，不通过放开 API transport 的重定向实现。

首期跨源 API 明确拒绝。未来独立 H5 接入另行增加精确 origin＋路径登记、凭据模式、CORS/CSRF 和可读响应头契约，不在本期默认放开。浏览器网络失败统一安全提示，不能从 TypeError 推断是 CORS 还是重定向。请求查询参数遵循端点 DTO 的显式命名，默认沿用本仓 PascalCase；响应错误 JSON 的 lowerCamelCase 不改变查询契约。

增加客户目录及示例的静态导入门禁，禁止直接或经管理请求封装导入 Umi request、requestErrorConfig；沿用现有工具，不引入第二套 lint 平台。接入指南分开说明：管理 skip 仍原样抛出旧错误，客户 skip 只关闭展示，不得顺手统一两者。

### 7.2 归一化模型

```ts
type CustomerErrorKind =
  | 'business' | 'validation' | 'authentication' | 'forbidden'
  | 'notFound' | 'rateLimited' | 'conflict' | 'server'
  | 'network' | 'timeout' | 'cancelled' | 'outcomeUnknown';

type CustomerError = Error & {
  code?: string;
  kind: CustomerErrorKind;
  httpStatus?: number;
  fieldErrors: Record<string, string[]>;
  correlationId?: string;
  retryAfterSeconds?: number;
};
```

不得附回原始 `response`、`config`、`cause`、`raw`、内部堆栈、令牌或原始异常对象。安全错误自身的 message 来自批准服务端响应或本地安全提示。

固定顺序：`transport → 校验/解析 → normalize → 会话动作 → 展示责任 → reject CustomerError`。

- 展示选项为 `display = global | page | form | none`。`skipErrorHandler: true` 仅兼容映射为 `none`，不能绕过归一化，也不能把原异常直接抛给页面。
- 客户接口的非 2xx、2xx 内 ABP 错误信封、任务/批量失败结果都要准确识别；失败不能被 catch 转成成功空对象。
- 精确匹配快照中的公开码及字段结构，才接纳安全 message。未知新码、非法字段、HTML、非标准网关错误、原生异常一律本地兜底。
- 分类顺序为：合法公开码对应 PublicKind → 已登记协议的安全机器字段 → HTTP 状态安全兜底 → 无响应时的取消/超时/网络分类。403 的公开业务码仍是 business，不能覆盖成 forbidden；空体401仍触发 authentication，403/404/429/5xx仍能使用本地安全分类。状态兜底不批准响应原文。ABP401与 session_revoked401均进入 authentication，并共享同会话单次恢复；OAuth原文描述不参与业务推断。
- 认证动作另有强约束：受控请求HTTP401触发同会话单次恢复，HTTP403永不触发清理/恢复；模块PublicKind不能改变此规则。首期HTTP401公开码只允许Client:AuthenticationRequired或无公开码，任何其他码（不仅business）均丢弃码/message，用本地authentication兜底；403上的authentication同样丢弃并用本地forbidden兜底，合法business/403继续保留。其他违反已登记出口(code,kind,status)组合的响应也丢弃码/message并按安全状态兜底。HTTP200任务条目按结果项契约校验，其authentication/server等失败不清除当前会话。服务端基础码校验和端点测试同步拒绝不一致组合。
- 前端只使用 code/kind 分支，不对 message 做错误码或业务含义正则。
- 内容纯文本显示；不渲染错误中的 HTML/Markdown，不把 URL/字段名原文拼入兜底。
- 字段提示由表单接管后不再全局提示；每次操作有明确展示属主，不仅按相同文案全局去重。

### 7.3 身份、重试与文件行为

- 客户 auth adapter 提供发送时会话快照、当前会话比较及恢复入口；现有 `oidc.ts` 没有可直接假设存在的“会话代数”API。
- 快照内部包含客户身份、租户、会话代数与实际发送 token 的引用；token 只在内存中比较，不进入错误对象、日志或关联编号。
- 同一会话并发 401 仅触发一次恢复；执行清理/跳转前再次校验快照。旧 token/旧会话迟到的 401 不清除新会话。
- 403 不退出。客户 404 默认反馈；只有明确标注预期缺失的读取才降级。管理端 404 现有行为不变。
- 主动 Abort 静默，保留取消失败语义；真实超时另行分类。
- 写请求的网络中断/超时/结果不明确时显示“结果暂未确认，请查看记录”，不自动重放 POST。登录恢复和 503 也不自动重放；框架不承诺业务幂等。
- API 重定向被 transport 拒绝后只返回安全失败，不显示原始异常或 Location；写请求按结果未知处理，不能再次发送。读取请求使用网络/请求通用兜底，不要求从 fetch 的 TypeError 猜出重定向原因。
- Blob错误按下载端点声明的成功媒体类型判断：非2xx安全JSON，或二进制端点意外返回的JSON（即使200），都先有界检查错误信封；有效错误按失败归一，不能下载成文件。声明允许JSON文件的端点按其文件契约处理，不仅因看见JSON就拒绝；204和正常二进制不解析。直接window.open不经过请求库，仍由服务端独立保证失败响应安全。
- 上传适配只把归一后的错误交给组件；覆盖 413/415、取消及组件/global 双提示。

### 7.4 公开机器契约快照

后端生效的公开目录是唯一事实源。新增Host.Tests的PublicErrorContractSnapshotTests，在不拨号目录组合中序列化公开码、分类、字段及公开HTTP组合，与提交JSON比较。catalog的schemaVersion只供构建期结构校验与诊断，代码需识别该schema；不与服务端响应协商版本。模块只锁自身定义，宿主组合是共享前端catalog唯一写入者。

采用显式环境变量 `UPDATE_PUBLIC_ERROR_CONTRACT=1` 更新快照，默认模式只校验、不写文件。新增或调整公开码时，后端声明与快照同提交。业务模块的公开定义参加其模块测试；宿主组合测试检查当前装配目录与前端快照的一致性。

这与现有 `FrontendContractSnapshotTests` 的 DTO 形状快照是两条门禁：框架 `ClientFailureDto` 加入原 SeedTypes，模板客户 DTO 由模块自身形状测试/快照或宿主组合测试覆盖，不让框架 Application.Tests 反向引用 Biz.Template。禁止把公开目录并入 `frontend-contract-shapes.json`，或把前端本地 CustomerError 当成后端 DTO。

路径从AppContext.BaseDirectory逐层向上找候选根，必须同时命中backend/AbpAdmin.slnx与web/package.json；无候选或多个完整候选均失败并打印候选路径，不猜最近仓库、不依赖CWD。文件缺失/损坏时测试和构建失败，不静默生成。HostUnderTest现有单锚点遍历可参考，但双锚点是本期新增。公开码采用增量兼容：旧前端遇新码安全兜底，新前端兼容旧码；不要求原子同步上线或响应带version。删除/改义旧码属于破坏性契约，必须另版本化。

这是错误机器契约导出，不是恢复 OpenAPI/API 服务生成。页面和 `example.ts` 的接口函数、DTO 仍手写；客户多语言文案不复制到前端。

## 8. Biz.Template 最小示例

新增 `Client/Controllers/BizProjectClientController.cs`、`Client/Dtos/`、`ClientErrors/BizTemplatePublicErrorDefinitions.cs`、`Localization/BizTemplateClient/`，修改 `BizTemplateModule.cs` 和 csproj 嵌入规则。

示例以 `/api/client/biz-template/...` 演示独立入口和 DTO；公开码使用 `BizTemplate:Public:ProjectUnavailable`。对成功、公开业务失败、校验、未知异常和任务失败分别设置测试夹具。

必须注意现有 `BizProject` 没有 `IMultiTenant`，管理列表也不能直接作为客户列表。首期不借错误处理任务改变其实体模型：

- 增加 `BizTemplate:EnableClientErrorSample` 部署开关，默认 `false`；仅 Development/测试宿主允许启用，其他环境启动校验拒绝启用。
- 示例端点不匿名、不自动授予客户角色权限，使用明确的样例访问权限；真实项目数据只在测试工厂构造的隔离库中演示。生产禁用时端点不注册，前端示例也不进入生产路由。
- 若示例使用现有项目记录，限定当前用户自己的记录；不存在、非本人或停用统一使用同一公开失败，不把原管理 AppService 直接透传成客户 API。
- 从样板中提取最小内部读取/状态检查服务供两个入口调用；权限在各入口明确执行，数据范围显式传递且不得关闭全局过滤。不要为复用关闭管理权限代理。
- 系统异常、上传/下载失败、写超时等故障注入端点仅放测试程序集，不发布“传 scenario 任意触发内部异常”的生产 API。
- 模块说明注明该示例用于错误协议接入，不能作为多租户客户业务的数据授权模板直接上线。正式客户模块需自行建立其租户和数据授权契约。
- 复制样板时同步 `.cursor/rules/framework/common/biz-module.mdc` 改名清单：`BizTemplateClient`、`<模块>:Public:`、`EnableClientErrorSample`、客户 `Client/` 与 `ClientErrors/`。漏改公开码前缀会与样板冲突并启动失败；A18 以“复制定义未改名前缀必须校验失败”为门禁。

## 9. 开发任务、依赖和完成条件

P0—P4 已完成。公开码快照、HTTP 管线、资源隔离、客户请求库与 CI Production 进程均已落地。

### P0：基线、端点登记和契约

- [x] 记录工作区状态、ABP 版本、已有管理端错误测试基线；保留用户改动。
- [x] 在实际实施的首个文档提交中纳入本方案、两轮审核处置及其被引用报告；生成标准仓库渲染所需的相对链接交付版并验证。当前本地文件未提交不算已完成此项；本轮仅改文档不执行Git提交。
- [x] 建立 `AdminErrorEndpointRegistry` 和端点分类测试，覆盖路由元数据与 AppService 权限的差异。
- [x] 完成宿主端点与非MVC写出清单：每项有ExitId、方法/签名/路由、权限、协议策略、当前状态/头/体、实际Data固定键与开放来源、批准字段集合、消费者、兼容变化和验收ID；不按Controllers目录名统计。补SourceId到ExitId关联和写出候选机器漂移门禁。
- [x] 盘点所有 UserFriendlyException 构造点、Data/插值来源、管理语言消费者和匿名登录必需资源；形成版本化清单，不把报告中的数量当固定门禁。
- [x] 固定第 3 节公开码/字段命名、支持语言、管理兼容数据白名单；补基线失败夹具。
- [x] 将后端图片繁忙503/Retry-After、管理503通用提示、限流429和管理404静默加入兼容用例；不把503倒计时说成既有管理行为。

完成条件：没有把“已登录”或权限前缀当管理资格；每个待接出口有明确适配负责人和回归用例；登记正反向漂移检查通过后才能合并全局替换。运行时未登记仍默认公开，不提供开发环境原文 dry-run。

### P1：目录、投影和 HTTP 出口

- [x] 首先建立第11节独立HTTP测试工程的Development真实TestServer基线，复用HostUnderTest与实际模块，证明MVC/隐式ABP中间件/外层兜底/授权路径；P1不要求先建通用跨进程runner，非Development放P4且发布前必过。
- [x] 实现公开定义 Provider、只读目录、启动校验和非 HTTP 投影核心。
- [x] 实现静态基础错误资源、严格配置绑定和入口关联号归一；补 csproj 嵌入目录。
- [x] 实现管理资格解析、MVC filter 与隐式 ABP异常中间件替换、双授权适配和 API兜底；原服务键只解析到一个目标实现。
- [x] 将第 5.3 节全部现有出口接入统一 writer/协议适配器；保留 OAuth、状态码和头。
- [x] 实现 `ClientFailureDto`，用 HTTP 200 的任务/批量结果夹具验证。
- [x] 加入“客户响应过滤，但原始日志/审计仍可定位”成对测试。
- [x] 加入安全降级原因计数及既有审计检索接入，禁止敏感值/高基数请求数据作指标标签。

完成条件：任一受控 API 出口都不能依赖前端隐藏原文；投影不修改原始诊断对象；故障时不会再抛格式化异常。

### P2：语言资源完整隔离

- [x] 跳过客户静态资源的数据库贡献者；阻止动态写覆盖，保留历史覆盖清理。
- [x] 实现通用配置与独立本地化服务的公开投影；覆盖两类脚本及具体类注入。
- [x] 验证资源继承、动态资源、缓存及 `includeLocalizationResources=true` 等旁路。
- [x] 为确有依赖的管理消费者接专用资源入口；公共接口不按管理员身份放宽。
- [x] 回归登录/注册/租户选择所需文案和 Razor 脚本加载。

完成条件：公开 JSON、脚本、前端构建产物均无内部错误目录；管理功能和匿名登录无缺字/缺资源回归。

### P3：客户请求库与模板示例

- [x] 导出公开机器契约快照，增加前后端一致性测试。
- [x] 补框架/模块 DTO 形状门禁、客户导入边界检查及快照版本错位兜底。
- [x] 实现纯 TS normalize、独立 fetch adapter、强制处理顺序与展示责任。
- [x] 实现注入式客户会话协调；不接管现有管理 OIDC；用假 adapter 验证并发与迟到响应。
- [x] 实现取消、读超时、写结果未知、429 等待、文件错误解析。
- [x] 实现第 8 节模板入口、DTO、内部业务复用与开发视图；默认关闭且不进入生产菜单/路由。
- [x] 原 `requestErrorConfig.test.ts` 继续通过，客户调用不触发管理全局提示或跳管理登录。

完成条件：同一次失败只由一个位置展示；skip 不绕过归一；客户代码不依赖 UI 框架解析错误；写请求不自动重复发送。

### P4：集成、文档、发布与回退

- [x] 在P1同一真实夹具上完成全部出口、管理兼容与非Development回归；用独立dotnet test进程/CI矩阵隔离环境，不另建模拟管线；未跑的环境分支记未验收。
- [x] 完成第 10 节矩阵，关键安全/兼容用例不允许因环境缺失而跳过后算通过。
- [x] 执行相关测试和构建；最终执行第11节现有CI检查，并补前端生产构建/路由产物检查，证明示例确实不在生产包中。
- [x] 更新 `docs/framework-contracts.md`、`web/AGENTS.md` 对客户错误路径的约定，并新增模块接入指南；保持管理 404 说明准确。同步 `.cursor/rules/framework/common/biz-module.mdc` 改名清单、`sectest/` 公开码判定（内部码不得出现在客户响应）、以及 `AnonymousEndpointSweepTests` 对 LocalizationScript 公开投影的论证。
- [x] 记录公开码版本规则、部署配置、样例开关和回退方式。

完成条件：全部必测项有结果，公开码与文案快照一致，文档/示例可独立交接。

### 并行与提交边界

P0 先完成；P1 的契约冻结后，HTTP 适配、P2 资源隔离和 P3 前端纯函数可并行。全局替换合并及P2/P3集成验收均依赖P1真实管线基线通过；模板联调依赖P1/P2，P4依赖全部完成。公共契约文件指定单一维护者，避免多人同时修改。

建议按以下可审阅提交组织：①契约与目录；②HTTP 适配与管理兼容；③资源隔离；④客户请求库与快照；⑤模板示例与端到端验收。各提交保留相应测试，不提交临时诊断、环境凭证或用户原有改动。

## 10. 验收矩阵

测试使用合成标记 `INTERNAL_ONLY_MARKER`，不得使用真实凭证或生产异常。检查完整原始 HTTP body、headers、脚本及 UI，而非只看 toast 文案。

P0每个ExitId关联适用用例；按“出口×适用错误类型”生成数据用例，不适用项写理由，再覆盖资源语言完整性。OAuth/SSE/文件/204并不适用相同错误集合，不机械强套组合。公共断言器以结构白名单及合成敏感标记为主，SQL/路径正则为辅助，不把扫描无命中当作安全证明。

| 编号 | 场景 | 必须满足 |
| --- | --- | --- |
| A01 | 已登记业务异常 | 公开码及当前语言文案正确；内部码、原文与未批准参数不在客户响应。 |
| A02 | 未知码/无码友好异常/SQL/远程嵌套异常 | 安全兜底；没有原始 details、Data、路径、SQL、实体类型/ID。 |
| A03 | 参数与校验 | 非法类型、reason=内部原文、未知字段、输入原值不能进入公开消息；错误项超限安全兜底。 |
| A04 | 伪造端身份 | 请求头/clientType/client_id/Origin 无法升级；匿名或仅登录的管理请求失败安全。 |
| A05 | 管理授权 | 原所有权限条件仍执行；服务代理权限不遗漏；资格异常和租户未就绪时降级公开。 |
| A06 | Shared | 同一公共/共享接口由客户或管理员调用，公开错误与资源边界相同。 |
| A07 | 语言出口 | JSON、两类脚本、资源继承/动态覆盖/缓存均不可取得内部模板；缺默认语言启动失败。 |
| A08 | 日志/审计 | 客户响应安全，日志/审计保留内部错误分类及关联信息；原对象未被投影修改，秘密仍按规则脱敏。 |
| A09 | 状态与协议 | 401/403/429、OAuth、WWW-Authenticate、Retry-After 正确；HTTP 不被统一改成 200。 |
| A10 | 传输旁路 | void、FileResult、非JSON Accept、直开下载、Blob JSON/HTML、413/415均安全；二进制端点200错误信封识别为失败，合法JSON文件/正常二进制/204不误伤；同源未登记入口及跨源重定向不自动跟随。 |
| A11 | 已开始响应 | 不写第二份 JSON；不把截断文件当成功；日志有定位信息。 |
| A12 | 成功响应内失败 | 任务/批量 HTTP 200 中只含公开失败 DTO，无内部 ErrorMessage。 |
| A13 | 前端 skip/原文 | skip 后仍 reject 安全错误；未知码、HTML、原生网络异常不可直接上屏；错误对象不附带 raw/config/token。 |
| A14 | 提示属主 | 表单已显示字段错误时不再全局弹；独立操作不会因同文案错误去重而无提示。 |
| A15 | 并发身份/协议组合 | 同会话5个401只恢复一次；旧会话迟到401不清新会话；参数化全部非认证码+401均丢弃，authentication+403丢弃，business+403保留；其他越界组合安全状态兜底；200任务失败项不触发登录恢复。 |
| A16 | 取消/写超时 | Abort静默；写操作结果未知，超时/断网/503/307或308重定向失败/登录恢复后均不自动再次发送。 |
| A17 | 管理兼容 | 原用例继续通过；404静默、403提示、429等待、503通用提示保持；每个管理出口error.data键集合⊆批准集合，未知ExtraProperties/同名污染值不进入message/data，批准字段大小写和结构兼容。 |
| A18 | 构建与模块 | 新资源存在于发布程序集；模板可复制/移除；框架不反向依赖业务实现；生产无法启用演示故障入口。 |
| A19 | 契约漂移 | 公开码、分类、公开字段变化使快照校验失败；快照无内部码与文案。 |
| A20 | 登录与资源 | 匿名登录、注册、租户选择及必要Razor脚本正常；管理资源消费者通过受权路径取得所需资源。 |
| A21 | 环境/详细开关 | MVC、UOW完成期、/connect及/v1尚未开始SSE的未捕获异常均按协议安全兜底，无错误页HTML/堆栈；ABP details/stack开关true/false组合下均成立；非Development另进程必测，原审计诊断仍可保留。 |
| A22 | 关联号入口 | 缺失/非法/合法伪造值均生成新canonical；同一传入值两请求主号不同；合法传入只是不可信父线索。body/启用的header/日志/审计一致，Clear及下游冲突头不改变主值；替换服务唯一、回调单写，关联号不绕过查询权限。 |
| A23 | 配置拒绝 | ClientErrorHandling未知键、宽松键、非法范围/文化启动失败；不扩成整个宿主所有未知键都失败。 |
| A24 | 导出/匿名下载 | 两类导出BadRequest字符串与空体Forbid/NotFound分支、window.open、FileShare显式404及GDPR异常均安全；生产者helper不误等授权异常handler；已开始文件不二次写，隐藏资源语义保持。 |
| A25 | 上传独立出口 | fileName/reason不能未经批准回显；400/413/415保留正确状态和组件单次提示。 |
| A26 | 友好异常与插值 | 带码/无码、预插值ex.Message、完整Data均按清单覆盖；管理与客户都先选批准参数；不以有code或UserFriendly类型批准原文。 |
| A27 | 装配/写出漂移 | 新增/删除/别名可检测，含同名不同参数重载正例及缺签名失败；写出候选新增/变化/过期清单使CI失败，原文SourceId关联到ExitId；SettingUi代理不遗漏；filter/UOW/关联号原服务键唯一且解析到新实现。 |
| A28 | OAuth与空体认证 | grant无码BusinessException不输出ex.Message；ABP401、session_revoked401、cookie空体401/403正确归一；不删WWW-Authenticate，不二次写。 |
| A29 | 无码任务失败 | 客户纯字符串失败映射OperationFailed；不暴露字符串、不伪造任务关联号；现有受权管理诊断DTO不被全局重写。 |
| A30 | 独立协议 | 网关501、捕获失败、未捕获失败、已开始SSE及HTTP握手分别测；保留安全协议体/固定错误帧与既有302导航，不套ABP、不出详细错误页。故障头仅测固定错误且非Dev忽略；未捕获/已开始注入在测试程序集。 |
| A31 | 等待时间/kind | 合法header优先、批准body兜底；冲突/非法值/0/日期/超限有测试；ImageProcessingBusy由目录映射Unexpected/server+503且可带等待值，错误注册失败；Clear后头恢复，管理429旧字段及503头保留。 |
| A32 | 快照与依赖 | 两类快照独立；默认不写、缺失/损坏失败、不同CWD路径一致；旧前端遇新码兜底；模块无反向依赖；客户导入约束生效。 |
| A33 | URL与凭据 | 同源凭据模式固定；userinfo/歧义路径/未登记入口/跨源均拒绝；合法编码查询不误拦；不转发身份头、不自动跟随跳转。 |
| A34 | 资源回退与发布 | 当前文化缺键走批准回退，整个链缺失安全兜底且message不回显资源键/内部码；实际创建localizer后无数据库贡献者，包括GlobalContributors负向用例；CI web job实际构建并检查路由/产物无示例。 |

## 11. 测试落点与执行命令

### 11.1 新增/扩充测试位置

- `backend/test/AbpAdmin.Domain.Tests/ClientErrors/`：定义、参数、文案、纯投影和配置边界。
- `backend/test/AbpAdmin.Application.Tests/ClientErrors/`：客户失败 DTO、语言写入规则；已有 `Localization/LanguageTextAppServiceTests.cs` 同步扩充。
- `backend/test/AbpAdmin.HttpApi.Host.Tests/ClientErrors/`：端点/写出候选登记、管理资格、目录快照及组合装配；`Snapshots/error-response-producers.json`保存已审候选，不把行号作稳定主键。
- `backend/test/AbpAdmin.ClientError.HttpPipeline.Tests/`：新增专用真实HTTP测试工程，仅包含该机制的HTTP夹具/测试，在solution内使用独立测试进程；与原HostTestBed工程隔离。通过编译链接同一HostUnderTest源码或提取共享测试helper复用实际builder，不复制启动逻辑。
- `backend/test/AbpAdmin.HttpApi.Host.Tests/ApplicationConfiguration/`：公共资源 JSON/脚本、匿名资源兼容；保留并调整 `AnonymousSettingTrimTests` 的正确预期。
- `backend/test/AbpAdmin.EntityFrameworkCore.Tests/EntityFrameworkCore/Applications/EfCoreLanguageTextAppServiceTests.cs`：真实持久化覆盖与恢复默认。
- `backend/test/modules/biz/AbpAdmin.Biz.Template.Tests/ClientErrors/`：模块声明、客户 DTO、样例授权及故障夹具。
- `web/src/abp/customer/*.test.ts`、示例页测试：纯函数、传输、会话、取消、显示属主。纯函数测试文件头使用 `// @vitest-environment node` 确认没有隐式 UI 依赖，不改现有全局 happy-dom 配置。

现有HostTestBed只初始化宿主/端点表，不启动HTTP；直调或元数据不能作为网络验收。P1专用HTTP测试工程从同一HostUnderTest.CreateBuilderAsync创建新builder，在Build前接TestServer、隔离容器及替身，走真实InitializeApplicationAsync和HTTP启动。不能复用已Build的ServiceCollection或抄一套影子管线；外部网络/后台工作按现有接缝隔离，数据库使用已迁移独立测试库。装配检查与实际响应/顺序探针互补，MvcOptions不能证明中间件顺序。

生命周期直接使用dotnet test进程隔离，不开发通用HostProcessFixture/父子IPC框架。专用工程每进程只初始化一个共享宿主，不与原HostTestBed同进程混跑、不逐用例Dispose再建；理由见HostTestBed.cs:10–15的Quartz静态LoggerFactory及Host静态MaskingPermissionGate。测试专用环境变量CLIENT_ERROR_TEST_ENVIRONMENT只允许Development/Production，缺省Development；CI另起一次Production测试进程。它仅控制测试builder环境，不是生产开关。P1先完成Development核心基线；P4必须跑非Development全部适用项，不能记未跑分支为通过。

HostUnderTest当前固定Development，需要受控测试环境参数及测试专用证书/配置接缝，仍复用真实装配；不读取本机生产证书/secrets。样例生产禁用有配置单测及非Development装配验证。TestHost/语法树解析等新增NuGet依赖进入集中版本与对应测试csproj，不进入生产依赖。check-docker-restore.ps1只检查Host/DbMigrator的ProjectReference闭包对应Dockerfile COPY，不是NuGet清单；只有生产项目引用闭包变化才同步Dockerfile，不能因新增测试包修改该脚本。

### 11.2 环境准备

- .NET 10 SDK、仓库所需 Node（当前要求 >=22）、pnpm（以 packageManager/锁文件为准）。
- 后端数据库集成测试使用已有 Docker/Testcontainers PostgreSQL。关键用例若因 Docker 不可用被 Skip，记为未验收，不能算通过。
- 测试前检查没有继承指向开发/生产库的测试连接覆盖；使用隔离测试容器，不把本机 secrets 复制到夹具。
- 以下命令用于未来实施验收；本文编写不代表已经执行这些新增测试。

在仓库根执行后端相关检查：

```powershell
Set-Location -LiteralPath 'E:\code\abp_admin'
dotnet restore backend/AbpAdmin.slnx
dotnet build backend/AbpAdmin.slnx
dotnet test backend/test/AbpAdmin.Domain.Tests/AbpAdmin.Domain.Tests.csproj
dotnet test backend/test/AbpAdmin.Application.Tests/AbpAdmin.Application.Tests.csproj
dotnet test backend/test/AbpAdmin.HttpApi.Host.Tests/AbpAdmin.HttpApi.Host.Tests.csproj
dotnet test backend/test/AbpAdmin.ClientError.HttpPipeline.Tests/AbpAdmin.ClientError.HttpPipeline.Tests.csproj
dotnet test backend/test/modules/biz/AbpAdmin.Biz.Template.Tests/AbpAdmin.Biz.Template.Tests.csproj
dotnet test backend/test/AbpAdmin.EntityFrameworkCore.Tests/AbpAdmin.EntityFrameworkCore.Tests.csproj --filter FullyQualifiedName~EfCoreLanguageTextAppServiceTests
```

P4发布前及CI在另一测试进程跑非Development分支（不得复用前一进程宿主）：

```powershell
Set-Location -LiteralPath 'E:\code\abp_admin'
$previousClientErrorTestEnvironment = $env:CLIENT_ERROR_TEST_ENVIRONMENT
$env:CLIENT_ERROR_TEST_ENVIRONMENT = 'Production'
try {
    dotnet test backend/test/AbpAdmin.ClientError.HttpPipeline.Tests/AbpAdmin.ClientError.HttpPipeline.Tests.csproj
    if ($LASTEXITCODE -ne 0) { throw 'Non-Development client error tests failed' }
} finally {
    $env:CLIENT_ERROR_TEST_ENVIRONMENT = $previousClientErrorTestEnvironment
}
```

实施新增快照测试后，仅在有意更新契约时运行：

```powershell
Set-Location -LiteralPath 'E:\code\abp_admin'
$env:UPDATE_PUBLIC_ERROR_CONTRACT = '1'
try {
    dotnet test backend/test/AbpAdmin.HttpApi.Host.Tests/AbpAdmin.HttpApi.Host.Tests.csproj --filter FullyQualifiedName~PublicErrorContractSnapshotTests
} finally {
    Remove-Item Env:\UPDATE_PUBLIC_ERROR_CONTRACT -ErrorAction SilentlyContinue
}
```

框架客户 DTO 形状有意变更时，另行刷新既有形状快照；模块 DTO 按模块测试自己的刷新命令执行：

```powershell
Set-Location -LiteralPath 'E:\code\abp_admin'
$env:FRONTEND_CONTRACT_SNAPSHOT_UPDATE = '1'
try {
    dotnet test backend/test/AbpAdmin.Application.Tests/AbpAdmin.Application.Tests.csproj --filter FullyQualifiedName~FrontendContractSnapshot
} finally {
    Remove-Item Env:\FRONTEND_CONTRACT_SNAPSHOT_UPDATE -ErrorAction SilentlyContinue
}
```

前端检查：

```powershell
Set-Location -LiteralPath '../web'
pnpm exec vitest run src/requestErrorConfig.test.ts src/abp/customer
pnpm exec vitest run src/pages/framework/customer-errors-demo
pnpm run lint
pnpm run build
```

`lint` 已包含 Biome lint 与 `tsc --noEmit`，无需本地重复跑相同类型检查。当前CI只有前端tsc/test，没有lint/build；本任务必须补生产构建及示例路由/产物缺席检查，不能声称现有CI已经证明样例不发布。产物检查覆盖route manifest、页面模块和故障示例标识，hideInMenu不足以通过。

最终合并前完整检查以.github/workflows/ci.yml为准：restore闭包、solution/工具restore、七套EF漂移、后端全量测试、前端冻结安装/tsc/test、双镜像构建；本期另加专用HTTP工程Production进程及web生产build/样例产物检查。P1只跑相应开发基线，完整CI为P4交付门禁，可由现有CI执行一次并保存证据，不要求每阶段反复全量跑。以下命令补足局部测试；模型检查仅占位连接串设计建模，不连真实库：

```powershell
Set-Location -LiteralPath 'E:\code\abp_admin'
pwsh scripts/check-docker-restore.ps1
dotnet restore backend/AbpAdmin.slnx
dotnet tool restore
$previousDriftConnection = $env:ConnectionStrings__Default
$env:ConnectionStrings__Default = 'Host=localhost;Database=ef_model_drift_check'
try {
    $driftChecks = @(
        @('backend/framework-app/AbpAdmin.EntityFrameworkCore', 'AbpAdminDbContext'),
        @('backend/modules/biz/AbpAdmin.Biz.Template', 'BizTemplateDbContext'),
        @('backend/modules/platform/AbpAdmin.Webhooks', 'WebhooksDbContext'),
        @('backend/modules/platform/AbpAdmin.Collection', 'CollectionDbContext'),
        @('backend/modules/platform/AbpAdmin.Ordering', 'OrderingDbContext'),
        @('backend/modules/platform/AbpAdmin.Ledger', 'LedgerDbContext'),
        @('backend/modules/platform/AbpAdmin.TokenGateway', 'TokenGatewayDbContext')
    )
    foreach ($driftCheck in $driftChecks) {
        dotnet ef migrations has-pending-model-changes -p $driftCheck[0] -s backend/framework-app/AbpAdmin.DbMigrator -c $driftCheck[1]
        if ($LASTEXITCODE -ne 0) { throw "Model drift check failed: $($driftCheck[1])" }
    }
} finally {
    $env:ConnectionStrings__Default = $previousDriftConnection
}
dotnet test backend/AbpAdmin.slnx --nologo
pwsh scripts/docker-build-check.ps1
Set-Location -LiteralPath '../web'
pnpm install --frozen-lockfile
pnpm tsc
pnpm test
```

上述为逐项人工执行命令，任一非零退出立即停止验收并记录；既有失败与本次引入失败分别归因，不能借此跳过新增必测项。

### 11.3 完成记录模板

每个阶段记录：提交号、变更文件、测试命令、通过/失败/跳过数量、关键响应样本（仅合成数据）、兼容变化与剩余限制。禁止把“源码看起来接好了”写成“网络响应已验证”。

## 12. 上线、回退与完成定义

### 12.1 上线顺序

1. 先完成管理登记、基础协议、全部服务端出口和资源过滤，不先开放客户端点。
2. 在隔离测试环境跑 P4，并验证客户模式不受 Development 详细异常配置影响。
3. 客户示例只在开发/测试启用；实际业务模块必须完成自身公开码及数据授权审查后，才能单独接入生产客户入口。
4. 反向代理/网关错误页另行核查；前端可兜底展示，但应用投影不能阻止网关自身返回内部内容。客户 SignalR/流式协议新增时单独接入，不能假定 REST filter 自动覆盖。

### 12.2 回退

仅样例/客户业务入口自身故障，可以关闭该入口。涉及全局filter、隐式异常中间件、认证适配或共享资源过滤的管理兼容故障，关闭样例开关无效，必须回退整套已验证版本；不要开启原始错误透传或“开发dry-run”应急。回退时校验前后端目录与资源的兼容版本组合，保留旧前端未知新码兜底，不假设可以原子同步部署。日志和关联号保留，方便定位回退原因。

### 12.3 Definition of Done

- [x] D1—D6 全部落实，P0—P4 完成，A01—A34 有实际验收结果，并能追溯至出口清单。
- [x] 客户异常、资源与成功 DTO 失败字段均无未批准信息；前端不是唯一防线。
- [x] 管理端兼容测试通过；匿名/无管理资格的信息收紧明确记录。
- [x] 原始诊断仍可关联检索，投影未污染审计和异常通知。
- [x] 新模块可按文档注册公开错误，不必修改框架业务代码。
- [x] 支持语言完整、资源已嵌入发布包、机器契约快照一致。
- [x] 示例默认关闭且生产不可启用；未引入数据库表、无关业务改造或在线配置后台。
- [x] 文档与实际实现一致，所有关键测试通过且无环境导致的未说明跳过。
- [x] 方案、两轮处置及引用报告进入实施文档提交，标准仓库交付版的相对链接经克隆/CI checkout验证可用；不能以本机存在文件替代版本化交接。

## 13. 事实依据与当前验证范围

本地关键入口：

- exchange H5 映射（参考仓库 exchange/exchange `Web-H5/src/lib/tradeErrorMessage.ts:166`）、请求归一（参考仓库 exchange/exchange `Web-H5/src/api/request.ts:101`）、后端原文包装（参考仓库 exchange/exchange `Admin.NET/Admin.NET.Core/Utils/AdminResultProvider.cs:128`）。
- [abp_admin 管理错误优先级](../web/src/requestErrorConfig.ts:94)、[业务异常兜底及Data写出](../backend/framework-app/AbpAdmin.HttpApi.Host/FriendlyBusinessExceptionMiddleware.cs:84)。
- [匿名配置裁剪](../backend/framework-app/AbpAdmin.HttpApi/Controllers/AnonymousSettingTrimApplicationConfigurationContributor.cs:21)、[登录前 API/脚本豁免](../backend/framework-app/AbpAdmin.HttpApi.Host/Authorization/PreLoginApiEndpointsConvention.cs:23)。
- [语言资源贡献者接线](../backend/framework-app/AbpAdmin.Domain/AbpAdminDomainModule.cs:169)、[语言文本服务](../backend/framework-app/AbpAdmin.Application/Localization/LanguageTextAppService.cs)。
- [模板实体](../backend/modules/biz/AbpAdmin.Biz.Template/Entities/BizProject.cs)、[模板管理服务](../backend/modules/biz/AbpAdmin.Biz.Template/Services/BizProjectAppService.cs)。
- [UOW装配](../backend/framework-app/AbpAdmin.HttpApi.Host/AbpAdminHttpApiHostModule.cs:1516)、[真实测试builder](../backend/test/AbpAdmin.HttpApi.Host.Tests/HostUnderTest.cs:69)、[现有CI](../.github/workflows/ci.yml:29)。
- [管理404约束](./framework-contracts.md:70)、[受权管理诊断取舍](./framework-contracts.md:76)、[现有审计关联号入口](../web/src/pages/audit-logs/index.tsx:59)。

固定版本外部依据：

- [ABP 10.7.0 MVC 异常 filter](https://raw.githubusercontent.com/abpframework/abp/10.7.0/framework/src/Volo.Abp.AspNetCore.Mvc/Volo/Abp/AspNetCore/Mvc/ExceptionHandling/AbpExceptionFilter.cs)：保留日志、通知及状态流程，在 HTTP 输出处投影。
- [ABP 10.7.0 异常中间件](https://raw.githubusercontent.com/abpframework/abp/10.7.0/framework/src/Volo.Abp.AspNetCore/Volo/Abp/AspNetCore/ExceptionHandling/AbpExceptionHandlingMiddleware.cs)：不要假设 private 包装方法可重写，也不要遗漏已开始响应的限制。
- [ABP 10.7.0 审计转换](https://raw.githubusercontent.com/abpframework/abp/10.7.0/modules/audit-logging/src/Volo.Abp.AuditLogging.Domain/Volo/Abp/AuditLogging/AuditLogInfoToAuditLogConverter.cs)：异常 converter 被审计共用。
- [ABP 10.7.0 独立本地化服务](https://raw.githubusercontent.com/abpframework/abp/10.7.0/framework/src/Volo.Abp.AspNetCore.Mvc/Volo/Abp/AspNetCore/Mvc/ApplicationConfigurations/AbpApplicationLocalizationAppService.cs)：配置贡献者不能覆盖全部资源出口。
- [ABP 10.7.0管线扩展](https://raw.githubusercontent.com/abpframework/abp/10.7.0/framework/src/Volo.Abp.AspNetCore/Microsoft/AspNetCore/Builder/AbpApplicationBuilderExtensions.cs)：UseUnitOfWork自动安装异常中间件。
- [ABP 10.7.0异常选项](https://raw.githubusercontent.com/abpframework/abp/10.7.0/framework/src/Volo.Abp.ExceptionHandling/Volo/Abp/AspNetCore/ExceptionHandling/AbpExceptionHandlingOptions.cs)与[转换器](https://raw.githubusercontent.com/abpframework/abp/10.7.0/framework/src/Volo.Abp.ExceptionHandling/Volo/Abp/AspNetCore/ExceptionHandling/DefaultExceptionToErrorInfoConverter.cs)：IBusinessException默认Data输出及文案插值。
- [ABP 10.7.0关联号中间件](https://raw.githubusercontent.com/abpframework/abp/10.7.0/framework/src/Volo.Abp.AspNetCore/Volo/Abp/AspNetCore/Tracing/AbpCorrelationIdMiddleware.cs)、[.NET10严格配置绑定](https://raw.githubusercontent.com/dotnet/runtime/v10.0.0/src/libraries/Microsoft.Extensions.Configuration.Binder/src/ConfigurationBinder.cs)、[Retry-After规范](https://www.rfc-editor.org/rfc/rfc9110.html#name-retry-after)：入口稳定值、未知键和等待时间解析的依据。

规划会话于2026-11-05已运行现有针对性测试：exchange两个文件21项，abp_admin管理错误测试22项通过，来源为本会话工具输出，未保存独立历史日志。本轮未重跑，不把这些基线当新机制通过证据。下面给出实施前复核命令，执行时重新记录环境、命令和当次输出，不补造历史日志：

```powershell
Set-Location -LiteralPath 'E:\code\exchange\exchange\Web-H5'
node scripts/vitest-run.mjs run src/lib/tradeErrorMessage.test.ts src/pages/auth/register/registerErrorMap.test.ts
Set-Location -LiteralPath '../web'
pnpm exec vitest run src/requestErrorConfig.test.ts
```

本轮只核对本地源码、固定版本官方源码与文档一致性；未访问生产、未修改业务代码。P0需要的响应样本在隔离测试宿主用合成输入采集，不为补文档连生产或触发真实资金操作。

## 14. 审核处置与版本记录

1.1补入第一轮出口覆盖、严格绑定、资源隔离、快照分层及验收，纠正ABP默认Data、Umi runtime、资源回退和控制器盘点等事实。1.2补齐原协议未捕获异常、关联号可信来源及具体覆写点、401/503目录相容性、管理Data集合断言、写出候选漂移门禁，并将测试基建改为专用工程＋现有dotnet test进程分阶段运行。完整结果见[第一轮处置及勘误](./client-error-handling-development-plan-review-disposition.md)和[第二轮处置](./client-error-handling-development-plan-review-disposition-r2.md)。六项决定不变，原审核报告不改写。

当前交付是本地方案及两轮处置，未提交Git，不声称克隆或CI已能获得这些文件。正式实施P0首个文档提交须纳入方案、两轮处置及被引用报告，并验证标准仓库交付版的相对链接。桌面交付链接保留绝对可点击路径，文件清单相对仓库根；标准仓库版从docs所在目录计算../backend等相对链接，仓外exchange仅作带仓库名/路径的参考，不伪造仓内链接。此项是可移交的质量条件，不是新增审批。本次不提交、不启动实现或部署。旧画布仅为历史摘要，规则以本文1.2为准。
