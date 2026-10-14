/**
 * 前端 OIDC / 应用标识（浏览器侧）。
 *
 * 与后端 AuthServer、OpenIddict 种子客户端必须对齐：
 * - clientId、scope 改这里却不改后端种子 → 授权页报 invalid_client / invalid_scope。
 * - authority 必须是浏览器实际访问的前端源（dev 经代理、生产经 nginx），
 *   后端 Authority/SelfUrl/CORS/RedirectUris 同指该源。
 *
 * 无 `.env` 文件：本对象即运行时配置真源；密钥/client secret 不进仓库、也不写在此。
 */
export const abpEnv = {
  /**
   * OIDC Authority（发现文档与授权端点所在源）。
   * - 干什么：oidc-client 拼 `/.well-known/openid-configuration` 与 `/connect/*`。
   * - 可配：完整 origin（协议+主机+端口），无路径。
   * - 默认/现状：`window.location.origin`（相对当前页源）。
   * - 为何相对源：dev 走 Umi 代理、prod 走 nginx，浏览器只看见前端源，避免直连后端证书/CORS。
   * - 改错会怎样：登录跳转到错误主机、混合内容、或 CORS 失败。
   * - 覆盖方式：改本字段（一般不要改）；不要用 ABP_TARGET 替代——ABP_TARGET 只给 Node 代理上游。
   */
  authority: window.location.origin,

  /**
   * OpenIddict 客户端 ID。
   * - 干什么：授权请求的 client_id。
   * - 可配：与后端种子的应用 Name/ClientId 完全一致的字符串。
   * - 默认：`AbpAdmin_App`。
   * - 改错会怎样：invalid_client，无法登录。
   */
  clientId: 'AbpAdmin_App',

  /**
   * OAuth scope 空格分隔列表。
   * - 干什么：要申请的声明与 API 资源。
   * - 可配：后端已登记的 scope；`openid` 必需；`offline_access` 才有 refresh_token；
   *   `AbpAdmin` 为业务 API 资源名（与后端 ApiResource 一致）。
   * - 默认：`openid profile email offline_access roles AbpAdmin`。
   * - 改错会怎样：缺 claim、无 refresh、或调 API 401 insufficient_scope。
   */
  scope: 'openid profile email offline_access roles AbpAdmin',

  /**
   * 应用展示名（日志/部分 UI）。
   * - 可配：任意短字符串；与权限/鉴权无关。
   * - 默认：`AbpAdmin`。
   */
  appName: 'AbpAdmin',
};
