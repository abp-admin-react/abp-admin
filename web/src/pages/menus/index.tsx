import { PlusOutlined, QuestionCircleOutlined } from '@ant-design/icons';
import { PageContainer } from '@ant-design/pro-components';
import { useAccess } from '@umijs/max';
import { App, Button, Input, Space, Table, Tooltip } from 'antd';
import React, { useCallback, useEffect, useMemo, useState } from 'react';
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
  type MenuFilters,
  type MenuRow,
  queryMenuTree,
} from './components/menuTree';
import {
  buildPermissionTreeData,
  type MenuEditTarget,
  type TreeSelectNode,
  toMenuTreeSelectData,
} from './components/menuTypes';

const MenuManagement: React.FC = () => {
  const { message } = App.useApp();
  const access = useAccess();

  const [tree, setTree] = useState<MenuTreeDto[]>([]);
  const [loading, setLoading] = useState(false);
  const [draft, setDraft] = useState('');
  const [keyword, setKeyword] = useState('');
  const [filters, setFilters] = useState<MenuFilters>({});
  const [expandedKeys, setExpandedKeys] = useState<React.Key[]>();
  const [filteredKeys, setFilteredKeys] = useState<React.Key[]>();
  const [editTarget, setEditTarget] = useState<MenuEditTarget>();
  const [permissions, setPermissions] = useState<PermissionOptionDto[]>([]);
  const { rows, branchKeys } = useMemo(
    () => queryMenuTree(tree, keyword, filters),
    [tree, keyword, filters],
  );
  const filtering = Boolean(
    keyword || Object.values(filters).some((values) => values?.length),
  );
  // 筛选时展开所有命中路径，并将该视图中的手动展开与平时的展开状态分开保存。
  const expanded = (filtering ? filteredKeys : expandedKeys) ?? branchKeys;
  const setExpanded = filtering ? setFilteredKeys : setExpandedKeys;
  const allExpanded =
    branchKeys.length > 0 && branchKeys.every((key) => expanded.includes(key));

  function search(value: string) {
    const next = value.trim();
    setDraft(next);
    if (next !== keyword) {
      setKeyword(next);
      setFilteredKeys(undefined);
    }
  }

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
      buildMenuColumns(
        {
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
        },
        filters,
      ),
    [access, message, reload, filters],
  );

  return (
    <PageContainer>
      <Space wrap style={{ marginBottom: 8 }}>
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
        <Input.Search
          aria-label="搜索菜单"
          placeholder="搜索名称、路由或权限"
          style={{ width: 'min(320px, 100%)' }}
          maxLength={512}
          allowClear
          value={draft}
          onChange={(event) => setDraft(event.target.value)}
          onSearch={(value, event) => {
            const key = event?.nativeEvent;
            if (
              key instanceof KeyboardEvent &&
              (key.isComposing || key.keyCode === 229)
            )
              return;
            search(value);
          }}
        />
        <Button
          disabled={branchKeys.length === 0}
          onClick={() => setExpanded(allExpanded ? [] : branchKeys)}
        >
          {allExpanded ? '折叠全部' : '展开全部'}
        </Button>
        {filtering && (
          <Button
            onClick={() => {
              search('');
              setFilters({});
              setFilteredKeys(undefined);
            }}
          >
            清除筛选
          </Button>
        )}
        {access.canCreateMenus && (
          <Button
            type="primary"
            icon={<PlusOutlined />}
            onClick={() => setEditTarget({ mode: 'create' })}
          >
            新增顶级菜单
          </Button>
        )}
      </Space>
      <Table<MenuRow>
        aria-label="菜单树"
        rowKey={(row) => row.menu.id}
        columns={columns}
        dataSource={rows}
        loading={loading}
        size="small"
        pagination={false}
        scroll={{ x: 1156 }}
        expandable={{
          expandedRowKeys: expanded,
          onExpandedRowsChange: (keys) => setExpanded([...keys]),
        }}
        onChange={(_page, nextFilters, _sorter, extra) => {
          if (extra.action === 'filter') {
            setFilters(nextFilters);
            setFilteredKeys(undefined);
          }
        }}
      />
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
