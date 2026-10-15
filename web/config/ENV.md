# 前端环境变量与本地开关说明

旁注：config/、src/abp/env.ts、src/abp/oidc.ts、请求与本地存储。仓库无 .env 基线。
**禁止**写入密码、client secret、token；示例只用占位。

---

## 时区不变量（强制）

| 环节 | 约定 |
| --- | --- |
| 后端 / API | DateTime 一律 **UTC**；带 Z 或显式偏移按字面；**无偏移也当 UTC** |
| 前端展示 | 转到 **浏览器本地 IANA 时区**（Intl.DateTimeFormat().resolvedOptions().timeZone，失败再 dayjs.tz.guess()） |
| 筛选入参 | 用户选的「本地日」→ UTC 半开区间 [startUtc, endUtc)（src/utils/time.ts） |

**禁止**写死 Asia/Shanghai / 东八区。即使用户人在上海，也必须走浏览器时区 API，不能硬编码。

---

## Node 进程环境变量（max dev / max build）

| 变量 | 用在哪 | 干什么 | 可配值 | 默认 | 改错会怎样 | 如何覆盖 |
| --- | --- | --- | --- | --- | --- | --- |
| ABP_TARGET | config/proxy.ts | 开发代理上游后端 origin | https://localhost:443xx（须 44300–44399） | https://localhost:44300 | 接口/OIDC 代理失败 | ABP_TARGET=https://localhost:44301 pnpm start |
| UMI_ENV | config/config.ts | 选 proxy.ts 的 dev/test/pre 档 | dev / test / pre | dev | 取错代理清单；test/pre 缺 /connect 无法登录联调 | package.json scripts 或 UMI_ENV=test |
| MOCK | package scripts | Umi 内置 mock；**财务假数据不看它** | 常用 none | scripts 决定 | 与 finance.useMock 无关 | MOCK=none |
| COMMIT_HASH | config/config.ts → define | 打进包的提交号 | git sha | 无则 CF_PAGES_COMMIT_SHA 再 git rev-parse | 仅版本信息缺失 | CI 注入 |
| CF_PAGES_COMMIT_SHA | 同上 | Cloudflare Pages 提交号 | sha | — | 同上 | 平台注入 |
| CI | define process.env.CI | CI 标记 | 任意 | 空 | 一般无影响 | CI 环境 |
| ANALYZE | pnpm analyze | 打包分析 | 1 | 空 | 仅影响分析构建 | ANALYZE=1 |
| NODE_ENV | 多处（app/requestErrorConfig/AutoHeight…） | development/production 分支 | development / production | 由 Umi 设置 | 勿手改；错会导致生产打开发日志 | 构建工具 |
| CHAT_API_URL | src/pages/chatbot/service.ts | 聊天演示页模型地址 | https URL | 代码内演示地址 | 演示页调不通；**未在 define 注入时读不到覆盖** | 须同时在 config.ts define 加键才生效 |
| ABP_BASE | scripts/finance-shell-shots.mjs | 截图脚本打开的前端基址 | origin | http://127.0.0.1:8100 | 脚本打不开页 | 脚本环境变量 |
| ABP_SHOT_DIR | 同上 | 截图输出目录 | 本机路径 | C:\Users\Administrator\agent-tools\abp-shots | 写到错误目录 | 脚本环境变量 |

### 端口真源

1. **后端 Host 监听**（本机约定 https://localhost:44300）= API 真源；launchSettings.json 不是前端真源。
2. **ABP_TARGET / proxy 默认** 必须指向该端口。
3. **前端 Umi 端口**（常见 8000/8002/8004/8006）只影响 OIDC 回调白名单，不写在 proxy 里。
4. **生产** 不走 proxy.ts，走根目录 nginx.conf（路径须与 proxy dev 一致）。

---

## config/proxy.ts（仅 max dev）

| 键 / 项 | 干什么 | 可配 / 约定 | 默认 | 改错会怎样 |
| --- | --- | --- | --- | --- |
| target（ABP_TARGET） | 上游后端 origin | 见上表 | https://localhost:44300 | 全代理失败 |
| changeOrigin | 把 Host 改成上游 | 布尔；应保持 true | true | 后端按前端 Host 生成错链 |
| secure | 是否校验证书 | 本地自签须 false；生产不靠本代理 | false | true 时本地 HTTPS 握手失败 |
| onProxyRes / Location 改写 | 把 localhost:443x 绝对 Location 改相对路径 | 正则通配 443 段 | 已启用 | 登录 302 跳出前端源 |
| ws（abpWsProxy） | SignalR Upgrade | 仅 /signalr-hubs/ 为 true | Hub 用 true | Hub 连不上 |
| path 清单 dev | /api/ /connect/ /.well-known/ /Account/ /Abp/ /libs/ /Themes/ /global-styles.css /global-scripts.js /signalr-hubs/ | 与 nginx 对齐 | 见文件 | 少一条断对应功能 |
| path 清单 test/pre | 仅 /api/ + /signalr-hubs/ | 不做完整登录联调 | 见文件 | 用这两档登录会缺 OIDC |
| pathRewrite | **未使用** | — | 无 | 不要擅自加，除非与后端路径迁移对齐 |

