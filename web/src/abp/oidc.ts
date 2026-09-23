import { User, UserManager, WebStorageStateStore } from 'oidc-client-ts';
import { abpEnv } from './env';
import { getTenantId } from './tenant';

let manager: UserManager | undefined;

function spaOrigin() {
  return window.location.origin;
}

export function getUserManager(): UserManager {
  if (!manager) {
    const origin = spaOrigin();
    manager = new UserManager({
      authority: abpEnv.authority,
      metadataUrl: `${abpEnv.authority}/.well-known/openid-configuration`,
      metadataSeed: {
        authorization_endpoint: `${abpEnv.authority}/connect/authorize`,
        token_endpoint: `${abpEnv.authority}/connect/token`,
        revocation_endpoint: `${abpEnv.authority}/connect/revocat`,
        end_session_endpoint: `${abpEnv.authority}/connect/endsession`,
        userinfo_endpoint: `${abpEnv.authority}/connect/userinfo`,
        jwks_uri: `${abpEnv.authority}/.well-known/jwks`,
      },
      client_id: abpEnv.clientId,
      redirect_uri: `${origin}/user/callback`,
      post_logout_redirect_uri: `${origin}/user/login`,
      response_type: 'code',
      scope: abpEnv.scope,
      automaticSilentRenew: false,
      loadUserInfo: false,
      userStore: new WebStorageStateStore({ store: window.localStorage }),
    });
  }
  return manager;
}

export async function getAccessToken(): Promise<string | undefined> {
  const user = await getUserManager().getUser();
  if (!user || user.expired) {
    return undefined;
  }
  return user.access_token;
}

export async function startLogin() {
  const tenantId = getTenantId();
  await getUserManager().signinRedirect({
    extraQueryParams: tenantId ? { __tenant: tenantId, prompt: 'login' } : {},
  });
}

/**
 * 账号切换：携带 prompt=select_account 重新发起授权。
 * IdP 侧跳到 /Account/SelectAccount，用户可选"继续当前账号"或"换账号"（换账号会登出当前会话落到登录表单）。
 * 与 startLogin 对齐携带 __tenant：租户子域名会话下"换账号"才能落回本租户的登录表单，
 * 否则会按 Host 上下文解析，用户只能登宿主账号。
 */
export async function switchAccount() {
  const tenantId = getTenantId();
  await getUserManager().signinRedirect({
    extraQueryParams: tenantId
      ? { __tenant: tenantId, prompt: 'select_account' }
      : { prompt: 'select_account' },
  });
}

export async function completeLogin() {
  return getUserManager().signinRedirectCallback();
}

export async function logout() {
  const tenantId = getTenantId();
  await getUserManager().signoutRedirect({
    extraQueryParams: tenantId ? { __tenant: tenantId } : {},
  });
}

// ========== T2.7 模拟登录会话接管 ==========

/** 模拟登录令牌交换结果（对应后端 ImpersonationResultDto） */
export type ImpersonationResultDto = {
  accessToken: string;
  tokenType?: string;
  expiresIn?: number;
  refreshToken?: string;
};

function decodeJwtPayload(token: string): Record<string, unknown> {
  const payload = token.split('.')[1] || '';
  const base64 = payload.replace(/-/g, '+').replace(/_/g, '/');
  const json = decodeURIComponent(
    atob(base64)
      .split('')
      .map((c) => `%${`00${c.charCodeAt(0).toString(16)}`.slice(-2)}`)
      .join(''),
  );
  return JSON.parse(json) as Record<string, unknown>;
}

/**
 * 当前会话是否处于模拟登录状态。
 * 判定依据：访问令牌里带 impersonator_userid claim（ABP 框架常量
 * AbpClaimTypes.ImpersonatorUserId，由 impersonation 扩展授权写入）。
 */
export async function getImpersonatorUserId(): Promise<string | undefined> {
  const token = await getAccessToken();
  if (!token) {
    return undefined;
  }
  try {
    const payload = decodeJwtPayload(token);
    return (payload.impersonator_userid as string) || undefined;
  } catch {
    return undefined;
  }
}

/**
 * 把模拟登录/返回原身份换来的令牌写入现有 OIDC 会话存储，接管当前会话。
 * profile 用新访问令牌的 claim 重建，使 UI 身份与令牌一致。
 */
export async function applyImpersonatedTokens(
  result: ImpersonationResultDto,
): Promise<void> {
  const manager = getUserManager();
  const existing = await manager.getUser();
  if (!existing) {
    throw new Error('当前没有有效会话，无法切换身份');
  }

  const payload = decodeJwtPayload(result.accessToken);
  const profile = {
    ...existing.profile,
    ...payload,
  } as typeof existing.profile;

  const user = new User({
    access_token: result.accessToken,
    refresh_token: result.refreshToken ?? existing.refresh_token,
    token_type: result.tokenType || 'Bearer',
    scope: existing.scope,
    profile,
    session_state: existing.session_state,
    expires_at: result.expiresIn
      ? Math.floor(Date.now() / 1000) + result.expiresIn
      : existing.expires_at,
  });
  await manager.storeUser(user);
}

/**
 * 用裸令牌建立全新 OIDC 会话（免密登录 Magic Link / OTP 落地时调用）。
 * 与 applyImpersonatedTokens 的区别：不要求已存在会话——用户从邮件点开链接时
 * 浏览器里没有任何登录态。profile 直接取访问令牌 claim（sub/role/tenant 等），
 * 页面级用户信息随后由 application-configuration 双校验补全（app.tsx getInitialState）。
 */
export async function applyTokensForNewSession(
  result: ImpersonationResultDto,
): Promise<void> {
  const payload = decodeJwtPayload(result.accessToken);

  const user = new User({
    access_token: result.accessToken,
    refresh_token: result.refreshToken,
    token_type: result.tokenType || 'Bearer',
    scope: abpEnv.scope,
    profile: payload as unknown as User['profile'],
    expires_at: result.expiresIn
      ? Math.floor(Date.now() / 1000) + result.expiresIn
      : undefined,
  });
  await getUserManager().storeUser(user);
}
