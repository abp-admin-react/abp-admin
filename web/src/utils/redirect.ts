/**
 * 登录后回跳目标（query 里的 redirect 参数 → sessionStorage 暂存 → 登录/双因素/回调页消费）
 * 的统一收口。安全属性是这条链的核心：redirect 是攻击者完全可控的输入，认证成功后跳转它
 * 等价于 post-auth open redirect——受害者带着刚建立的会话被送往钓鱼站。
 * 防线不是前缀猜测：startsWith('//') 挡不住 /\evil.com，也挡不住 /\t/evil.com——WHATWG
 * URL 规范在解析前会剥掉输入中的 tab/LF/CR、把特殊 scheme 里的反斜杠当正斜杠，两者都被
 * 归一成协议相对地址。这里用「规范化后比对 origin」：任何走私形态最终都被解析成跨源而被
 * 拒绝，返回值只含 pathname+search+hash、天然无法携带 authority。
 */
export const REDIRECT_STORAGE_KEY = 'abp.redirect';

/** 规范化 + 同源校验：通过则返回剥离了一切走私形态的 pathname+search+hash，否则 null。 */
function toSameOriginPath(raw: string): string | null {
  try {
    const url = new URL(raw, window.location.origin);
    if (url.origin !== window.location.origin) return null;
    return url.pathname + url.search + url.hash;
  } catch {
    return null;
  }
}

/** 站内相对路径守卫（写入侧）：/ 开头且规范化后同源才接受。 */
export function isSafeRedirectTarget(
  raw: string | null | undefined,
): raw is string {
  if (!raw?.startsWith('/')) return false;
  return toSameOriginPath(raw) !== null;
}

/** 暂存认证成功后的回跳目标（登录页：query 参数 → sessionStorage）。 */
export function storeIntendedRedirect(raw: string | null | undefined) {
  if (isSafeRedirectTarget(raw)) {
    sessionStorage.setItem(REDIRECT_STORAGE_KEY, raw);
  }
}

/** 消费暂存的回跳目标（回调/双因素页：读后即清；非法值与脏值落回 fallback）。 */
export function consumeIntendedRedirect(fallback = '/administration'): string {
  const raw = sessionStorage.getItem(REDIRECT_STORAGE_KEY);
  sessionStorage.removeItem(REDIRECT_STORAGE_KEY);
  return raw && isSafeRedirectTarget(raw) ? raw : fallback;
}

/**
 * 服务端下发菜单路径的渲染前守卫：与回跳同一口径。服务端菜单管理是第一道防线，
 * 这里是第二道（历史脏数据/绕过校验的行不进可信侧边栏）——防线不应弱于登录侧。
 */
export function isSafeMenuPath(path?: string | null): path is string {
  if (!path?.startsWith('/')) return false;
  return toSameOriginPath(path) !== null;
}