无单独 proxy timeout 配置；慢请求靠后端/nginx（生产 /api proxy_read_timeout 300s）。

---

## OIDC（src/abp/env.ts + src/abp/oidc.ts）

| 项 | 干什么 | 可配值 | 默认 / 现状 | 改错会怎样 | 如何改 |
| --- | --- | --- | --- | --- | --- |
| authority | 发现文档与 /connect/* 源 | 完整 origin | window.location.origin | 登录跳错源 / CORS | 改 env.ts（一般勿改）；**不是** ABP_TARGET |
| metadataUrl | OIDC discovery | <authority>/.well-known/openid-configuration | 由 authority 拼 | discovery 失败则靠 metadataSeed | 跟 authority |
| clientId / client_id | 客户端 ID | 与后端种子一致 | AbpAdmin_App | invalid_client | env.ts + 后端种子 |
| scope | 授权范围（空格分隔） | 后端已登记；须含 openid；offline_access 才有 refresh | openid profile email offline_access roles AbpAdmin | 缺 claim / 无 refresh / API 401 | env.ts |
| appName | 展示名 | 短字符串 | AbpAdmin | 仅展示 | env.ts |
| redirect_uri | 授权回调 | <前端源>/user/callback；须在后端 RedirectUris（含各 dev 端口） | 运行时拼 origin | invalid redirect_uri | 改 oidc.ts + 路由 + 后端白名单 |
| post_logout_redirect_uri | 注销回跳 | <前端源>/user/login；须在 PostLogoutRedirectUris | 运行时拼 | 停在后端注销页 | 同上 |
| response_type | 响应类型 | 仅 code（+ PKCE） | code | 改 implicit 后端不支持 | 勿改 |
| automaticSilentRenew | 是否定时静默续期 | true/false | false（过期后 SignalR 按需 signinSilent） | true 可能与按需续期竞态 | oidc.ts |
| loadUserInfo | 是否调 userinfo | 布尔 | false（用户信息走 application-configuration） | 多一次请求或缺字段 | oidc.ts |
| userStore | 令牌存储 | localStorage | WebStorageStateStore(localStorage) | 改 sessionStorage 则关标签丢登录 | oidc.ts |
| revocation_endpoint seed | 注销吊销兜底路径 | ABP 实路径 /connect/revocat（非 RFC revocation） | 见 oidc.ts 注释 | 路径错则吊销失败 | 与后端一致 |

---

## 全局请求（src/app.tsx request）

| 项 | 干什么 | 默认 | 改错会怎样 |
| --- | --- | --- | --- |
| baseURL | axios/Umi 请求前缀 | ''（同源相对路径） | 写成后端主机会绕过 proxy/nginx、触发 CORS/证书问题 |

请求 timeout 的真源见下方「浏览器存储键 / 请求超时 / 时区」表（勿在本文件重复维护两份）。

鉴权与错误处理：src/requestErrorConfig.ts（开发态多打日志看 NODE_ENV）。

---

## 其它代码内常量（非 env，但易被当成「配置」）

| 项 | 位置 | 说明 |
| --- | --- | --- |
| Idle THROTTLE_MS | IdleSessionWatcher | 1000ms，活动写入节流；超时分钟数来自后端设置 AbpAdmin.Account.IdleSessionTimeoutMinutes（0=关） |

## 浏览器存储键 / 请求超时 / 时区（真源见代码）

| 项 | 值 | 真源 | 改错后果 |
| --- | --- | --- | --- |
| 存储前缀 | `abpadmin:`（令牌 `abpadmin:oidc.user:<authority>:<client_id>`） | `src/abp/storageKeys.ts` | 空串=同源多项目串 token/租户；改值=全员重新登录。令牌键**勿提交、勿截图外泄**；业务代码禁手拼令牌键（统一走 oidc.ts 的 getAccessToken） |
| 请求 timeout | `300000` ms | `src/app.tsx` `request` | 0=永不超时转圈；过小误杀导出/批量过账 |
| 时区 | API 收发 UTC；仅展示层转浏览器时区 | `src/app.tsx` 注释 | 请求层改时区会导致入账日期偏一天 |

旧键（`abp.tenant`、`finance.useMock`、无前缀 `oidc.user:*`）升级后失效，需重新登录；本地开关请改用 `abpadmin:` 前缀重设。
