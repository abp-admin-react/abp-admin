import { request } from '@umijs/max';
import type { CreateTenantInput, TenantDto, TenantListResult } from './types';

export async function getTenants(params: {
  current?: number;
  pageSize?: number;
  filter?: string;
}) {
  const maxResultCount = params.pageSize ?? 10;
  const skipCount = ((params.current ?? 1) - 1) * maxResultCount;
  return request<TenantListResult>('/api/multi-tenancy/tenants', {
    method: 'GET',
    params: {
      SkipCount: skipCount,
      MaxResultCount: maxResultCount,
      Filter: params.filter,
    },
  });
}

export async function createTenant(data: CreateTenantInput) {
  return request<TenantDto>('/api/multi-tenancy/tenants', {
    method: 'POST',
    data,
  });
}

export async function updateTenant(
  id: string,
  data: {
    name: string;
    concurrencyStamp?: string;
    extraProperties?: Record<string, unknown>;
  },
) {
  return request<TenantDto>(`/api/multi-tenancy/tenants/${id}`, {
    method: 'PUT',
    data,
  });
}

export async function deleteTenant(id: string) {
  return request(`/api/multi-tenancy/tenants/${id}`, {
    method: 'DELETE',
  });
}

// ========== T2.8 SaaS Pro 缺口：租户连接字符串管理（密文存储，API 只回掩码） ==========

export type TenantConnectionStringItem = {
  name: string;
  /** 已设值时为掩码 "********"；管理 API 永不返回明文 */
  value?: string | null;
};

export type TenantConnectionStringManagement = {
  isManagementEnabled: boolean;
  useSharedDatabase: boolean;
  items: TenantConnectionStringItem[];
};

export async function getTenantConnectionStringManagement(id: string) {
  return request<TenantConnectionStringManagement>(
    `/api/app/tenant/${id}/connection-strings`,
    { method: 'GET' },
  );
}

/**
 * 全量同步租户连接串。item.value 留空或回传掩码表示保持原值；
 * 已存在但未出现在 items 中的记录会被删除；useSharedDatabase 为 true 时清空全部记录。
 */
export async function updateTenantConnectionStrings(
  id: string,
  data: {
    useSharedDatabase: boolean;
    items: { name: string; value?: string }[];
  },
) {
  return request(`/api/app/tenant/${id}/connection-strings`, {
    method: 'PUT',
    data,
  });
}

/** 可分配连接串的数据库清单（只含 IsUsedByTenants = true 的数据库，不含 Default） */
export async function getAvailableTenantDatabases() {
  return request<string[]>('/api/app/tenant/available-databases', {
    method: 'GET',
  });
}

/** 校验连接串（纯校验，不写库） */
export async function checkTenantConnectionString(
  id: string,
  connectionString: string,
) {
  return request<{ isValid: boolean; errorMessage?: string }>(
    `/api/app/tenant/${id}/check-connection-string`,
    { method: 'POST', data: { connectionString } },
  );
}
