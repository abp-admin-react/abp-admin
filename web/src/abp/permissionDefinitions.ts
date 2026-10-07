import { request } from '@umijs/max';

/** 权限组定义记录（PermissionGroupDefinitionRecord 镜像） */
export type PermissionGroupRecord = {
  id: string;
  name: string;
  /** 落库的本地化串（"L:资源:键" 或纯文本，原样） */
  displayName: string;
  /** 按当前会话文化解析后的显示名 */
  displayNameLocalized: string;
};

/** 权限定义记录（PermissionDefinitionRecord 镜像） */
export type PermissionDefinitionRecord = {
  id: string;
  groupName: string | null;
  name: string;
  parentName: string | null;
  displayName: string;
  displayNameLocalized: string;
  isEnabled: boolean;
  /** 0=Both 1=Host 2=Tenant */
  multiTenancySide: number;
};

export type PagedResult<T> = {
  items: T[];
  totalCount: number;
};

export async function getPermissionGroups(params: {
  current?: number;
  pageSize?: number;
  filter?: string | null;
}): Promise<PagedResult<PermissionGroupRecord>> {
  const maxResultCount = params.pageSize ?? 20;
  const skipCount = ((params.current ?? 1) - 1) * maxResultCount;
  const result = await request<PagedResult<PermissionGroupRecord>>(
    '/api/app/permission-definition-management/groups',
    {
      method: 'GET',
      params: {
        SkipCount: skipCount,
        MaxResultCount: maxResultCount,
        Filter: params.filter,
      },
    },
  );
  return { items: result.items ?? [], totalCount: result.totalCount ?? 0 };
}

export async function getPermissionDefinitions(params: {
  current?: number;
  pageSize?: number;
  groupName?: string | null;
  filter?: string | null;
}): Promise<PagedResult<PermissionDefinitionRecord>> {
  const maxResultCount = params.pageSize ?? 20;
  const skipCount = ((params.current ?? 1) - 1) * maxResultCount;
  const result = await request<PagedResult<PermissionDefinitionRecord>>(
    '/api/app/permission-definition-management/definitions',
    {
      method: 'GET',
      params: {
        SkipCount: skipCount,
        MaxResultCount: maxResultCount,
        GroupName: params.groupName,
        Filter: params.filter,
      },
    },
  );
  return { items: result.items ?? [], totalCount: result.totalCount ?? 0 };
}

export async function createPermissionGroup(params: {
  name: string;
  displayName: string;
}): Promise<PermissionGroupRecord> {
  return request('/api/app/permission-definition-management/group', {
    method: 'POST',
    data: { Name: params.name, DisplayName: params.displayName },
  });
}

export async function updatePermissionGroup(
  id: string,
  params: { displayName: string },
): Promise<PermissionGroupRecord> {
  return request(`/api/app/permission-definition-management/${id}/group`, {
    method: 'PUT',
    data: { DisplayName: params.displayName },
  });
}

export async function deletePermissionGroup(id: string): Promise<void> {
  return request(`/api/app/permission-definition-management/${id}/group`, {
    method: 'DELETE',
  });
}

export async function createPermissionDefinition(params: {
  groupName: string;
  name: string;
  parentName?: string;
  displayName: string;
  isEnabled: boolean;
}): Promise<PermissionDefinitionRecord> {
  // 请求体显式 PascalCase 映射（与仓库服务层口径一致）
  return request('/api/app/permission-definition-management/definition', {
    method: 'POST',
    data: {
      GroupName: params.groupName,
      Name: params.name,
      ParentName: params.parentName,
      DisplayName: params.displayName,
      IsEnabled: params.isEnabled,
    },
  });
}

export async function updatePermissionDefinition(
  id: string,
  params: { displayName: string; isEnabled: boolean },
): Promise<PermissionDefinitionRecord> {
  return request(`/api/app/permission-definition-management/${id}/definition`, {
    method: 'PUT',
    data: { DisplayName: params.displayName, IsEnabled: params.isEnabled },
  });
}

export async function deletePermissionDefinition(id: string): Promise<void> {
  return request(`/api/app/permission-definition-management/${id}/definition`, {
    method: 'DELETE',
  });
}
