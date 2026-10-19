/**
 * Umi 开发代理（仅 `max dev` 生效；`max build` 产物不带代理，生产走 nginx.conf）。
 *
 * ## 真源与端口关系（勿和后端 launchSettings 搞反）
 * - **后端 Host 真源端口**：HttpApi.Host 实际监听端口（本机约定 `https://localhost:44300`，
 *   须落在 IIS Express 开发证书段 **44300–44399**）。launchSettings.json 只是 VS/dotnet 启动提示，
 *   不是前端真源；前端以本文件的 `target` / `ABP_TARGET` 为准。
 * - **前端 dev 端口**：Umi 自选（常见 8000/8002/8004/8006），OIDC 回调白名单须含这些源；
 *   与本文件无关。
 * - **生产**：反向代理路径须与下方 `dev` 清单一致，见仓库根 `nginx.conf`。
 *
 * @doc https://umijs.org/docs/guides/proxy
 */

/**
 * 后端基址。
 * - 干什么：所有下方 path 的代理上游。
 * - 可配：完整 origin，形如 `https://localhost:44300`（协议+主机+端口，无路径后缀）。
 * - 默认：`https://localhost:44300`（对齐本机 Host 约定端口）。
 * - 环境变量覆盖：`ABP_TARGET`（例：`ABP_TARGET=https://localhost:44301 pnpm start`）。
 * - 改错会怎样：接口全 502/ECONNREFUSED；OIDC `/connect` 跳转失败；登录后回跳断。
 * - 注意：改默认值会影响未设 ABP_TARGET 的所有人；换端口优先用环境变量，勿改仓库默认除非约定变更。
 */
const target = process.env.ABP_TARGET || 'https://localhost:44300';

/**
 * 把后端 302 Location 里的绝对地址改成相对路径，避免浏览器跳出前端源。
 * 通配 `localhost:443x`：后端换 44300–44399 内任意端口都不必改这里。
 * 若 Location 指向其他主机/非 443 段，不会改写——那种情况应查后端 Authority/SelfUrl。
 */
function rewriteLocation(proxyRes: { headers: Record<string, unknown> }) {
  const location = proxyRes.headers.location;
  if (typeof location !== 'string') {
    return;
  }
  proxyRes.headers.location = location.replace(
    /https?:\/\/localhost:443\d+/gi,
    '',
  );
}

/**
 * HTTP 反代公共项。
 * - target：见上。
 * - changeOrigin：把 Host 改成上游主机，避免后端按前端 Host 生成错误链接。
 * - secure:false：允许本地自签 HTTPS（IIS Express 开发证）；生产勿依赖本代理。
 * - onProxyRes：见 rewriteLocation。
 */
const abpProxy = {
  target,
  changeOrigin: true,
  secure: false,
  onProxyRes: rewriteLocation,
};

/**
 * SignalR：在 abpProxy 上加 `ws:true`，仅对 Upgrade 握手生效。
 * 缺此项则 `/signalr-hubs/` 实时推送连不上（通知铃、财务等 Hub）。
 */
const abpWsProxy = {
  ...abpProxy,
  ws: true,
};

/**
 * 按 `UMI_ENV` 取用（config.ts：`proxy: proxy[UMI_ENV]`）。
 * - 键名须为 `dev` | `test` | `pre`（与 package.json 的 UMI_ENV 一致）。
 * - `dev` 清单最全：OIDC + ABP 账户页 + 静态虚文件 + API + SignalR。
 * - `test`/`pre` 只保留业务 API 与 Hub；缺 OIDC 路径时不要用这两档做登录联调。
 *
 * 路径约定（与 nginx.conf 对齐，少一条会断对应功能）：
 * - `/api/`：业务与 ABP REST
 * - `/connect/`、`/.well-known/`：OIDC 发现/授权/令牌
 * - `/Account/`：后端登录等 SSR 页
 * - `/Abp/`、`/libs/`、`/Themes/`、`/__bundles/`、`/global-styles.css`、`/global-scripts.js`：ABP 虚文件/打包产物
 * - `/signalr-hubs/`：SignalR（须 ws）
 */
export default {
  dev: {
    '/api/': abpProxy,
    '/connect/': abpProxy,
    '/.well-known/': abpProxy,
    '/Account/': abpProxy,
    '/Abp/': abpProxy,
    '/libs/': abpProxy,
    '/Themes/': abpProxy,
    '/__bundles/': abpProxy,
    '/global-styles.css': abpProxy,
    '/global-scripts.js': abpProxy,
    '/signalr-hubs/': abpWsProxy,
  },
  test: {
    '/api/': abpProxy,
    '/signalr-hubs/': abpWsProxy,
  },
  pre: {
    '/api/': abpProxy,
    '/signalr-hubs/': abpWsProxy,
  },
};
