# Web 前端 Agent 说明（Cursor）

本目录是 AbpAdmin 管理端，只用 **Cursor**，不用 Claude Code。

## 读这些

- Cursor rules：`.cursor/rules/`（技术栈 `web-stack`、ABP 业务边界 `abp-frontend`）
- 仓库级 rules：`../.cursor/rules/web/`
- MCP：`.cursor/mcp.json`（官方 `@ant-design/cli mcp`）

官方文档：https://ant.design/docs/react/for-agents-cn

## 命令

```bash
pnpm start
pnpm build
pnpm lint
pnpm exec antd info Button
pnpm exec antd lint ./src
```

包管理只用 pnpm。不要覆盖 `src/abp/**`（手写主服务层）。`src/services/**` 的定位见下节。

## 服务层约定（AI 手写，无代码生成）

openapi 生成器已退役，前端 API 层不再有「生成物」，全部由 AI 对照后端契约手写维护：

- **唯一主层是 `src/abp/**`**：新增端点只写进这里。写之前先读后端 `AbpAdmin.Application.Contracts` 里对应的 AppService 接口与 DTO（路由 = `/api/app/<service>/<method>` 按 ABP 动态 API 规则推导），函数用语义化命名（如 `moveOrganizationUnit`），类型就地定义或放 `src/abp/types.ts`。
- URL 一律写**相对路径**（`/api/app/...`）、无 baseURL——开发请求走 umi 代理（`config/proxy.ts`），不要写死后端地址；**查询参数用 PascalCase**（`SkipCount` / `MaxResultCount`，ABP 模型绑定默认口径），camelCase→PascalCase 适配收在 `src/abp` 函数内，页面不感知。
- **`src/services/abpadmin/` 是历史 openapi 生成物，已冻结**：不再新增、不再重新生成、页面不要再新接入。仅剩存量域仍在用（language/languageText、myNotification/notificationManagement、scheduledJob、textTemplate、virtualFileExplorer、dataScopeDemo，以及 `src/abp/settingUi.ts`、`dataDictionary.ts`、`proModules.ts` 的委托目标 auditLog/backgroundJob 等）——**哪个域要改动时，顺手整域迁入 `src/abp` 并删除对应生成文件**，逐步清空。
- `src/services/abp-admin-web/`、`src/services/ant-design-pro/` 为脚手架演示残留，不动。
- 契约漂移以**后端 Contracts 为事实源**：改后端接口时同步改 `src/abp` 对应函数；`pnpm tsc` 只兜底类型层面的不一致，不再承担生成物漂移检测（该机制随生成器退役）。
- **可空性镜像规则（机器强制）**：镜像字段可空性必须逐字段对齐后端 C#——`T?` / 可空引用 ⇔ TS 的 `T | null`，非空 ⇔ 不加 `| null`。任何生成器都不可信（umi `nullable` 是「可选即 null」的粗开关；ABP 官方 Angular 代理也丢可空性，见 abp#22798/#25176）。后端 `FrontendContractSnapshotTests`（Contracts/Snapshots/frontend-contract-shapes.json）会把前端消费的 DTO 形状反射为快照：改后端契约该测试必红，此时同步镜像后用 `FRONTEND_CONTRACT_SNAPSHOT_UPDATE=1` 重跑刷新快照、与镜像变更同提交。前端新消费一个 DTO 时，把类型加进该测试的 `SeedTypes`（嵌套 DTO 自动递归）。
