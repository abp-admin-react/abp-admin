import { PlusOutlined } from '@ant-design/icons';
import {
  type ActionType,
  PageContainer,
  ProTable,
} from '@ant-design/pro-components';
import { useAccess } from '@umijs/max';
import { App, Button, Tooltip } from 'antd';
import { QuestionCircleOutlined } from '@ant-design/icons';
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
import { MENU_TABLE_SCROLL_X, buildMenuColumns } from './components/menuColumns';
import {
  buildPermissionTreeData,
  type MenuEditTarget,
  type TreeSelectNode,
  toMenuTreeSelectData,
} from './components/menuTypes';
import RoleGrantModal from './components/RoleGrantModal';

/** 收集整棵树的全部节点 id（加载后默认全展开 / 展开折叠总开关用）。 */
function collectAllKeys(nodes: MenuTreeDto[]): React.Key[] {
  return nodes.flatMap((node) => [node.id, ...collectAllKeys(node.children)]);
}



const MenuManagement: React.FC = () => {
  const { message } = App.useApp();
  const access = useAccess();
  const actionRef = useRef<ActionType | undefined>(undefined);
  const [tree, setTree] = useState<MenuTreeDto[]>([]);
  const [loading, setLoading] = useState(false);
  const [expandedRowKeys, setExpandedRowKeys] = useState<React.Key[]>([]);
  const [editTarget, setEditTarget] = useState<MenuEditTarget>();
  const [permissions, setPermissions] = useState<PermissionOptionDto[]>([]);
  const [roles, setRoles] = useState<IdentityRoleDto[]>([]);
  const [roleTarget, setRoleTarget] = useState<MenuTreeDto>();
  const allRowKeys = useMemo(() => collectAllKeys(tree), [tree]);
  const allExpanded = allRowKeys.length > 0 && expandedRowKeys.length >= allRowKeys.length;

  const reload = useCallback(async () => {
    setLoading(true);
    try {
      const res = await getMenuTree();
      setTree(res.items);
      // defaultExpandAllRows 对异步加载的数据不生效（首渲染时 dataSource 为空），
      // 改为每次加载后受控全展开；管理员仍可手动收起
      setExpandedRowKeys(collectAllKeys(res.items));
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
    // 编辑态剔除自身及子孙节点，避免选中后成环（后端会拒，但不应给出非法选项）
    () =>
      toMenuTreeSelectData(
        tree,
        editTarget?.mode === 'edit' ? editTarget.node.id : undefined,
      ),
    [tree, editTarget],
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
        headerTitle={
          <span>
            菜单树&nbsp;
            <Tooltip
              title={
                <div style={{ lineHeight: 1.8 }}>
                  在此页改名、排序（OrderNo）、隐藏（IsHide）、启停、勾选角色后
                  <b>立即写库生效</b>，用户下次刷新侧边栏即见，无需改代码或重启后端。
                  <br />
                  可见性 = 启用 且 未隐藏 且（未绑权限或权限已授予）且（admin 或
                  未受角色勾选控制 或 角色命中勾选）——停用整棵子树剔除，隐藏节点子级上浮。
                  <br />
                  本页只改运行态数据库；代码里的 MenuTemplateDefinition 仅供新环境首次播种，
                  两边不会自动同步（新环境出厂默认以模板为准）。
                </div>
              }
            >
              <QuestionCircleOutlined style={{ color: '#8c8c8c', cursor: 'help' }} />
            </Tooltip>
          </span>
        }
        loading={loading}
        actionRef={actionRef}
        columns={columns}
        dataSource={tree}
        search={false}
        pagination={false}
        scroll={{ x: MENU_TABLE_SCROLL_X }}
        expandable={{
          expandedRowKeys,
          onExpandedRowsChange: (keys) => setExpandedRowKeys([...keys]),
        }}
        toolBarRender={() =>
          [
            <Button
              key="expand-toggle"
              onClick={() =>
                setExpandedRowKeys((keys) =>
                  keys.length >= allRowKeys.length ? [] : allRowKeys,
                )
              }
            >
              {allExpanded ? '折叠全部' : '展开全部'}
            </Button>,
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
