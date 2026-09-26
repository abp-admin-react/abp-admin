import { request } from '@umijs/max';
import type { ImpersonationResultDto } from './oidc';

export type UserLoginDto = {
  loginProvider: string;
  providerKey: string;
  providerDisplayName?: string;
};

/** 当前用户已绑定的外部登录（provider + providerKey 列表）。 */
export async function getExternalLogins() {
  return request<UserLoginDto[]>('/api/app/account-security/logins', {
    method: 'GET',
  });
}

/** 解绑外部登录：provider + providerKey 二元组唯一定位一条绑定。 */
export async function removeExternalLogin(data: {
  loginProvider: string;
  providerKey: string;
}) {
  return request('/api/app/account-security/remove-login', {
    method: 'POST',
    data,
  });
}

export type AuthenticatorStatusDto = {
  enabled: boolean;
  hasAuthenticatorKey: boolean;
};

/** 验证器（TOTP）状态：enabled=双因素已启用；hasAuthenticatorKey=已生成密钥但未通过验证。 */
export async function getAuthenticatorStatus() {
  return request<AuthenticatorStatusDto>(
    '/api/app/account-security/authenticator-status',
    { method: 'GET' },
  );
}

/** 重新生成 TOTP 共享密钥（旧密钥作废）；返回 sharedKey 与 authenticatorUri 供认证器 App 扫码/手输。 */
export async function resetAuthenticatorKey() {
  return request<{ sharedKey: string; authenticatorUri: string }>(
    '/api/app/account-security/reset-authenticator-key',
    { method: 'POST' },
  );
}

/** 用认证器当前 6 位码完成验证并启用双因素；成功返回一次性恢复码列表。 */
export async function enableAuthenticator(code: string) {
  return request<{ recoveryCodes: string[] }>(
    '/api/app/account-security/enable-authenticator',
    { method: 'POST', data: { code } },
  );
}

/** 解绑认证器：需提供认证器当前验证码，后端强校验后才作废密钥。 */
export async function disableAuthenticator(code: string) {
  return request('/api/app/account-security/disable-authenticator', {
    method: 'POST',
    data: { code },
  });
}

/** 用户委托：授权他人在指定时间段内以我的身份登录操作。 */
export type IdentityUserDelegationDto = {
  id: string;
  sourceUserId: string;
  targetUserId: string;
  sourceUserName?: string;
  targetUserName?: string;
  startTime: string;
  endTime: string;
  isActive: boolean;
};

/** 我委托给他人的列表（我作为 source，即授权方）。 */
export async function getDelegatedToOthers() {
  return request<IdentityUserDelegationDto[]>(
    '/api/app/identity-user-delegation/delegated-to-others',
    { method: 'GET' },
  );
}

/** 他人委托给我的列表（我作为 target，即可代登录方）。 */
export async function getDelegatedToMe() {
  return request<IdentityUserDelegationDto[]>(
    '/api/app/identity-user-delegation/delegated-to-me',
    { method: 'GET' },
  );
}

/** 创建委托：指定目标用户与起止时间，返回创建后的委托记录。 */
export async function createDelegation(data: {
  targetUserId: string;
  startTime: string;
  endTime: string;
}) {
  return request<IdentityUserDelegationDto>(
    '/api/app/identity-user-delegation/delegate',
    { method: 'POST', data },
  );
}

/** 删除委托（按委托记录 Id，未到期也可删）。 */
export async function deleteDelegation(id: string) {
  return request(`/api/app/identity-user-delegation/${id}`, {
    method: 'DELETE',
  });
}

/** 以被委托身份开始会话：返回目标用户完整令牌，调用方用 applyImpersonatedTokens 重建会话。 */
export async function startDelegation(id: string) {
  return request<ImpersonationResultDto>(
    `/api/app/identity-user-delegation/${id}/start`,
    { method: 'POST' },
  );
}

export type UserPasskeyDto = {
  credentialId: string;
  name?: string;
  createdAt?: string;
};

/** 当前用户的 Passkey（WebAuthn 凭据）列表。 */
export async function getPasskeys() {
  return request<{ items: UserPasskeyDto[] }>('/api/app/account-passkey', {
    method: 'GET',
  });
}

/** 取服务端生成的 WebAuthn 注册挑战（JSON 串），原样传给 navigator.credentials.create。 */
export async function getPasskeyCreationOptions() {
  return request<{ json: string }>(
    '/api/app/account-passkey/creation-options',
    { method: 'GET' },
  );
}

/** 提交浏览器产出的注册结果（credentialJson）与备注名，完成 Passkey 注册。 */
export async function registerPasskey(data: {
  credentialJson: string;
  name?: string;
}) {
  return request('/api/app/account-passkey/register', {
    method: 'POST',
    data,
  });
}

/** 删除 Passkey：credentialId 是 base64url 串，进路径段前需转义。 */
export async function deletePasskey(credentialId: string) {
  return request(
    `/api/app/account-passkey/${encodeURIComponent(credentialId)}`,
    { method: 'DELETE' },
  );
}

/**
 * 绑定外部登录必须 POST。Host Identity cookie 为 SameSite=Lax：跨站 GET 会带 cookie，跨站 POST 不会。
 * Provider 由服务端白名单再校验，这里只负责发表单，不把任意 scheme 拼进 GET 查询串。
 */
export function linkExternalLogin(provider: string) {
  const form = document.createElement('form');
  form.method = 'POST';
  form.action = '/Account/LinkLogin';
  form.style.display = 'none';

  const providerInput = document.createElement('input');
  providerInput.name = 'Provider';
  providerInput.value = provider;
  form.appendChild(providerInput);

  const returnInput = document.createElement('input');
  returnInput.name = 'ReturnUrl';
  returnInput.value = window.location.href;
  form.appendChild(returnInput);

  document.body.appendChild(form);
  form.submit();
}
