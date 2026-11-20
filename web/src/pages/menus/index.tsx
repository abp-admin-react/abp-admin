import { PlusOutlined, QuestionCircleOutlined } from '@ant-design/icons';
import { PageContainer } from '@ant-design/pro-components';
import { useAccess } from '@umijs/max';
import { AgGridReact } from 'ag-grid-react';
import { App, Button, Tooltip } from 'antd';
import React, { useCallback, useEffect, useMemo, useState } from 'react';
import {
  deleteMenu,
  getMenuTree,
  getPermissionOptions,
  type MenuTreeDto,
  type PermissionOptionDto,
} from '@/abp/menus';
import { toGridColumns, useSearchableLocalGrid } from '@/components/remoteGrid';
import MenuFormModal from './components/MenuFormModal';
import { buildMenuColumns } from './components/menuColumns';
import {
  buildPermissionTreeData,
  type MenuEditTarget,
  type TreeSelectNode,
  toMenuTreeSelectData,
} from './components/menuTypes';

/** 收集整棵树的全部节点 id（加载后默认全展开 / 展开折叠总开关用）。 */
function collectAllKeys(nodes: MenuTreeDto[]): React.Key[] {
  return nodes.flatMap((node) => [node.id, ...collectAllKeys(node.children)]);
}

const MenuManagement: React.FC = () => {
  const { message } = App.useApp();
  const access = useAccess();

  const [tree, setTree] = useState<MenuTreeDto[]>([]);
  const [loading, setLoading] = useState(false);
  const [expandedRowKeys, setExpandedRowKeys] = useState<React.Key[]>([]);
  const [editTarget, setEditTarget] = useState<MenuEditTarget>();
  const [permissions, setPermissions] = useState<PermissionOptionDto[]>([]);
  const allRowKeys = useMemo(() => collectAllKeys(tree), [tree]);
  const allExpanded =
    allRowKeys.length > 0 && expandedRowKeys.length >= allRowKeys.length;

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

  const { rows, parents } = useMemo(() => {
    const rows: MenuTreeDto[] = [];
    const parents = new Map<string, string[]>();
    const visit = (nodes: MenuTreeDto[], ancestors: string[]) => {
      for (const node of nodes) {
        rows.push(node);
        parents.set(node.id, ancestors);
        visit(node.children, [...ancestors, node.id]);
      }
    };
    visit(tree, []);
    return { rows, parents };
  }, [tree]);
  const titles = useMemo(
    () => new Map(rows.map((node) => [node.id, node.title])),
    [rows],
  );
  const expanded = useMemo(() => new Set(expandedRowKeys), [expandedRowKeys]);
  const columns = useMemo(
    () =>
      buildMenuColumns({
        ancestorPath: (node) =>
          parents
            .get(node.id)
            ?.map((id) => titles.get(id))
            .join(' / ') ?? '',
        treeToggle: (node) => (
          <span
            style={{
              display: 'var(--menu-toggle-display, inline)',
              paddingLeft: (parents.get(node.id)?.length ?? 0) * 14,
            }}
          >
            {node.children.length > 0 ? (
              <Button
                type="text"
                size="small"
                aria-label={`${expanded.has(node.id) ? '折叠' : '展开'}${node.title}`}
                aria-expanded={expanded.has(node.id)}
                onClick={() =>
                  setExpandedRowKeys((keys) =>
                    expanded.has(node.id)
                      ? keys.filter((key) => key !== node.id)
                      : [...keys, node.id],
                  )
                }
              >
                {expanded.has(node.id) ? '▾' : '▸'}
              </Button>
            ) : null}
          </span>
        ),
        canCreateMenus: !!access.canCreateMenus,
        canUpdateMenus: !!access.canUpdateMenus,
        canDeleteMenus: !!access.canDeleteMenus,
        onEdit: (node) => setEditTarget({ mode: 'edit', node }),
        onAddChild: (node) => setEditTarget({ mode: 'create', parent: node }),
        onDelete: async (node) => {
          await deleteMenu(node.id);
          message.success('菜单已删除');
          reload();
        },
      }),
    [access, message, reload, parents, expanded, titles],
  );

  const grid = useSearchableLocalGrid<MenuTreeDto>({
    columnDefs: toGridColumns(columns, {
      pro: true,
      kinds: { type: 'number', orderNo: 'number', isEnabled: 'boolean' },
    }),
    rowData: rows,
    getRowId: ({ data }) => data.id,
    ariaLabel: '菜单',
    keywordFields: ['title', 'name', 'path', 'permissionName'],
    pagination: false,
  });
  const visibleRows = useMemo(
    () =>
      grid.hasFilters
        ? rows
        : rows.filter((n) =>
            parents.get(n.id)?.every((id) => expanded.has(id)),
          ),
    [grid.hasFilters, rows, parents, expanded],
  );
  return (
    <PageContainer>
      <div
        style={{
          display: 'flex',
          gap: 8,
          alignItems: 'center',
          marginBottom: 8,
          flexWrap: 'wrap',
        }}
      >
        <span>
          菜单树&nbsp;
          <Tooltip
            title={
              <div style={{ lineHeight: 1.8 }}>
                在此维护菜单名称、顺序、隐藏和启停状态。修改保存后，用户刷新页面即可看到。
                <br />
                菜单随绑定的功能权限显示；到角色页配置功能权限，无需再为菜单分配角色。
                <br />
                停用目录会隐藏其全部子菜单；隐藏目录自身时，有权限的子菜单仍可显示。
              </div>
            }
          >
            <QuestionCircleOutlined
              style={{ color: '#8c8c8c', cursor: 'help' }}
            />
          </Tooltip>
        </span>
        {grid.searchBox}
        {[
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
        ].filter(Boolean)}
      </div>
      <div
        style={
          {
            '--menu-path-display': grid.hasFilters ? 'inline' : 'none',
            '--menu-toggle-display': grid.hasFilters ? 'none' : 'inline',
          } as React.CSSProperties
        }
      >
        <AgGridReact<MenuTreeDto>
          {...grid.gridProps}
          rowData={visibleRows}
          loading={loading}
          postSortRows={({ nodes }) => {
            // 先按用户选择的列对同级菜单排序，再按树的先序输出，确保子菜单紧跟父级。
            const rank = new Map(nodes.map((n, i) => [n.data?.id, i]));
            const chain = (id: string) => [...(parents.get(id) ?? []), id];
            nodes.sort((a, b) => {
              if (!a.data || !b.data) return 0;
              const left = chain(a.data.id),
                right = chain(b.data.id);
              for (let i = 0; i < Math.min(left.length, right.length); i++) {
                if (left[i] !== right[i])
                  return (rank.get(left[i]) ?? 0) - (rank.get(right[i]) ?? 0);
              }
              return left.length - right.length;
            });
          }}
        />
      </div>
      <MenuFormModal
        target={editTarget}
        menuTreeData={menuTreeData}
        permissionTreeData={permissionTreeData}
        onClose={() => setEditTarget(undefined)}
        onSaved={reload}
      />
    </PageContainer>
  );
};

export default MenuManagement;
