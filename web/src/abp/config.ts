/**
 * ABP 应用配置与租户名解析。
 *
 * - getApplicationConfiguration：登录后初始态真源（当前用户、grantedPolicies、
 *   settingValues、租户、功能开关等）。skipErrorHandler=true —— 未登录 401 不弹全局错，
 *   由 app.tsx getInitialState 自行降级。
 * - findTenantByName：登录页/子域名解析租户；同样 skipErrorHandler，缺失时页面跳
 *   /tenant-not-found，而不是全局 notification。
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

export async function findTenantByName(name: string) {
  return request<FindTenantResult>(
    `/api/abp/multi-tenancy/tenants/by-name/${encodeURIComponent(name)}`,
    {
      method: 'GET',
      skipErrorHandler: true,
    },
  );
}
