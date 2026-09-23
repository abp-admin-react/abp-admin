import type React from 'react';
import type { MenuTreeDto } from '@/abp/menus';

/** 菜单编辑表单值（MenuFormModal / index 共用）。 */
export type MenuFormValues = {
  parentId?: string | null;
  type: number;
  title: string;
  name?: string;
  path?: string;
  icon?: string;
  orderNo: number;
  permissionName?: string;
  isHide: boolean;
  isEnabled: boolean;
  remark?: string;
};

/** 编辑目标：新增（可指定上级）或编辑既有节点。 */
export type MenuEditTarget =
  | { mode: 'create'; parent?: MenuTreeDto }
  | { mode: 'edit'; node: MenuTreeDto };

/** TreeSelect 树数据节点（菜单树/权限树共用形状，替代 any）。 */
export type TreeSelectNode = {
  title: React.ReactNode;
  value: string;
  children: TreeSelectNode[];
};

/** 权限定义平铺列表 → TreeSelect 树数据（按 parentName 组装）。 */
export function buildPermissionTreeData(
  options: {
    name: string;
    displayName: string;
    parentName?: string | null;
  }[],
): TreeSelectNode[] {
  const nodes = new Map<string, TreeSelectNode>();
  for (const opt of options) {
    nodes.set(opt.name, {
      title: `${opt.displayName} (${opt.name})`,
      value: opt.name,
      children: [],
    });
  }
  const roots: TreeSelectNode[] = [];
  for (const opt of options) {
    const node = nodes.get(opt.name)!;
    const parent = opt.parentName ? nodes.get(opt.parentName) : undefined;
    if (parent) {
      parent.children.push(node);
    } else {
      roots.push(node);
    }
  }
  return roots;
}

/** 菜单树 → TreeSelect 树数据（上级菜单选择用）。 */
export function toMenuTreeSelectData(tree: MenuTreeDto[]): TreeSelectNode[] {
  const toNode = (node: MenuTreeDto): TreeSelectNode => ({
    title: node.title,
    value: node.id,
    children: node.children.map(toNode),
  });
  return tree.map(toNode);
}
