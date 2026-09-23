import { request } from '@umijs/max';
import type { ImpersonationResultDto } from './oidc';

export type ProfileDto = {
  userName?: string;
  email?: string;
  name?: string;
  surname?: string;
  phoneNumber?: string;
  isExternal?: boolean;
  hasPassword?: boolean;
  concurrencyStamp?: string;
};

export async function getMyProfile() {
  return request<ProfileDto>('/api/account/my-profile', { method: 'GET' });
}

export async function updateMyProfile(data: {
  userName?: string;
  email?: string;
  name?: string;
  surname?: string;
  phoneNumber?: string;
  concurrencyStamp?: string;
}) {
  return request<ProfileDto>('/api/account/my-profile', {
    method: 'PUT',
    data,
  });
}

export async function changeMyPassword(data: {
  currentPassword?: string;
  newPassword: string;
}) {
  return request('/api/account/my-profile/change-password', {
    method: 'POST',
    data,
  });
}

// ========== T2.7 双因素认证 ==========

export type TwoFactorStatusDto = {
  twoFactorEnabled?: boolean;
  emailConfirmed?: boolean;
  phoneNumberConfirmed?: boolean;
};

export async function getTwoFactorStatus() {
  return request<TwoFactorStatusDto>('/api/app/account-pro/two-factor-status', {
    method: 'GET',
  });
}

// 关闭 2FA 必须携带当前有效验证码（后端强校验）；启用时 code 省略
export async function setTwoFactorEnabled(enabled: boolean, code?: string) {
  return request('/api/app/account-pro/set-two-factor-enabled', {
    method: 'POST',
    data: { enabled, code },
  });
}

// 收件人/校验对象由服务端取当前登录用户（防越权给他人发码），入参只传通道
export async function sendTwoFactorCode(data: { provider?: string }) {
  return request('/api/app/account-pro/send-two-factor-code', {
    method: 'POST',
    data,
  });
}

export async function verifyTwoFactorCode(data: {
  code: string;
  provider?: string;
}) {
  return request('/api/app/account-pro/verify-two-factor-code', {
    method: 'POST',
    data,
  });
}

// ========== T2.7 无密码登录（Magic Link / OTP） ==========

/**
 * 消费邮件中的 Magic Link（或兜底验证码）换取登录令牌。
 * 匿名端点；tenantName 由邮件链接携带（host 邮件为空），服务端据此建立租户上下文。
 * skipErrorHandler：落地页自管错误态（过期链接要展示引导 UI，而非全局报错弹窗）。
 */
export async function loginWithMagicLink(data: {
  email: string;
  magicLinkToken?: string;
  code?: string;
  tenantName?: string;
}) {
  return request<ImpersonationResultDto>(
    '/api/app/account-pro/login-with-magic-link',
    { method: 'POST', data, skipErrorHandler: true },
  );
}

// ========== 联系方式确认（改邮箱/手机号后重新确认；后端 SetEmail/SetPhone 会重置确认状态） ==========

export async function sendEmailConfirmationCode(email: string) {
  return request('/api/app/account-pro/send-email-confirmation-code', {
    method: 'POST',
    data: { email },
  });
}

export async function confirmEmail(email: string, code: string) {
  return request('/api/app/account-pro/confirm-email', {
    method: 'POST',
    data: { email, code },
  });
}

export async function sendPhoneNumberConfirmationCode(phoneNumber: string) {
  return request('/api/app/account-pro/send-phone-number-confirmation-code', {
    method: 'POST',
    data: { phoneNumber },
  });
}

export async function confirmPhoneNumber(phoneNumber: string, code: string) {
  return request('/api/app/account-pro/confirm-phone-number', {
    method: 'POST',
    data: { phoneNumber, code },
  });
}

// ========== T2.7 模拟登录 ==========

export type { ImpersonationResultDto };

/** 以指定租户身份模拟登录（host 管理员专用，走 impersonation 扩展授权换 token） */
export async function impersonateTenant(tenantId: string) {
  return request<ImpersonationResultDto>(
    '/api/app/account-pro/impersonate-tenant',
    { method: 'POST', data: { tenantId } },
  );
}

/** 以指定用户身份模拟登录（走 impersonation 扩展授权换 token） */
export async function impersonateUser(userId: string) {
  return request<ImpersonationResultDto>(
    '/api/app/account-pro/impersonate-user',
    { method: 'POST', data: { userId } },
  );
}

/** 返回原身份。当前令牌不含 impersonator claim 时后端返回 403 */
export async function backToMyAccount() {
  return request<ImpersonationResultDto>(
    '/api/app/account-pro/back-to-my-account',
    { method: 'POST' },
  );
}
