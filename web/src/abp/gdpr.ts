import { request } from '@umijs/max';
import type { PagedResult } from './identity';

export interface GdprRequestDto {
  id: string;
  creationTime: string;
  readyTime: string;
}

/** 发起个人数据收集请求 */
export async function createGdprRequest() {
  return request<string>('/api/app/gdpr-request', { method: 'POST' });
}

/** 是否允许发起新请求（受 RequestTimeInterval 限制） */
export async function isNewGdprRequestAllowed() {
  return request<boolean>('/api/app/gdpr-request/is-new-request-allowed', {
    method: 'POST',
  });
}

/** 当前用户的请求列表 */
export async function getGdprRequests(params?: {
  current?: number;
  pageSize?: number;
}) {
  const maxResultCount = params?.pageSize ?? 10;
  const skipCount = ((params?.current ?? 1) - 1) * maxResultCount;
  return request<PagedResult<GdprRequestDto>>('/api/app/gdpr-request', {
    method: 'GET',
    params: { SkipCount: skipCount, MaxResultCount: maxResultCount },
  });
}

/** 第一步：获取下载 token（60 分钟过期，一次性） */
export async function getGdprDownloadToken(requestId: string) {
  return request<string>(
    `/api/app/gdpr-request/download-token/${encodeURIComponent(requestId)}`,
    { method: 'GET' },
  );
}

/** 第二步：用 token 换 ZIP 下载地址（显式 GET Controller，匿名可访问） */
export function getGdprDownloadUrl(requestId: string, token: string) {
  return `/api/app/gdpr-request/download?requestId=${encodeURIComponent(
    requestId,
  )}&token=${encodeURIComponent(token)}`;
}

/** 删除当前用户账户（需密码确认） */
export async function deleteCurrentUserAccount(password: string) {
  return request('/api/app/gdpr-request/current-user-account', {
    method: 'DELETE',
    data: { password },
  });
}
