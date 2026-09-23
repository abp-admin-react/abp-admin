# AbpAdmin Web

AbpAdmin 管理端前端：基于 Ant Design Pro v6（Umi Max + React 19 + antd 6 + Tailwind v4）定制，对接 ABP 后端（`../src/AbpAdmin.HttpApi.Host`）。

## 常用命令

```bash
pnpm install        # 安装依赖（只用 pnpm，Node ≥ 22）
pnpm start          # 开发服务器 http://localhost:8000（代理到后端）
pnpm tsc            # 类型检查
pnpm test           # Vitest
pnpm build          # 生产构建（产物 dist/）
pnpm openapi        # 后端接口变更后重新生成 src/services/abpadmin/
```

## 约定

- AI 协作规则见 [AGENTS.md](./AGENTS.md) 与 `.cursor/rules/`；仓库级规则见 `../.cursor/rules/`。
- `src/services/**` 为生成物，不要手改（只通过 `pnpm openapi` 重新生成）；业务封装在 `src/abp/**`。
- 依赖升级与「对齐官方 Pro 模板」流程见根 [README.md](../README.md) 第六节。
- 本项目自 ant-design-pro v6.0.3 定制而来，许可证见 [LICENSE](./LICENSE)；上游文档 https://pro.ant.design/
