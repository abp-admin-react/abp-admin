import { request } from '@umijs/max';
import type { MenuTreeDto } from './menus';

export type TenantPackageDto = {
  id: string;
  name: string;
  remark?: string | null;
  menuCount: number;
};

export type TenantPackageCreateDto = {
  name: string;
  remark?: string | null;
};

export type TenantPackageMenuSelectionDto = {
  tree: MenuTreeDto[];
  checkedMenuIds: string[];
};

const BASE = '/api/app/tenant-package';

export async function getTenantPackages(params: {
  current?: number;
  pageSize?: number;
  filter?: string;
}) {
  return request<{ items: TenantPackageDto[]; totalCount: number }>(BASE, {
    method: 'GET',
    params: {
      Filter: params.filter,
      SkipCount: ((params.current ?? 1) - 1) * (params.pageSize ?? 10),
      MaxResultCount: params.pageSize ?? 10,
    },
  });
}

export async function createTenantPackage(data: TenantPackageCreateDto) {
  return request<TenantPackageDto>(BASE, { method: 'POST', data });
}

export async function updateTenantPackage(
  id: string,
  data: TenantPackageCreateDto,
) {
  return request<TenantPackageDto>(`${BASE}/${id}`, { method: 'PUT', data });
}

export async function deleteTenantPackage(id: string) {
  return request(`${BASE}/${id}`, { method: 'DELETE' });
}

export async function getPackageMenuSelection(id: string) {
  return request<TenantPackageMenuSelectionDto>(
    `${BASE}/${id}/menu-selection`,
    { method: 'GET' },
  );
}

export async function updatePackageMenuSelection(
  id: string,
  menuIds: string[],
) {
  return request(`${BASE}/${id}/menu-selection`, {
    method: 'PUT',
    data: { menuIds },
  });
}

/** 把套餐应用到租户：租户菜单树重置为套餐过滤后的模板拷贝（破坏性）。 */
export async function applyTenantPackage(
  tenantId: string,
  packageId: string | null,
) {
  return request(`/api/app/tenant/${tenantId}/apply-package`, {
    method: 'POST',
    data: { packageId },
  });
}
