# 后端 Agent 说明

.NET 10 / ABP vNext 分层模块化单体（Domain / Application / HttpApi.Host…），EF Core + PostgreSQL，
Quartz 承载后台作业与定时调度。Cursor rules：`../.cursor/rules/framework`。
全栈契约地图见仓库根 `docs/framework-contracts.md`，前端侧约定见 `web/AGENTS.md`。

## 出站 HTTP（HttpAgent）

所有出站 HTTP 统一走 **HttpAgent**（独立包，非 Furion 本体）。编写 HttpAgent 相关代码前，**先读
`../docs/httpagent-llms-full.txt`**——官方全量文档单文件，API 签名与用法以它为准，不要凭记忆猜
方法名（例如 `SetRawStringContent` 会给文本包一层引号，原文发送要用 `SetContent(text, contentType)`；
重试策略是 `SetRetry(options => options.SetRetryIntervals(...))`，状态码触发与异常触发语义不同）。

- 文档索引：https://http.furion.net/llms.txt ；官方文档更新后刷新本地副本：
  `curl https://http.furion.net/llms-full.txt -o docs/httpagent-llms-full.txt`
- 项目内范式：`src/AbpAdmin.Domain/ModuleConfigurators/HttpRemoteConfigurator.cs`——
  声明式接口（`IHttpDeclarative` + `[HttpClientName]`）绑定命名 HttpClient，**注册跟随消费方所在模块**；
  第三方 5xx 非 JSON 体的容错解析参照 `TolerantJsonContentConverter`（fail-closed，不炸穿）。
- 调试：`HttpRemote:Profiler=true` 时**全部**出站客户端控制台直出完整报文（经
  `ConfigureHttpClientDefaults` 全局挂载，新消费方零接线）；报文可能含密钥/令牌，仅本地临时开启。

## 命令

```bash
dotnet build backend/AbpAdmin.slnx
dotnet test backend/test/AbpAdmin.Application.Tests   # 其余测试工程在 backend/test/ 下
```
