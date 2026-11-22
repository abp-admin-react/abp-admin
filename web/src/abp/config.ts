/**
 * ABP 应用配置与租户名解析。
 *
 * - getApplicationConfiguration：登录后初始态真源（当前用户、grantedPolicies、
 *   settingValues、租户、功能开关等）。skipErrorHandler=true —— 未登录 401 不弹全局错，
 *   由 app.tsx getInitialState 自行降级。
 * - findTenantByName：匿名解析租户。找不到或读取失败由登录入口处理。
 */
import { request } from '@umijs/max';
import type { AbpApplicationConfiguration, FindTenantResult } from './types';

export async function getApplicationConfiguration() {
  return request<AbpApplicationConfiguration>(
    '/api/abp/application-configuration',
    {
      method: 'GET',
      params: { includeLocalizationResources: false },
      skipErrorHandler: true,
    },
  );
}

// 登录入口尚未确定租户。匿名解析租户名时不发送旧令牌或租户 Cookie。
async function readAnonymousConfiguration<T>(url: string): Promise<T> {
  const response = await fetch(url, {
    credentials: 'omit',
    cache: 'no-store',
    signal: AbortSignal.timeout(10_000),
  });
  if (!response.ok) {
    throw new Error(`无法读取登录配置（HTTP ${response.status}）`);
  }
  return response.json();
}

export async function findTenantByName(name: string) {
  return readAnonymousConfiguration<FindTenantResult>(
    `/api/abp/multi-tenancy/tenants/by-name/${encodeURIComponent(name)}`,
  );
}
