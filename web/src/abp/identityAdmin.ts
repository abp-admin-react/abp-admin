import { request } from '@umijs/max';
import type { PagedResult } from './identity';

export type OrganizationUnitDto = {
  id: string;
  parentId?: string | null;
  code: string;
  displayName: string;
};

export async function getOrganizationUnits() {
  return request<OrganizationUnitDto[]>('/api/app/organization-unit', {
    method: 'GET',
  });
}

export async function createOrganizationUnit(data: {
  displayName: string;
  parentId?: string;
}) {
  return request<OrganizationUnitDto>('/api/app/organization-unit', {
    method: 'POST',
    data,
  });
}

export async function updateOrganizationUnit(
  id: string,
  data: { displayName: string },
) {
  return request(`/api/app/organization-unit/${id}`, { method: 'PUT', data });
}

export async function deleteOrganizationUnit(id: string) {
  return request(`/api/app/organization-unit/${id}`, { method: 'DELETE' });
}

export async function getOrganizationUnitMembers(
  id: string,
  paging?: { skipCount?: number; maxResultCount?: number },
) {
  return request<PagedResult<{ id: string; userName: string; email?: string }>>(
    `/api/app/organization-unit/${id}/members`,
    {
      method: 'GET',
      params: {
        SkipCount: paging?.skipCount ?? 0,
        MaxResultCount: paging?.maxResultCount ?? 10,
      },
    },
  );
}

/**
 * 移动组织单元。parentId 为空＝移到根级。
 * 父子关系走请求体（后端 MoveOrganizationUnitInput）：路由段无法表达 null，
 * 也为「移到根」保留了可空语义；防环校验在服务端（UI 不是控制）。
 */
export async function moveOrganizationUnit(id: string, parentId?: string | null) {
  return request(`/api/app/organization-unit/${id}/move`, {
    method: 'POST',
    data: { parentId: parentId ?? null },
  });
}

export async function addOrganizationUnitMembers(
  id: string,
  userIds: string[],
) {
  return request(`/api/app/organization-unit/${id}/members`, {
    method: 'POST',
    data: userIds,
  });
}

export async function removeOrganizationUnitMember(id: string, userId: string) {
  return request(`/api/app/organization-unit/${id}/member/${userId}`, {
    method: 'DELETE',
  });
}

export async function getOrganizationUnitRoles(id: string) {
  return request<{ items: { id: string; name: string }[] }>(
    `/api/app/organization-unit/${id}/roles`,
    { method: 'GET' },
  );
}

export async function addOrganizationUnitRoles(id: string, roleIds: string[]) {
  return request(`/api/app/organization-unit/${id}/roles`, {
    method: 'POST',
    data: roleIds,
  });
}

export async function removeOrganizationUnitRole(id: string, roleId: string) {
  return request(`/api/app/organization-unit/${id}/role/${roleId}`, {
    method: 'DELETE',
  });
}

export type ClaimTypeDto = {
  id: string;
  name: string;
  required: boolean;
  isStatic: boolean;
  regex?: string;
  description?: string;
  valueType: number;
};

export async function getClaimTypes(params: {
  current?: number;
  pageSize?: number;
}) {
  const maxResultCount = params.pageSize ?? 10;
  const skipCount = ((params.current ?? 1) - 1) * maxResultCount;
  return request<PagedResult<ClaimTypeDto>>('/api/app/claim-type', {
    method: 'GET',
    params: { SkipCount: skipCount, MaxResultCount: maxResultCount },
  });
}

export async function getAllClaimTypes() {
  return request<ClaimTypeDto[]>('/api/app/claim-type/lookup', {
    method: 'GET',
  });
}

export async function createClaimType(
  data: Partial<ClaimTypeDto> & { name: string },
) {
  return request('/api/app/claim-type', { method: 'POST', data });
}

export async function updateClaimType(id: string, data: Partial<ClaimTypeDto>) {
  return request(`/api/app/claim-type/${id}`, { method: 'PUT', data });
}

export async function deleteClaimType(id: string) {
  return request(`/api/app/claim-type/${id}`, { method: 'DELETE' });
}

export type ClaimValueDto = { claimType: string; claimValue: string };

export async function getUserClaims(userId: string) {
  return request<ClaimValueDto[]>(
    `/api/app/identity-claim/user-claims/${userId}`,
    {
      method: 'GET',
    },
  );
}

export async function updateUserClaims(
  userId: string,
  claims: ClaimValueDto[],
) {
  return request(`/api/app/identity-claim/user-claims/${userId}`, {
    method: 'PUT',
    data: claims,
  });
}

export async function getRoleClaims(roleId: string) {
  return request<ClaimValueDto[]>(
    `/api/app/identity-claim/role-claims/${roleId}`,
    {
      method: 'GET',
    },
  );
}

export async function updateRoleClaims(
  roleId: string,
  claims: ClaimValueDto[],
) {
  return request(`/api/app/identity-claim/role-claims/${roleId}`, {
    method: 'PUT',
    data: claims,
  });
}

export type SecurityLogDto = {
  id: string;
  creationTime: string;
  applicationName?: string;
  identity?: string;
  action?: string;
  userName?: string;
  clientId?: string;
  clientIpAddress?: string;
  browserInfo?: string;
  correlationId?: string;
};

export async function getSecurityLogs(params: {
  current?: number;
  pageSize?: number;
  userName?: string;
  action?: string;
  applicationName?: string;
  identity?: string;
  clientId?: string;
  startTime?: string;
  endTime?: string;
}) {
  const maxResultCount = params.pageSize ?? 10;
  const skipCount = ((params.current ?? 1) - 1) * maxResultCount;
  return request<PagedResult<SecurityLogDto>>('/api/app/security-log', {
    method: 'GET',
    params: {
      SkipCount: skipCount,
      MaxResultCount: maxResultCount,
      UserName: params.userName,
      Action: params.action,
      ApplicationName: params.applicationName,
      Identity: params.identity,
      ClientId: params.clientId,
      StartTime: params.startTime,
      EndTime: params.endTime,
    },
  });
}

export type IdentitySessionDto = {
  id: string;
  sessionId: string;
  userId: string;
  device?: string;
  deviceInfo?: string;
  clientId?: string;
  ipAddresses?: string;
  signedIn: string;
  lastAccessed?: string | null;
};

export async function getSessions(params: {
  current?: number;
  pageSize?: number;
  userId?: string;
  device?: string;
  clientId?: string;
}) {
  const maxResultCount = params.pageSize ?? 10;
  const skipCount = ((params.current ?? 1) - 1) * maxResultCount;
  return request<PagedResult<IdentitySessionDto>>(
    '/api/app/identity-session',
    {
      method: 'GET',
      params: {
        SkipCount: skipCount,
        MaxResultCount: maxResultCount,
        UserId: params.userId,
        Device: params.device,
        ClientId: params.clientId,
      },
    },
  );
}

export async function revokeSession(id: string) {
  return request(`/api/app/identity-session/${id}/revoke`, { method: 'POST' });
}

/** 吊销指定用户的全部会话（强制全端下线） */
export async function revokeAllSessionsByUser(userId: string) {
  return request(`/api/app/identity-session/revoke-all-by-user/${userId}`, {
    method: 'POST',
  });
}
