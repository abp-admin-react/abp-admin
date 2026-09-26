import { request } from '@umijs/max';

export type IdentityUserDto = {
  id: string;
  userName: string;
  name?: string;
  surname?: string;
  email: string;
  emailConfirmed?: boolean;
  phoneNumber?: string;
  phoneNumberConfirmed?: boolean;
  twoFactorEnabled?: boolean;
  isActive: boolean;
  lockoutEnabled: boolean;
  accessFailedCount?: number;
  lockoutEnd?: string | null;
  creationTime?: string;
  lastPasswordChangeTime?: string | null;
  concurrencyStamp?: string;
};

export type IdentityRoleDto = {
  id: string;
  name: string;
  isDefault?: boolean;
  isPublic?: boolean;
  isStatic?: boolean;
  concurrencyStamp?: string;
};

export type PagedResult<T> = {
  items: T[];
  totalCount: number;
};

export type IdentityUserCreateDto = {
  userName: string;
  email: string;
  password: string;
  name?: string;
  surname?: string;
  phoneNumber?: string;
  isActive: boolean;
  lockoutEnabled: boolean;
  roleNames?: string[];
};

export type IdentityUserUpdateDto = Omit<IdentityUserCreateDto, 'password'> & {
  password?: string;
  concurrencyStamp?: string;
};

export async function getUsers(params: {
  current?: number;
  pageSize?: number;
  filter?: string;
  /** ABP 排序串（如 "UserName desc"），由列头 sorter 经 sorterToAbpSorting 生成 */
  sorting?: string;
}) {
  const maxResultCount = params.pageSize ?? 10;
  const skipCount = ((params.current ?? 1) - 1) * maxResultCount;
  return request<PagedResult<IdentityUserDto>>('/api/identity/users', {
    method: 'GET',
    params: {
      Filter: params.filter,
      Sorting: params.sorting,
      SkipCount: skipCount,
      MaxResultCount: maxResultCount,
    },
  });
}

export async function createUser(data: IdentityUserCreateDto) {
  return request<IdentityUserDto>('/api/identity/users', {
    method: 'POST',
    data,
  });
}

export async function updateUser(id: string, data: IdentityUserUpdateDto) {
  return request<IdentityUserDto>(`/api/identity/users/${id}`, {
    method: 'PUT',
    data,
  });
}

export async function deleteUser(id: string) {
  return request(`/api/identity/users/${id}`, { method: 'DELETE' });
}

export async function getUserRoles(id: string) {
  return request<{ items: IdentityRoleDto[] }>(
    `/api/identity/users/${id}/roles`,
    { method: 'GET' },
  );
}

export async function updateUserRoles(id: string, roleNames: string[]) {
  return request(`/api/identity/users/${id}/roles`, {
    method: 'PUT',
    data: { roleNames },
  });
}

export async function getRoles(params: {
  current?: number;
  pageSize?: number;
  filter?: string;
  /** ABP 排序串（如 "Name asc"），由列头 sorter 经 sorterToAbpSorting 生成 */
  sorting?: string;
}) {
  const maxResultCount = params.pageSize ?? 10;
  const skipCount = ((params.current ?? 1) - 1) * maxResultCount;
  return request<PagedResult<IdentityRoleDto>>('/api/identity/roles', {
    method: 'GET',
    params: {
      Filter: params.filter,
      Sorting: params.sorting,
      SkipCount: skipCount,
      MaxResultCount: maxResultCount,
    },
  });
}

export async function getAllRoles() {
  return request<{ items: IdentityRoleDto[] }>('/api/identity/roles/all', {
    method: 'GET',
  });
}

export async function createRole(data: {
  name: string;
  isDefault?: boolean;
  isPublic?: boolean;
}) {
  return request<IdentityRoleDto>('/api/identity/roles', {
    method: 'POST',
    data,
  });
}

export async function updateRole(
  id: string,
  data: {
    name: string;
    isDefault?: boolean;
    isPublic?: boolean;
    concurrencyStamp?: string;
  },
) {
  return request<IdentityRoleDto>(`/api/identity/roles/${id}`, {
    method: 'PUT',
    data,
  });
}

export async function deleteRole(id: string) {
  return request(`/api/identity/roles/${id}`, { method: 'DELETE' });
}

export function isUserLocked(user: IdentityUserDto) {
  return !!user.lockoutEnd && new Date(user.lockoutEnd).getTime() > Date.now();
}

