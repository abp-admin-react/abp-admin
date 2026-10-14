# 前端环境变量与本地开关说明

本文档是 `config/`、`src/abp/env.ts` 的旁注：仓库里没有 `.env` 基线文件，可变项如下。
**禁止**把密码、client secret、token 写进仓库或本说明的示例以外的地方。

## Node 进程环境变量（`max dev` / `max build`）

| 变量 | 用在哪 | 干什么 | 可配值 | 默认 | 改错会怎样 |
| --- | --- | --- | --- | --- | --- |
| `ABP_TARGET` | `config/proxy.ts` | 开发代理上游后端 origin | `https://localhost:443xx`（44300–44399） | `https://localhost:44300` | 接口/登录代理失败 |
| `UMI_ENV` | `config/config.ts` | 选择 `proxy.ts` 的 `dev`/`test`/`pre` 档 | `dev` / `test` / `pre` | `dev` | 取错代理清单；test/pre 缺 OIDC 路径 |
| `MOCK` | package scripts | Umi mock；财务另用 localStorage | scripts 里常用 `none` | — | 与 `finance.useMock` 无关 |
| `COMMIT_HASH` | `config/config.ts` define | 打进包的提交号 | git sha | 无则用 `CF_PAGES_COMMIT_SHA` 或 `git rev-parse` | 仅版本信息缺失 |
| `CF_PAGES_COMMIT_SHA` | 同上 | Cloudflare Pages 注入的提交号 | sha | — | 同上 |
| `CI` | define | CI 标记 | 任意真值 | 空 | 一般无影响 |
| `ANALYZE` | `pnpm analyze` | 打开打包分析 | `1` | 空 | 仅构建分析 |

### 真源关系（端口）

1. **后端 Host 监听端口**（本机约定 44300）才是 API 真源。
2. **前端 `ABP_TARGET` / proxy 默认** 必须指向该端口；`launchSettings.json` 不是前端真源。
3. **前端 dev 端口**（8000/8002/…）是 Umi 的，只影响 OIDC 回调白名单，不写在 proxy 里。
4. **生产** 不走 proxy.ts，走根目录 `nginx.conf`（路径清单须与 proxy `dev` 一致）。

## 浏览器 localStorage（仅本机调试）

| 键 | 取值 | 作用 | 注意 |
| --- | --- | --- | --- |
| `finance.useMock` | `1` | `src/abp/finance.ts` 走内存假数据 | 验收/联调前必须关掉 |
| `finance.shellAccess` | `1` | `src/access.ts` 放行财务菜单/按钮看壳 | **不能绕过后端 403**；ApproveOwn / Scope.Agent 不吃此键 |

## OIDC（代码内配置）

见 `src/abp/env.ts`：`authority` / `clientId` / `scope` / `appName`。
`authority` 用 `window.location.origin`，与 `ABP_TARGET` 职责不同（一个给浏览器 OIDC，一个给 Node 代理上游）。
