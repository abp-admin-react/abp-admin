# P0 基线记录：客户端错误处理开发方案

> 任务分支：kimi/ceh-p0
> 执行日期：2026-11-05
> ABP 版本：10.7.0
> .NET SDK：10.0.401

## 1. 工作区状态

```
On branch kimi/ceh-p0
nothing to commit, working tree clean
Head: 69bbbe5b refactor(backend): 模块目录对齐参照系——platform/biz 子层分组，消除 framework-app/modules 嵌套
```

## 2. ABP 版本基线

- **ABP 框架版本**：10.7.0（Directory.Packages.props: `<AbpVersion>10.7.0</AbpVersion>`）
- **Microsoft 版本**：10.0.13
- **关键安全修复**：GHSA-4x2h-f4f8-xjr6（Volo.Abp.BlobStoring.FileSystem 路径穿越，Low）已在 10.7.0 修复

### 2.1 ABP 异常处理关键事实（P0 必须接管的入口）

1. **UseUnitOfWork 隐式安装 AbpExceptionHandlingMiddleware**
   - 来源：ABP 10.7.0 `AbpApplicationBuilderExtensions.UseUnitOfWork` 内部调用 `UseAbpExceptionHandling`
   - 本仓证据：`AbpAdminHttpApiHostModule.cs:1516` 调用 `UseUnitOfWork()`
   - 必须适配：替换原服务键为 `ClientErrorAbpExceptionHandlingMiddleware`

2. **SendExceptionDataToClientTypes 默认含 IBusinessException**
   - 来源：ABP 10.7.0 `AbpExceptionHandlingOptions` 构造函数
   - 含义：`BusinessException.Data` 默认会写入响应
   - 本仓影响：限流异常 `AbpAdminOperationRateLimitingException` 继承 `BusinessException`

3. **DefaultExceptionToErrorInfoConverter 用 Data 插值 message**
   - 来源：ABP 10.7.0 `DefaultExceptionToErrorInfoConverter.TryToLocalizeExceptionMessage`
   - 含义：仅过滤响应 `data` 不能撤销已进入 `message` 的敏感值
   - 必须处理：管理端也必须先筛选参数再本地化插值

4. **AbpExceptionFilter.ShouldHandleException 只处理特定条件**
   - 条件：ObjectResult / AJAX (X-Requested-With) / Accept: application/json
   - 放行：void 动作、FileResult、非 JSON Accept
   - 本仓影响：P0 必须接管 void/FileResult 分支

## 3. 前端管理端错误测试基线

```bash
cd E:\code\abp_admin-wt\ceh-p0\web
pnpm install --frozen-lockfile
pnpm exec vitest run src/requestErrorConfig.test.ts
```

**结果**：22 passed (22)，Duration 1.11s

### 3.1 关键测试用例覆盖

| 用例 | 覆盖行为 |
|------|----------|
| 404 全局静默 | `isNotFound` 分流降级，不弹全局错误 |
| validationErrors/details/message 优先级 | 优先级：validationErrors → details → message |
| 403 单提示 | 非权限 403 弹全局提示 |
| skipErrorHandler | 直接抛原错误（管理端语义） |

### 3.2 Umi 版本

- package.json 声明：`@umijs/max: ^4.7.24`
- 实际安装：`node_modules/@umijs/max/package.json` 显示 4.7.24

## 4. 现有异常处理出口盘点

### 4.1 必须接管的现有出口（方案 5.3 列表）

| 出口 | 文件 | 当前行为 | 协议 |
|------|------|----------|------|
| MVC 异常 filter | ABP 内置 | ObjectResult/AJAX/JSON Accept 才处理 | ABP 信封 |
| 隐式 AbpExceptionHandlingMiddleware | `AbpAdminHttpApiHostModule.cs:1516` UseUnitOfWork | 同上 | ABP 信封 |
| FriendlyBusinessExceptionMiddleware | `FriendlyBusinessExceptionMiddleware.cs:84-94` | 带码 BusinessException → 403 + ABP 信封 + Data | ABP 信封 |
| 图片繁忙 filter | `ImageProcessingBusyExceptionFilter.cs:20` | 补 Retry-After 头，不接管异常 | ABP 信封 + 头 |
| TokenEndpoint 限流 | `TokenEndpointRateLimitingMiddleware.cs:107-120` | 抛限流异常时写 429 + Retry-After + OAuth 风格 JSON | OAuth 风格 |
| 会话撤销 | `IdentitySessionValidationMiddleware.cs:57-61` | 写 session_revoked/error_description | OAuth 风格 |
| 租户失配 | `AbpAdminHttpApiHostModule.cs:1502-1507` | 写 tenant_mismatch | OAuth 风格 |
| 文件上传拒绝 | `FileUploadContentGuardMiddleware.cs:204-223` | 写 400 + {fileName, reason} | 自定义 |
| 用户导出 BadRequest | `UserExportController.cs:56-61` | 返回 error 字符串 | 纯文本 |
| 审计日志导出 BadRequest | `AuditLogExportController.cs:59-64` | 返回本地化字符串 | 纯文本 |
| 匿名文件分享 | `FileShareController.cs:27-40` | FileResult 或显式 404 | 文件流 |
| GDPR 下载 | `GdprDownloadController.cs:48-57` | FileResult 或异常 | 文件流 |
| TokenGateway 错误 | `TokenGateway/Http/*.cs` | 独立协议（501、SSE 错误帧） | 独立协议 |

### 4.2 UserFriendlyException 构造点统计（待 P0-4 完整盘点）

初步扫描：49 个 `new UserFriendlyException` 构造点
- 带显式 code：12 处
- 未显式 code：37 处（部分可能预插值 ex.Message）

完整盘点见 P0-4。

## 5. 现有测试基建

### 5.1 HostUnderTest

- 位置：`backend/test/AbpAdmin.HttpApi.Host.Tests/HostUnderTest.cs`
- 能力：创建真实 `WebApplicationBuilder`，复用宿主模块初始化
- 环境：固定 Development（非 Development 需 openiddict.pfx）
- 用途：静态断言端点表、DI 注册、管线顺序

### 5.2 HostTestBed

- 位置：`backend/test/AbpAdmin.HttpApi.Host.Tests/HostTestBed.cs`
- 能力：进程内共享一份完整初始化宿主（InitializeApplicationAsync），不启动 Kestrel
- 数据库：Testcontainers PG 专用 schema `abp_admin_hosttest`
- 约束：从不 Dispose（Quartz 静态 LoggerFactory 缓存问题）

### 5.3 AnonymousEndpointSweepTests

- 位置：`backend/test/AbpAdmin.HttpApi.Host.Tests/Authorization/AnonymousEndpointSweepTests.cs`
- 能力：
  - 枚举所有 RouteEndpoint
  - 检查 IAllowAnonymous 元数据
  - 双向断言：匿名端点必须在白名单 / 登录链路端点必须匿名
- 复用价值：P0-2 端点登记可复用其枚举逻辑

## 6. 基线验证命令

```powershell
# 后端构建
Set-Location E:\code\abp_admin-wt\ceh-p0
dotnet build backend/AbpAdmin.slnx

# 前端错误处理测试
Set-Location web
pnpm install --frozen-lockfile
pnpm exec vitest run src/requestErrorConfig.test.ts
```

## 7. P0 实施约束

- **运行时行为不变**：P0 不替换任何全局异常处理，只建立登记和清单
- **待接出口有明确适配负责人**：每个出口关联验收 ID
- **登记正反向漂移检查**：新增/删除/签名变化使 CI 失败
- **不复制/提交方案文档**：留给用户决定
