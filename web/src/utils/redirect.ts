/**
 * 登录后回跳目标（query 里的 redirect 参数 → sessionStorage 暂存 → 登录/双因素/回调页消费）
 * 的统一收口。安全属性是这条链的核心：redirect 是攻击者完全可控的输入，认证成功后跳转它
 * 等价于 post-auth open redirect——受害者带着刚建立的会话被送往钓鱼站。
 * 防线不是前缀猜测：startsWith('//') 挡不住 /\evil.com，也挡不住 /\t/evil.com——WHATWG
 * URL 规范在解析前会剥掉输入中的 tab/LF/CR、把特殊 scheme 里的反斜杠当正斜杠，两者都被
 * 归一成协议相对地址。这里用「规范化后比对 origin」：任何走私形态最终都被解析成跨源而被
 * 拒绝，返回值只含 pathname+search+hash、天然无法携带 authority。
 *
 * 默认落地（无 intended redirect 时）：按财务菜单顺序挑第一个有权限的页（产品定：有
 * Finance.Ledger.View → A1 资金总览；否则 A2→A2b→…→A7；都没有则 /welcome）。
 */
import { getApplicationConfiguration } from '@/abp/config';
import { STORAGE_KEYS } from '@/abp/storageKeys';

/** 回跳路径键（sessionStorage），见 storageKeys.ts。 */
export const REDIRECT_STORAGE_KEY = STORAGE_KEYS.redirect;

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

/** 无任何财务页权限时的兜底落地（welcome）；有权限时由 resolvePostLoginLandingPath 覆盖。 */
export const DEFAULT_LANDING_PATH = '/welcome';

/**
 * 财务菜单落地候选：顺序与 config/routes.ts /finance 子路由一致（A1→A2→A2b→A3→A4→A5→A6→A7）。
 * 权限码与 access.ts / 产品清单对齐；A2 读权限 = Ledger.View 或 Account.Manage。
 */
export const FINANCE_LANDING_CANDIDATES: ReadonlyArray<{
  path: string;
  /** 是否具备进入该页的权限（含 financeShell 前端放开菜单态）。 */
  canEnter: (policies: Record<string, boolean>, financeShell: boolean) => boolean;
}> = [
  {
    // A1 资金总览
    path: '/finance/overview',
    canEnter: (p, shell) => !!p['Finance.Ledger.View'] || shell,
  },
  {
    // A2 科目：产品定读权限 Ledger.View | Account.Manage（路由 access 见 canViewFinanceAccounts）
    path: '/finance/accounts',
    canEnter: (p, shell) =>
      !!p['Finance.Ledger.View'] || !!p['Finance.Account.Manage'] || shell,
  },
  {
    // A2b 过账规则
    path: '/finance/rules',
    canEnter: (p, shell) => !!p['Finance.Rule.View'] || shell,
  },
  {
    // A3 凭证
    path: '/finance/vouchers',
    canEnter: (p, shell) => !!p['Finance.Voucher.View'] || shell,
  },
  {
    // A4 期间
    path: '/finance/period',
    canEnter: (p, shell) => !!p['Finance.Period.View'] || shell,
  },
  {
    // A5 对账
    path: '/finance/reconcile',
    canEnter: (p, shell) => !!p['Finance.Ledger.View'] || shell,
  },
  {
    // A6 业务单据（代理常见落地）
    path: '/finance/biz-docs',
    canEnter: (p, shell) => !!p['Finance.BizDoc.View'] || shell,
  },
  {
    // A7 余额
    path: '/finance/balances',
    canEnter: (p, shell) => !!p['Finance.Balance.View'] || shell,
  },
];

/**
 * 按已授财务权限解析登录后默认落地路径。
 * - 有 Finance.Ledger.View（或 shell）→ 第一个候选即 A1；
 * - 否则沿菜单顺序找第一页；
 * - 都没有 → DEFAULT_LANDING_PATH（/welcome）。
 */
export function resolvePostLoginLandingPath(
  policies: Record<string, boolean> | null | undefined,
  options?: { financeShell?: boolean },
): string {
  const p = policies ?? {};
  const shell = options?.financeShell === true;
  for (const item of FINANCE_LANDING_CANDIDATES) {
    if (item.canEnter(p, shell)) {
      return item.path;
    }
  }
  return DEFAULT_LANDING_PATH;
}

/** 读 localStorage 的 financeShell 开关（与 access.ts 同一键）。 */
function readFinanceShellAccess(): boolean {
  try {
    return (
      typeof localStorage !== 'undefined' &&
      localStorage.getItem(STORAGE_KEYS.financeShellAccess) === '1'
    );
  } catch {
    return false;
  }
}

/**
 * 登录成功后拉 application-configuration，按 grantedPolicies 解析默认落地。
 * 接口失败时落回 /welcome，不阻塞进系统。
 */
export async function resolvePostLoginLandingPathFromServer(): Promise<string> {
  try {
    const config = await getApplicationConfiguration();
    return resolvePostLoginLandingPath(config.auth?.grantedPolicies, {
      financeShell: readFinanceShellAccess(),
    });
  } catch {
    return DEFAULT_LANDING_PATH;
  }
}

/** 消费暂存的回跳目标（回调/双因素页：读后即清；非法值与脏值落回 fallback）。 */
export function consumeIntendedRedirect(fallback = DEFAULT_LANDING_PATH): string {
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
