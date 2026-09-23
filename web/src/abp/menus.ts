import { request } from '@umijs/max';

/** 菜单类型：1 目录（分组），2 菜单（页面）。 */
export const MENU_TYPE = {
  Catalog: 1,
  Menu: 2,
} as const;

export type MenuTypeValue = (typeof MENU_TYPE)[keyof typeof MENU_TYPE];

export type MenuDto = {
  id: string;
  tenantId?: string | null;
  parentId?: string | null;
  type: number;
  title: string;
  name?: string | null;
  path?: string | null;
  icon?: string | null;
  orderNo: number;
  isHide: boolean;
  isEnabled: boolean;
  permissionName?: string | null;
  remark?: string | null;
  /** 并发戳：编辑时原样回传，服务端检测到不一致返回 409（防止编辑冲突被静默覆盖）。 */
  concurrencyStamp?: string | null;
};

export type MenuTreeDto = MenuDto & {
  grantedRoles: string[];
  children: MenuTreeDto[];
};

// 创建契约不含并发戳（与后端 MenuCreateDto 一致：新建无冲突可言，多传会被忽略）
export type MenuCreateDto = Omit<Partial<MenuDto>, 'concurrencyStamp'> & {
  title: string;
  type: number;
};

/** 更新契约与后端 MenuUpdateDto 对齐：编辑表单必须原样回传并发戳（缺失则服务端跳过并发校验）。 */
export type MenuUpdateDto = MenuCreateDto & {
  concurrencyStamp?: string | null;
};

export type PermissionOptionDto = {
  name: string;
  displayName: string;
  parentName?: string | null;
};

export type MyMenuItemDto = {
  title: string;
  name?: string | null;
  path?: string | null;
  icon?: string | null;
  children: MyMenuItemDto[];
};

const BASE = '/api/app/menu';
const MY_BASE = '/api/app/my-menu';

export async function getMenuTree() {
  return request<{ items: MenuTreeDto[] }>(`${BASE}/tree`, { method: 'GET' });
}

export async function createMenu(data: MenuCreateDto) {
  return request<MenuDto>(BASE, { method: 'POST', data });
}

export async function updateMenu(id: string, data: MenuUpdateDto) {
  return request<MenuDto>(`${BASE}/${id}`, { method: 'PUT', data });
}

export async function deleteMenu(id: string) {
  return request(`${BASE}/${id}`, { method: 'DELETE' });
}

export async function getMenuRoleGrants(menuId: string) {
  return request<{ items: string[] }>(`${BASE}/role-grants/${menuId}`, {
    method: 'GET',
  });
}

export async function updateMenuRoleGrants(
  menuId: string,
  roleNames: string[],
) {
  return request(`${BASE}/role-grants/${menuId}`, {
    method: 'PUT',
    data: { roleNames },
  });
}

export async function getPermissionOptions() {
  return request<{ items: PermissionOptionDto[] }>(
    `${BASE}/permission-options`,
    {
      method: 'GET',
    },
  );
}

/** 当前登录用户可见菜单树（动态菜单数据源）。 */
export async function getMyMenu() {
  return request<{ items: MyMenuItemDto[] }>(`${MY_BASE}`, { method: 'GET' });
}
