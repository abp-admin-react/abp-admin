import { request } from '@umijs/max';
import type { ImpersonationResultDto } from './oidc';

export type UserLoginDto = {
  loginProvider: string;
  providerKey: string;
  providerDisplayName?: string;
};

export async function getExternalLogins() {
  return request<UserLoginDto[]>('/api/app/account-security/logins', {
    method: 'GET',
  });
}

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

export async function getAuthenticatorStatus() {
  return request<AuthenticatorStatusDto>(
    '/api/app/account-security/authenticator-status',
    { method: 'GET' },
  );
}

export async function resetAuthenticatorKey() {
  return request<{ sharedKey: string; authenticatorUri: string }>(
    '/api/app/account-security/reset-authenticator-key',
    { method: 'POST' },
  );
}

export async function enableAuthenticator(code: string) {
  return request<{ recoveryCodes: string[] }>(
    '/api/app/account-security/enable-authenticator',
    { method: 'POST', data: { code } },
  );
}

export async function disableAuthenticator(code: string) {
  return request('/api/app/account-security/disable-authenticator', {
    method: 'POST',
    data: { code },
  });
}

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

export async function getDelegatedToOthers() {
  return request<IdentityUserDelegationDto[]>(
    '/api/app/identity-user-delegation/delegated-to-others',
    { method: 'GET' },
  );
}

export async function getDelegatedToMe() {
  return request<IdentityUserDelegationDto[]>(
    '/api/app/identity-user-delegation/delegated-to-me',
    { method: 'GET' },
  );
}

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

export async function deleteDelegation(id: string) {
  return request(`/api/app/identity-user-delegation/${id}`, {
    method: 'DELETE',
  });
}

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

export async function getPasskeys() {
  return request<{ items: UserPasskeyDto[] }>(
    '/api/app/account-passkey',
    { method: 'GET' },
  );
}

export async function getPasskeyCreationOptions() {
  return request<{ json: string }>(
    '/api/app/account-passkey/creation-options',
    { method: 'GET' },
  );
}

export async function registerPasskey(data: {
  credentialJson: string;
  name?: string;
}) {
  return request('/api/app/account-passkey/register', {
    method: 'POST',
    data,
  });
}

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