export async function lockUser(id: string) {
  return request(`/api/app/identity-user-admin/${id}/lock`, { method: 'POST' });
}

export async function unlockUser(id: string) {
  return request(`/api/app/identity-user-admin/${id}/unlock`, {
    method: 'POST',
  });
}

/** 管理端按用户启用/禁用双因素认证（对标 ABP Identity Pro 用户页 2FA 开关） */
export async function setUserTwoFactorEnabled(id: string, enabled: boolean) {
  return request(`/api/app/identity-user-admin/${id}/set-two-factor-enabled`, {
    method: 'POST',
    data: { enabled },
  });
}

export type UserTwoFactorStatusDto = {
  userId: string;
  twoFactorEnabled: boolean;
};

/**
 * 批量取用户 2FA 状态（Volo 用户列表契约不含 twoFactorEnabled，列表页据此补齐）。
 * GET 查询串用重复 userIds 键：手动拼接，避免 axios 数组序列化加 [] 导致后端绑定失败。
 */
export async function getTwoFactorStatuses(
  userIds: string[],
): Promise<UserTwoFactorStatusDto[]> {
  // ABP 约定路由会剥掉 Get 前缀：GetTwoFactorStatusesAsync → GET two-factor-statuses
  const query = userIds
    .map((id) => `userIds=${encodeURIComponent(id)}`)
    .join('&');
  return request<UserTwoFactorStatusDto[]>(
    `/api/app/identity-user-admin/two-factor-statuses?${query}`,
    { method: 'GET' },
  );
}

export async function resetUserPassword(
  user: IdentityUserDto,
  password: string,
) {
  const userRoles = await getUserRoles(user.id);
  return updateUser(user.id, {
    userName: user.userName,
    email: user.email,
    name: user.name,
    surname: user.surname,
    phoneNumber: user.phoneNumber,
    isActive: user.isActive,
    lockoutEnabled: user.lockoutEnabled,
    roleNames: (userRoles.items || []).map((item) => item.name),
    password,
    concurrencyStamp: user.concurrencyStamp,
  });
}

// ==================== T2.6 用户导入导出 ====================

export type UserExportResultDto = {
  isQueued: boolean;
  message?: string;
};

export type UserImportRowErrorDto = {
  rowNumber: number;
  userName?: string;
  errorMessage?: string;
};

export type UserImportResultDto = {
  totalCount: number;
  succeededCount: number;
  failedCount: number;
  failureReportId?: string;
  errors: UserImportRowErrorDto[];
};

/** 同步导出用户（≤1000 条时直接下载文件） */
export function exportUsers(filter?: string) {
  const params = filter ? `?filter=${encodeURIComponent(filter)}` : '';
  window.open(`/api/app/identity-user-admin/export${params}`, '_blank');
}

/** 异步导出用户（>1000 条时转后台作业，完成后邮件通知） */
export async function enqueueExportUsers(filter?: string) {
  return request<UserExportResultDto>(
    '/api/app/identity-user-admin/enqueue-export',
    {
      method: 'POST',
      params: { filter },
    },
  );
}

/** 下载导入模板 */
export function downloadImportTemplate() {
  window.open('/api/app/identity-user-admin/import-template', '_blank');
}

/** 导入用户 */
export async function importUsers(file: File) {
  const formData = new FormData();
  formData.append('file', file);
  return request<UserImportResultDto>('/api/app/identity-user-admin/import', {
    method: 'POST',
    data: formData,
    requestType: 'form',
  });
}

/** 下载导入失败明细 */
export function downloadImportFailureReport(id: string) {
  window.open(
    `/api/app/identity-user-admin/import-failure-report/${id}`,
    '_blank',
  );
}

/** 下载导出文件（按 ID） */
export function downloadExportFile(id: string) {
  window.open(`/api/app/identity-user-admin/export-file/${id}`, '_blank');
}

/** 要求用户下次登录时修改密码 */
export async function requireChangePasswordOnNextLogin(id: string) {
  return request(
    `/api/app/identity-user-admin/${id}/require-change-password-on-next-login`,
    {
      method: 'POST',
    },
  );
}

/** 获取当前账户状态 */
export type AccountStatusDto = {
  shouldChangePassword: boolean;
  reason?: string;
};

export async function getCurrentAccountStatus() {
  return request<AccountStatusDto>(
    '/api/app/identity-user-admin/current-account-status',
    {
      method: 'GET',
    },
  );
}
