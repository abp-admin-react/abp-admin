/**
 * OIDC 客户端会话（oidc-client-ts）。
 *
 * ## 边界
 * - client_id / scope / authority 真源在 env.ts；此处只组装 UserManager。
 * - **不包含 client_secret**：SPA 公钥客户端，密钥不进浏览器、不进本仓库。
 * - 令牌存 localStorage（前缀 STORAGE_KEYS.oidcPrefix）；读写一律经本文件，
 *   禁止业务代码手拼 `oidc.user:...` 键。
 * - 登录/换号/注销均携带 `__tenant`（若有），与租户子域名会话对齐。
 * - 模拟登录与 Magic Link 落地：applyImpersonatedTokens（需已有会话） /
 *   applyTokensForNewSession（无会话亦可）。
 */
import { User, UserManager, WebStorageStateStore } from 'oidc-client-ts';
import { abpEnv } from './env';
import { STORAGE_KEYS } from './storageKeys';
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
        // revocat（不是 RFC 7009 的 revocation）：这是 ABP 框架注册的真实路径
        //（abpframework/abp#24390 及 ABP 官方支持帖均按此路径调用；实测本服务
        // POST /connect/revocat → 200 {}，/connect/revocation 反而 302 未注册）。
        // 标准客户端以 discovery 文档为准（与本值一致），此 seed 仅作 discovery
        // 不可达时的兜底，必须与后端实际注册路径一致。
        revocation_endpoint: `${abpEnv.authority}/connect/revocat`,
        end_session_endpoint: `${abpEnv.authority}/connect/endsession`,
        userinfo_endpoint: `${abpEnv.authority}/connect/userinfo`,
        jwks_uri: `${abpEnv.authority}/.well-known/jwks`,
      },
      // ---- 以下各项说明见 config/ENV.md「OIDC」节 ----
      // client_id：= 后端 OpenIddict 种子 ClientId；错 → invalid_client。
      client_id: abpEnv.clientId,
      // redirect_uri：固定 <前端源>/user/callback；该完整 URL 必须在后端客户端 RedirectUris 白名单
      //（dev 8000/8002/8004/8006 各一条），否则授权页报 invalid redirect_uri。改路径须同步路由与种子。
      redirect_uri: `${origin}/user/callback`,
      // post_logout_redirect_uri：注销后回到 <前端源>/user/login；须在 PostLogoutRedirectUris 白名单，否则停在后端注销页。
      post_logout_redirect_uri: `${origin}/user/login`,
      // response_type：只允许 'code'（授权码 + PKCE，oidc-client-ts 自动加 PKCE）；改 implicit 后端不支持。
      response_type: 'code',
      scope: abpEnv.scope,
      // automaticSilentRenew：false=不自动静默续期；过期后由 signalr.ts 按需 signinSilent()（用 refresh_token，
      // 依赖 scope 含 offline_access）。改 true 会多一路定时续期，需评估与按需续期的竞态。
      automaticSilentRenew: false,
      // loadUserInfo：false=不额外调 /connect/userinfo，用户信息来自 application-configuration。
      loadUserInfo: false,
      // userStore：令牌存 localStorage，键名 `oidc.user:<authority>:<client_id>`（跨标签共享，关浏览器不丢）。
      // 前缀 abpadmin:oidc.（见 storageKeys.ts）：同源多项目不串 token；改前缀=全员重新登录。
      userStore: new WebStorageStateStore({
        store: window.localStorage,
        prefix: STORAGE_KEYS.oidcPrefix,
      }),
      // stateStore：授权码流程的 state/PKCE verifier，存 sessionStorage（仅本标签页、回调后清除），同前缀。
      stateStore: new WebStorageStateStore({
        store: window.sessionStorage,
        prefix: STORAGE_KEYS.oidcPrefix,
      }),
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

/**
 * 发起登录。租户上下文（子域名或切换租户）必须带 prompt=login：防止 IdP 残留会话把
 * 宿主/其它租户身份静默带进本租户。
 * ⚠ 配对契约：prompt 只生效一次——宿主登录页（AbpAdminLoginModel）认证成功后会把 prompt
 * 从回跳 authorize 的 URL 摘除（否则 OpenIddict 再次强制出登录页，无限回环）。删掉这里的
 * prompt 会重新引入跨租户静默 SSO；删掉宿主摘除则所有租户登录死循环——两端缺一不可。
 */
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
