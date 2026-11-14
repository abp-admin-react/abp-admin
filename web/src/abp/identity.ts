/**
 * 身份用户 / 角色 API 封装（ABP Identity + 本仓库 IdentityUserAdmin 扩展）。
 *
 * ## 边界
 * - 页面只认本文件导出；路径与查询参数（PascalCase：Filter/Sorting/SkipCount/MaxResultCount）只在此适配。
 * - 相对路径 `/api/identity/*` 与 `/api/app/identity-user-admin/*`，主机由 proxy.ts / nginx 转发。
 * - `getCurrentAccountStatus`：读「当前登录用户是否必须改密」；后端在方法内鉴权，
 *   匿名不可用；返回体只含 shouldChangePassword / reason，不含敏感资料。
 *   app.tsx getInitialState 用它决定是否强制跳转 `/account/force-change-password`。
 */
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
  extraProperties?: { Remark?: string | null };
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

export function getUser(id: string) {
  return request<IdentityUserDto>(`/api/identity/users/${encodeURIComponent(id)}`, {
    method: 'GET',
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

/**
 * 仅直接角色名（不含组织单元派生）。编辑框的已选值只能来自这里。
 * ABP 的 SetRolesAsync 用「直接 ∪ 部门」做差集，把部门角色名再提交也不会插入 AbpUserRoles；
 * 所以这里不要改成官方 GET /users/{id}/roles，否则勾选状态和离开部门后的权限会对不上。
 */
export async function getUserDirectRoleNames(id: string) {
  return request<{ items: string[] }>(
    `/api/app/identity-user-admin/${id}/direct-role-names`,
    { method: 'GET' },
  );
}

/**
 * 官方有效角色（直接成员 ∪ 部门角色），只用于标出「来自部门、离开即失效」。
 * 这些名字不能写进 updateUser.roleNames：ABP 不会据此建成直接成员，勾选会看起来保存成功、下次又空。
 */
export async function getUserEffectiveRoleNames(id: string) {
  const result = await request<{ items?: IdentityRoleDto[] }>(
    `/api/identity/users/${id}/roles`,
    { method: 'GET' },
  );
  return (result.items ?? [])
    .map((item) => item.name)
    .filter((name): name is string => !!name);
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

/** 当前操作者可分配的角色（Identity 官方用户端点，包含角色分配的防提权约束）。 */
export async function getAssignableRoles() {
  return request<{ items: IdentityRoleDto[] }>(
    '/api/identity/users/assignable-roles',
    { method: 'GET' },
  );
}

export async function createRole(data: {
  name: string;
  isDefault?: boolean;
  isPublic?: boolean;
  extraProperties?: IdentityRoleDto['extraProperties'];
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
    extraProperties?: IdentityRoleDto['extraProperties'];
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

/** 角色下的用户（只读视角，RoleUserAdminAppService） */
export interface RoleUserDto {
  id: string;
  userName: string;
  name?: string;
  email?: string;
  phoneNumber?: string;
  isActive: boolean;
  /** ISO 时间；晚于当前即处于锁定 */
  lockoutEnd?: string;
  creationTime: string;
}

/**
 * 查询角色下的用户（分页）。只读——角色分派/改派在用户管理页完成，
 * 这里只回答「谁拥有这个角色」。
 */
export async function getRoleUsers(params: {
  roleId: string;
  current?: number;
  pageSize?: number;
  sorting?: string;
}) {
  const maxResultCount = params.pageSize ?? 10;
  const skipCount = ((params.current ?? 1) - 1) * maxResultCount;
  return request<PagedResult<RoleUserDto>>('/api/app/role-user-admin', {
    method: 'GET',
    params: {
      RoleId: params.roleId,
      Sorting: params.sorting,
      SkipCount: skipCount,
      MaxResultCount: maxResultCount,
    },
  });
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

/**
 * 重置用户密码：走 Volo `IdentityUserAppService.UpdateAsync` 整体 PUT——ABP 10.7.0
 * 开源版没有专用的管理员重置密码端点，带非空 password 的 Update 就是官方唯一方式。
 * 以下三条均对照官方源码核实（abpframework/abp@10.7.0 IdentityUserAppService.cs）：
 * - `Password` 非空才执行 Remove+Add 重置，为空则完全不动密码；
 * - `RoleNames == null` 时 UpdateUserByInput 跳过 SetRolesAsync——故这里**省略
 *   roleNames**：省一次串行 GET roles，也消除「并发窗口内他人改的角色被本请求
 *   回写覆盖」（角色回写无并发戳保护）；改角色属于 UserForm 的显式编辑流；
 * - `ConcurrencyStamp` 经 SetConcurrencyStampIfNotNull 落到实体，列表行快照过期
 *   时由 EF 并发令牌以 409 拒绝，不会静默覆盖他处已改的用户字段。
 */
export async function resetUserPassword(
  user: IdentityUserDto,
  password: string,
) {
  return updateUser(user.id, {
    userName: user.userName,
    email: user.email,
    name: user.name,
    surname: user.surname,
    phoneNumber: user.phoneNumber,
    isActive: user.isActive,
    lockoutEnabled: user.lockoutEnabled,
    password,
    concurrencyStamp: user.concurrencyStamp,
  });
}

// ==================== 用户导出 ====================

export type UserExportResultDto = {
  isQueued: boolean;
  message?: string;
};

/** 同步导出用户（≤1000 条时直接下载文件） */
export function exportUsers(filter?: string) {
  const params = filter ? `?filter=${encodeURIComponent(filter)}` : '';
  window.open(
    `/api/app/identity-user-admin/export${params}`,
    '_blank',
    'noopener,noreferrer',
  );
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

/** 下载导出文件（按 ID） */
export function downloadExportFile(id: string) {
  window.open(
    `/api/app/identity-user-admin/export-file/${id}`,
    '_blank',
    'noopener,noreferrer',
  );
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

/**
 * 当前账户状态（强制改密闸门）。
 * - shouldChangePassword=true 时前端必须拦到改密页，不可进业务壳。
 * - reason 仅作展示/排障，勿当权限码。
 * - 与后端约定：接口自身校验登录态；无 Token → 401（全局跳登录），不是 404。
 */
export type AccountStatusDto = {
  shouldChangePassword: boolean;
  reason?: string;
};

/** GET current-account-status：只问「要不要改密」，不拉资料。 */
export async function getCurrentAccountStatus() {
  return request<AccountStatusDto>(
    '/api/app/identity-user-admin/current-account-status',
    {
      method: 'GET',
    },
  );
}
