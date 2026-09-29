import { request } from '@umijs/max';
import { isNotFound } from '@/requestErrorConfig';

/** 数据范围类型 */
export enum DataScopeType {
  /** 全部数据 */
  All = 0,
  /** 当前组织单元及子级 */
  CurrentOuAndChildren = 1,
  /** 仅当前组织单元 */
  CurrentOu = 2,
  /** 自定义组织单元 */
  Custom = 3,
  /** 仅本人数据 */
  SelfOnly = 4,
}

export const DataScopeTypeLabels: Record<DataScopeType, string> = {
  [DataScopeType.All]: '全部数据',
  [DataScopeType.CurrentOuAndChildren]: '当前组织单元及子级',
  [DataScopeType.CurrentOu]: '仅当前组织单元',
  [DataScopeType.Custom]: '自定义组织单元',
  [DataScopeType.SelfOnly]: '仅本人数据',
};

export type RoleDataScopeDto = {
  id?: string;
  tenantId?: string;
  roleName?: string;
  scopeType?: DataScopeType;
  customOrganizationUnitIds?: string[];
};

export type CreateUpdateRoleDataScopeDto = {
  roleName: string;
  scopeType: DataScopeType;
  customOrganizationUnitIds?: string[];
};

export type PagedResult<T> = {
  items: T[];
  totalCount: number;
};

/** 获取角色数据范围列表 */
export async function getRoleDataScopes(params: {
  current?: number;
  pageSize?: number;
  sorting?: string;
}) {
  const maxResultCount = params.pageSize ?? 10;
  const skipCount = ((params.current ?? 1) - 1) * maxResultCount;
  return request<PagedResult<RoleDataScopeDto>>('/api/app/role-data-scope', {
    method: 'GET',
    params: {
      SkipCount: skipCount,
      MaxResultCount: maxResultCount,
      Sorting: params.sorting,
    },
  });
}

/** 根据角色名获取数据范围 */
export async function getRoleDataScopeByRoleName(roleName: string) {
  return request<RoleDataScopeDto>('/api/app/role-data-scope/by-role-name', {
    method: 'GET',
    params: { roleName },
    skipErrorHandler: true,
  });
}

/** 创建角色数据范围 */
export async function createRoleDataScope(data: CreateUpdateRoleDataScopeDto) {
  return request<RoleDataScopeDto>('/api/app/role-data-scope', {
    method: 'POST',
    data,
  });
}

/** 更新角色数据范围 */
export async function updateRoleDataScope(
  id: string,
  data: CreateUpdateRoleDataScopeDto,
) {
  return request<RoleDataScopeDto>(`/api/app/role-data-scope/${id}`, {
    method: 'PUT',
    data,
  });
}

/** 删除角色数据范围 */
export async function deleteRoleDataScope(id: string) {
  return request(`/api/app/role-data-scope/${id}`, { method: 'DELETE' });
}

/** 保存角色数据范围（有则更新，无则创建） */
export async function saveRoleDataScope(data: CreateUpdateRoleDataScopeDto) {
  let existingId: string | undefined;
  try {
    existingId = (await getRoleDataScopeByRoleName(data.roleName))?.id;
  } catch (error) {
    // 仅 404（未配置过数据范围，后端约定语义）走创建；其它失败（403/500/网络）向上抛——
    // 读失败被当成"不存在"会把用户的更新静默变成新建，再撞角色名唯一键报一个错乱的错
    if (!isNotFound(error)) throw error;
  }
  return existingId
    ? updateRoleDataScope(existingId, data)
    : createRoleDataScope(data);
}
