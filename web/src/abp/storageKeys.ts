/**
 * 浏览器存储键名唯一真源（localStorage / sessionStorage）。
 *
 * - 干什么：所有本应用写入浏览器存储的键都从这里取，统一带前缀 `abpadmin:`。
 * - 为什么要前缀：同一源（如 http://localhost:8000）上常跑多个项目（MobileDot、exchange 等），
 *   不带前缀的 `abp.tenant`、`oidc.user:...` 会互相覆盖/串号（拿到别的项目的 token 或租户）。
 * - 可配：只改 STORAGE_PREFIX；格式建议 `<项目名>:`，必须以冒号结尾、全小写。
 * - 默认：`abpadmin:`。
 * - 改错会怎样：改前缀 = 所有用户旧键失效 → 需重新登录、租户选择/空闲计时重置；
 *   设成空串 = 回到无隔离状态，多项目同源时串号。
 * - 禁止在业务代码里直接写字面量键名；新增键先加到这里并写注释。
 * - 本文件只放键名，不放任何值/密钥。
 */
export const STORAGE_PREFIX = 'abpadmin:';

export const STORAGE_KEYS = {
  /** localStorage：当前租户 {id,name} JSON（tenant.ts）。缺失 = 宿主租户。 */
  tenant: `${STORAGE_PREFIX}tenant`,
  /** sessionStorage：登录前要回跳的站内路径（utils/redirect.ts）。仅本标签页有效，用完即删。 */
  redirect: `${STORAGE_PREFIX}redirect`,
  /** localStorage：最后活动时间戳 ms（IdleSessionWatcher），跨标签同步空闲登出。 */
  idleLastActivity: `${STORAGE_PREFIX}idle-last-activity`,
  /** localStorage：'1' = 财务走内存假数据（finance.ts）。验收前必须删除，否则数据"假成功"。 */
  financeUseMock: `${STORAGE_PREFIX}finance.useMock`,
  /** localStorage：'1' = 前端放行财务菜单看壳（access.ts）。接口仍按后端权限 403，不是授权。 */
  financeShellAccess: `${STORAGE_PREFIX}finance.shellAccess`,
  /**
   * oidc-client-ts WebStorageStateStore 的 prefix（oidc.ts）。
   * 实际令牌键 = `abpadmin:oidc.user:<authority>:<client_id>`；PKCE 状态在 sessionStorage 同前缀。
   * 读 token 一律走 oidc.ts getAccessToken()，不要手拼此键。
   */
  oidcPrefix: `${STORAGE_PREFIX}oidc.`,
} as const;
