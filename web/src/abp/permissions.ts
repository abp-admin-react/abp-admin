import { request } from '@umijs/max';

export type PermissionGrantInfo = {
  name: string;
  displayName: string;
  parentName?: string;
  isGranted: boolean;
  isEditable?: boolean;
};

export type PermissionGroup = {
  name: string;
  displayName: string;
  permissions: PermissionGrantInfo[];
};

export type PermissionListResult = {
  entityDisplayName: string;
  groups: PermissionGroup[];
};

export async function getPermissions(
  providerName: string,
  providerKey: string,
) {
  return request<PermissionListResult>(
    '/api/permission-management/permissions',
    {
      method: 'GET',
      params: { providerName, providerKey },
    },
  );
}

export async function updatePermissions(
  providerName: string,
  providerKey: string,
  permissions: { name: string; isGranted: boolean }[],
) {
  return request('/api/permission-management/permissions', {
    method: 'PUT',
    params: { providerName, providerKey },
    data: { permissions },
  });
}
