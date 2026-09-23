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
