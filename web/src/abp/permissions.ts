/**
 * 权限授予读写（ABP Permission Management）。
 *
 * - 角色通过稳定 ID 和打开时的名称读写；服务端在锁定角色后核对名称。
 * - 用户和客户端通过 ABP 的 providerName + providerKey 读写。
 * - 菜单/按钮是否可见另见 access.ts 的 grantedPolicies，不走本文件。
 */
import { request } from '@umijs/max';
import type { IdentityRoleDto } from './identity';

export type PermissionProvider = 'U' | 'C';
type RoleTarget = Pick<IdentityRoleDto, 'id' | 'name'>;
type PermissionChange = { name: string; isGranted: boolean };

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

export type PermissionRequestContext = {
  tenantId?: string;
  userId: string;
};

function roleRequestOptions(context: PermissionRequestContext) {
  return {
    expectedTenantId: context.tenantId ?? null,
    expectedUserId: context.userId,
  };
}

function rolePermissionsPath(role: RoleTarget) {
  return `/api/identity/roles/${encodeURIComponent(role.id)}/permissions`;
}

export function getRolePermissions(
  role: RoleTarget,
  context: PermissionRequestContext,
) {
  return request<PermissionListResult>(rolePermissionsPath(role), {
    method: 'GET',
    ...roleRequestOptions(context),
    params: { ExpectedRoleName: role.name },
  });
}

export function updateRolePermissions(
  role: RoleTarget,
  permissions: PermissionChange[],
  context: PermissionRequestContext,
) {
  return request<void>(rolePermissionsPath(role), {
    method: 'PUT',
    ...roleRequestOptions(context),
    data: { expectedRoleName: role.name, permissions },
  });
}

export async function getPermissions(
  providerName: PermissionProvider,
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
  providerName: PermissionProvider,
  providerKey: string,
  permissions: PermissionChange[],
) {
  return request('/api/permission-management/permissions', {
    method: 'PUT',
    params: { providerName, providerKey },
    data: { permissions },
  });
}
