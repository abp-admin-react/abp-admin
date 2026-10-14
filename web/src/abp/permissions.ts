/**
 * 权限授予读写（ABP Permission Management）。
 *
 * - providerName + providerKey：如 R=角色、U=用户；与权限弹窗 PermissionModal 一致。
 * - update 提交的是「变更集」{name,isGranted}[]，不是全量树。
 * - 菜单/按钮是否可见另见 access.ts 的 grantedPolicies，不走本文件。
 */
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
