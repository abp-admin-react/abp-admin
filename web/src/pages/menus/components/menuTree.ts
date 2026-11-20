import type { TableProps } from 'antd';
import type { MenuTreeDto } from '@/abp/menus';

export type MenuFilters = Parameters<
  NonNullable<TableProps<MenuRow>['onChange']>
>[1];

/** 权限与写操作使用完整节点，children 仅表示当前查询下可显示的分支。 */
export interface MenuRow {
  menu: MenuTreeDto;
  children?: MenuRow[];
}

export function menuPermissions(menu: MenuTreeDto): string[] {
  return menu.anyPermissionNames?.length
    ? menu.anyPermissionNames
    : menu.permissionName
      ? [menu.permissionName]
      : [];
}

/** 所有条件必须在同一节点命中。子节点命中时保留它的祖先，以便按原层级展开。 */
export function queryMenuTree(
  tree: MenuTreeDto[],
  keyword: string,
  filters: MenuFilters,
) {
  const term = keyword.trim().toLocaleLowerCase();
  const contains = (value: string | null | undefined, text: string) =>
    (value ?? '').toLocaleLowerCase().includes(text);
  const title = String(filters.title?.[0] ?? '').toLocaleLowerCase();
  const path = String(filters.path?.[0] ?? '').toLocaleLowerCase();
  const permission = String(
    filters.permissionName?.[0] ?? '',
  ).toLocaleLowerCase();
  const order = String(filters.orderNo?.[0] ?? '');
  const branchKeys: string[] = [];
  function matches(menu: MenuTreeDto) {
    const permissions = menuPermissions(menu);
    return (
      (!term ||
        [menu.title, menu.name, menu.path, ...permissions].some((text) =>
          contains(text, term),
        )) &&
      (!title || contains(menu.title, title)) &&
      (!path || contains(menu.path, path)) &&
      (!permission ||
        permissions.some((value) => contains(value, permission))) &&
      (!order || String(menu.orderNo) === order) &&
      (!filters.type?.length || filters.type.includes(menu.type)) &&
      (!filters.isEnabled?.length ||
        filters.isEnabled.includes(String(menu.isEnabled)))
    );
  }
  function visit(nodes: MenuTreeDto[]): MenuRow[] {
    const rows: MenuRow[] = [];
    for (const menu of nodes) {
      const children = visit(menu.children);
      if (children.length) {
        branchKeys.push(menu.id);
        rows.push({ menu, children });
      } else if (matches(menu)) {
        rows.push({ menu });
      }
    }
    return rows;
  }
  const rows = visit(tree);
  return { rows, branchKeys };
}
