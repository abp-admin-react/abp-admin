import { PlusOutlined } from '@ant-design/icons';
import {
  type ActionType,
  ModalForm,
  PageContainer,
  type ProColumns,
  ProFormText,
  ProFormTextArea,
} from '@ant-design/pro-components';
import { useAccess } from '@umijs/max';
import { App, Button, Popconfirm, Space, Tag, Tree } from 'antd';
import type { DataNode } from 'antd/es/tree';
import React, { useRef, useState } from 'react';
import { toMenuIcon as iconOf } from '@/abp/menuIcons';
import type { MenuTreeDto } from '@/abp/menus';
import {
  createTenantPackage,
  deleteTenantPackage,
  getPackageMenuSelection,
  getTenantPackages,
  type TenantPackageDto,
  updatePackageMenuSelection,
  updateTenantPackage,
} from '@/abp/tenantPackages';
import AutoHeightProTable from '@/components/AutoHeightProTable';
import { firstFilterValue, textFilter } from '@/components/tableColumnFilters';

type EditTarget = { mode: 'create' } | { mode: 'edit'; row: TenantPackageDto };

/** 模板菜单树 → antd Tree 数据（勾选键用节点 id）。 */
function toTreeData(nodes: MenuTreeDto[]): DataNode[] {
  return nodes.map((node) => ({
    title: node.title + (node.path ? `（${node.path}）` : ''),
    key: node.id,
    icon: iconOf(node.icon),
    children: toTreeData(node.children),
  }));
}

const TenantPackagePage: React.FC = () => {
  const { message } = App.useApp();
  const access = useAccess();
  const actionRef = useRef<ActionType>(undefined);
  const [editTarget, setEditTarget] = useState<EditTarget>();
  const [menuTarget, setMenuTarget] = useState<TenantPackageDto>();
  const [menuTree, setMenuTree] = useState<DataNode[]>([]);
  const [checkedKeys, setCheckedKeys] = useState<string[]>([]);
  const [menuLoading, setMenuLoading] = useState(false);

  const openMenuModal = async (row: TenantPackageDto) => {
    setMenuTarget(row);
    setMenuLoading(true);
    try {
      const selection = await getPackageMenuSelection(row.id);
      setMenuTree(toTreeData(selection.tree));
      setCheckedKeys(selection.checkedMenuIds);
    } finally {
      setMenuLoading(false);
    }
  };

  const saveMenus = async () => {
    if (!menuTarget) return;
    await updatePackageMenuSelection(menuTarget.id, checkedKeys);
    message.success('套餐菜单已保存');
    setMenuTarget(undefined);
    actionRef.current?.reload();
  };

  const columns: ProColumns<TenantPackageDto>[] = [
    { title: '套餐名', dataIndex: 'name', ...textFilter('按套餐名筛选') },
    { title: '菜单数', dataIndex: 'menuCount', width: 90 },
    { title: '备注', dataIndex: 'remark', ellipsis: true },
    {
      title: '操作',
      valueType: 'option',
      width: 220,
      render: (_, record) =>
        [
          access.canUpdateTenantPackages ? (
            <a
              key="edit"
              onClick={() => setEditTarget({ mode: 'edit', row: record })}
            >
              编辑
            </a>
          ) : null,
          access.canUpdateTenantPackages ? (
            <a key="menus" onClick={() => openMenuModal(record)}>
              菜单配置
            </a>
          ) : null,
          access.canDeleteTenantPackages ? (
            <Popconfirm
              key="delete"
              title="确认删除该套餐？已应用过的租户不受影响。"
              onConfirm={async () => {
                await deleteTenantPackage(record.id);
                message.success('套餐已删除');
                actionRef.current?.reload();
              }}
            >
              <a style={{ color: '#ff4d4f' }}>删除</a>
            </Popconfirm>
          ) : null,
        ].filter(Boolean),
    },
  ];

  return (
    <PageContainer>
      <AutoHeightProTable<TenantPackageDto>
        rowKey="id"
        headerTitle="租户套餐"
        actionRef={actionRef}
        columns={columns}
        request={async (params, _sorter, filter) => {
          const res = await getTenantPackages({
            current: params.current,
            pageSize: params.pageSize,
            filter: firstFilterValue(filter, 'name'),
          });
          return { data: res.items, total: res.totalCount, success: true };
        }}
        search={false}
        pagination={{ defaultPageSize: 10 }}
        toolBarRender={() =>
          [
            access.canCreateTenantPackages ? (
              <Button
                key="create"
                type="primary"
                icon={<PlusOutlined />}
                onClick={() => setEditTarget({ mode: 'create' })}
              >
                新增套餐
              </Button>
            ) : null,
          ].filter(Boolean)
        }
      />

      <ModalForm
        title={
          editTarget?.mode === 'edit'
            ? `编辑套餐 - ${editTarget.row.name}`
            : '新增套餐'
        }
        width={460}
        open={!!editTarget}
        key={
          editTarget?.mode === 'edit' ? `edit-${editTarget.row.id}` : 'create'
        }
        modalProps={{
          destroyOnHidden: true,
          onCancel: () => setEditTarget(undefined),
        }}
        initialValues={
          editTarget?.mode === 'edit'
            ? {
                name: editTarget.row.name,
                remark: editTarget.row.remark ?? undefined,
              }
            : undefined
        }
        onFinish={async (values: { name: string; remark?: string }) => {
          if (editTarget?.mode === 'edit') {
            await updateTenantPackage(editTarget.row.id, values);
            message.success('套餐已更新');
          } else {
            await createTenantPackage(values);
            message.success('套餐已创建，接下来请配置菜单');
          }
          setEditTarget(undefined);
          actionRef.current?.reload();
          return true;
        }}
      >
        <ProFormText
          name="name"
          label="套餐名"
          rules={[{ required: true, message: '请输入套餐名' }]}
        />
        <ProFormTextArea name="remark" label="备注" fieldProps={{ rows: 2 }} />
      </ModalForm>

      <ModalForm
        title={`菜单配置 - ${menuTarget?.name ?? ''}`}
        width={560}
        open={!!menuTarget}
        key={menuTarget?.id}
        loading={menuLoading}
        modalProps={{
          destroyOnHidden: true,
          onCancel: () => setMenuTarget(undefined),
        }}
        onFinish={saveMenus}
        submitter={{ searchConfig: { submitText: '保存' } }}
      >
        <div style={{ marginBottom: 8, color: 'rgba(0,0,0,0.45)' }}>
          勾选的菜单（含其上级目录）会在应用套餐时拷贝给租户；未勾选的整棵分支不下发。
        </div>
        <Tree
          checkable
          showIcon
          treeData={menuTree}
          checkedKeys={checkedKeys}
          onCheck={(keys, info) => {
            // 保存"全选 + 半选"节点 id：半选的父目录也要入库，应用套餐时才能挂上祖先链
            const all = [
              ...(keys as React.Key[]),
              ...((info.halfCheckedKeys ?? []) as React.Key[]),
            ].map(String);
            setCheckedKeys(all);
          }}
          defaultExpandAll
          selectable={false}
          style={{ maxHeight: 420, overflow: 'auto' }}
        />
      </ModalForm>
    </PageContainer>
  );
};

export default TenantPackagePage;
