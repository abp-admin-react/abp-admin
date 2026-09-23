import { PlusOutlined } from '@ant-design/icons';
import {
  type ActionType,
  PageContainer,
  ProTable,
} from '@ant-design/pro-components';
import { useAccess } from '@umijs/max';
import { App, Button } from 'antd';
import React, {
  useCallback,
  useEffect,
  useMemo,
  useRef,
  useState,
} from 'react';
import { getAllRoles, type IdentityRoleDto } from '@/abp/identity';
import {
  deleteMenu,
  getMenuTree,
  getPermissionOptions,
  type MenuTreeDto,
  type PermissionOptionDto,
} from '@/abp/menus';
import MenuFormModal from './components/MenuFormModal';
import { buildMenuColumns } from './components/menuColumns';
import {
  buildPermissionTreeData,
  type MenuEditTarget,
  type TreeSelectNode,
  toMenuTreeSelectData,
} from './components/menuTypes';
import RoleGrantModal from './components/RoleGrantModal';

const MenuManagement: React.FC = () => {
  const { message } = App.useApp();
  const access = useAccess();
  const actionRef = useRef<ActionType | undefined>(undefined);
  const [tree, setTree] = useState<MenuTreeDto[]>([]);
  const [loading, setLoading] = useState(false);
  const [editTarget, setEditTarget] = useState<MenuEditTarget>();
  const [permissions, setPermissions] = useState<PermissionOptionDto[]>([]);
  const [roles, setRoles] = useState<IdentityRoleDto[]>([]);
  const [roleTarget, setRoleTarget] = useState<MenuTreeDto>();

  const reload = useCallback(async () => {
    setLoading(true);
    try {
      const res = await getMenuTree();
      setTree(res.items);
    } catch {
      // 请求层已有统一错误提示
    } finally {
      setLoading(false);
    }
  }, []);

  useEffect(() => {
    reload();
  }, [reload]);

  useEffect(() => {
    if (!access.canManageMenus) return;
    getPermissionOptions()
      .then((res) => setPermissions(res.items))
      .catch(() => undefined);
    getAllRoles()
      .then((res) => setRoles(res.items))
      .catch(() => undefined);
  }, [access.canManageMenus]);

  const menuTreeData = useMemo<TreeSelectNode[]>(
    () => toMenuTreeSelectData(tree),
    [tree],
  );

  const permissionTreeData = useMemo<TreeSelectNode[]>(
    () => buildPermissionTreeData(permissions),
    [permissions],
  );

  const columns = useMemo(
    () =>
      buildMenuColumns({
        canUpdateMenus: !!access.canUpdateMenus,
        canAssignMenuRoles: !!access.canAssignMenuRoles,
        canDeleteMenus: !!access.canDeleteMenus,
        onEdit: (node) => setEditTarget({ mode: 'edit', node }),
        onAddChild: (node) => setEditTarget({ mode: 'create', parent: node }),
        onRoles: (node) => setRoleTarget(node),
        onDelete: async (node) => {
          await deleteMenu(node.id);
          message.success('菜单已删除');
          reload();
        },
      }),
    [access, message, reload],
  );

  return (
    <PageContainer>
      <ProTable<MenuTreeDto>
        rowKey="id"
        headerTitle="菜单树"
        loading={loading}
        actionRef={actionRef}
        columns={columns}
        dataSource={tree}
        search={false}
        pagination={false}
        expandable={{ defaultExpandAllRows: true }}
        toolBarRender={() =>
          [
            access.canCreateMenus ? (
              <Button
                key="create"
                type="primary"
                icon={<PlusOutlined />}
                onClick={() => setEditTarget({ mode: 'create' })}
              >
                新增顶级菜单
              </Button>
            ) : null,
          ].filter(Boolean)
        }
      />

      <MenuFormModal
        target={editTarget}
        menuTreeData={menuTreeData}
        permissionTreeData={permissionTreeData}
        onClose={() => setEditTarget(undefined)}
        onSaved={reload}
      />

      <RoleGrantModal
        target={roleTarget}
        roles={roles}
        onClose={() => setRoleTarget(undefined)}
        onSaved={reload}
      />
    </PageContainer>
  );
};

export default MenuManagement;
