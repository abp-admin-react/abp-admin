import { request } from '@umijs/max';
import type { ImpersonationResultDto } from './oidc';

/** Linked Accounts（关联账号，v1 限同租户） */
export type LinkedAccountDto = {
  linkId: string;
  userId: string;
  userName: string;
  emailAddress: string;
  tenantId?: string;
};

export async function getLinkedAccounts() {
  return request<LinkedAccountDto[]>('/api/app/account-link', {
    method: 'GET',
  });
}

// skipErrorHandler：绑定失败（密码错误/已关联）由表单内联提示，不走全局报错弹窗
export async function linkAccount(data: {
  userNameOrEmail: string;
  password: string;
}) {
  return request<LinkedAccountDto>('/api/app/account-link/link', {
    method: 'POST',
    data,
    skipErrorHandler: true,
  });
}

export async function unlinkAccount(id: string) {
  return request(`/api/app/account-link/${id}`, { method: 'DELETE' });
}

/** 切换到关联账号：返回目标账号完整令牌，调用方用 applyImpersonatedTokens 重建会话 */
export async function switchToLinkedAccount(id: string) {
  return request<ImpersonationResultDto>(
    `/api/app/account-link/${id}/switch`,
    { method: 'POST' },
  );
}
