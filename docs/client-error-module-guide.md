# 业务模块接入公开错误

本文说明业务模块如何声明公开错误、客户入口和静态文案，而不修改框架业务代码。规则以 [client-error-handling-development-plan.md](client-error-handling-development-plan.md) 为准。

## 1. 声明公开定义

在模块内新增 `IPublicErrorDefinitionProvider`（例：`BizTemplatePublicErrorDefinitions`）：

- `InternalCode` 只留服务端。
- `PublicCode` 进入客户响应和 `web/src/abp/customer/public-error-catalog.json`。
- `ResourceType` 必须实现 `IClientStaticResource`，JSON 放模块 `Localization/{Resource}/` 并嵌入 csproj。
- 多状态组合写在 `PublicHttpStatuses`；空则按 kind 默认状态导出快照。
- 允许回传的字段写在 `AllowedParameters`，禁止 `ex.Message` 进 `reason`。

模块移除后，对应定义随 DI 消失。框架工程不得引用业务实现。

## 2. 客户入口

- 独立 Controller / DTO，路径建议 `/api/client/{module}/...`。
- 用 `[ClientErrorAudience(Public)]` 标记；Shared 入口固定公开规则，管理员调用也不放宽。
- 复用内部领域服务，不要把管理 AppService 直接透传。
- 成功响应里的失败字段显式映射 `ClientFailureDto` / `ClientFailureMapper`，不要塞内部 `ErrorMessage`。

## 3. 样例开关

`BizTemplate:EnableClientErrorSample` 默认 false，仅 Development / Testing 可启用。生产启用会启动失败。不要把该开关当成错误安全策略的总开关。

复制 `AbpAdmin.Biz.Template` 时必须改名：`BizTemplateClient` 资源、`<模块>:Public:` 公开码前缀、`EnableClientErrorSample` 开关、`Client/` 与 `ClientErrors/`。漏改会与样板争同一批公开码，宿主启动失败。完整清单见 `.cursor/rules/framework/common/biz-module.mdc`。

## 4. 刷新契约

公开码变更后同提交：

```powershell
$env:UPDATE_PUBLIC_ERROR_CONTRACT = '1'
dotnet test backend/test/AbpAdmin.HttpApi.Host.Tests/AbpAdmin.HttpApi.Host.Tests.csproj --filter FullyQualifiedName~PublicErrorContractSnapshotTests
```

框架 `ClientFailureDto` 形状走既有 `FRONTEND_CONTRACT_SNAPSHOT_UPDATE=1`。模块 DTO 由模块测试覆盖，不要让 `AbpAdmin.Application.Tests` 反向引用业务模块。

## 5. 回退

仅样例入口故障时关闭该入口。全局 filter、异常中间件、资源过滤故障必须回退已验证版本，不要打开原文透传。
