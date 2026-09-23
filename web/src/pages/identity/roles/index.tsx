import {
  type ActionType,
  ModalForm,
  PageContainer,
  type ProColumns,
  ProFormSwitch,
  ProFormText,
  ProTable,
} from '@ant-design/pro-components';
import { Button, message, Popconfirm, Space } from 'antd';
import React, { useRef, useState } from 'react';
import { useAccess } from '@umijs/max';
import {
  createRole,
  deleteRole,
  getRoles,
  type IdentityRoleDto,
  updateRole,
} from '@/abp/identity';
import ClaimModal from '@/components/ClaimModal';
import DataScopeModal from '@/components/DataScopeModal';
import PermissionModal from '@/components/PermissionModal';

const RolesPage: React.FC = () => {
  const actionRef = useRef<ActionType>(undefined);
  const [permissionTarget, setPermissionTarget] = useState<IdentityRoleDto>();
  const [claimTarget, setClaimTarget] = useState<IdentityRoleDto>();
  const [dataScopeTarget, setDataScopeTarget] = useState<IdentityRoleDto>();
  const access = useAccess();

  const columns: ProColumns<IdentityRoleDto>[] = [
    { title: '名称', dataIndex: 'name' },
    {
      title: '默认',
      dataIndex: 'isDefault',
      search: false,
      valueEnum: { true: { text: '是' }, false: { text: '否' } },
    },
    {
      title: '公开',
      dataIndex: 'isPublic',
      search: false,
      valueEnum: { true: { text: '是' }, false: { text: '否' } },
    },
    {
      title: '静态',
      dataIndex: 'isStatic',
      search: false,
      valueEnum: { true: { text: '是' }, false: { text: '否' } },
    },
    {
      title: '操作',
      valueType: 'option',
      render: (_, record) => [
        !record.isStatic &&
          access.canUpdateRoles && (
            <ModalForm
              key="edit"
              title="编辑角色"
              trigger={<a>编辑</a>}
              initialValues={record}
              onFinish={async (values) => {
                await updateRole(record.id, {
                  name: values.name,
                  isDefault: values.isDefault,
                  isPublic: values.isPublic,
                  concurrencyStamp: record.concurrencyStamp,
                });
                message.success('已更新');
                actionRef.current?.reload();
                return true;
              }}
            >
              <ProFormText
                name="name"
                label="名称"
                rules={[{ required: true, message: '请输入角色名称' }]}
              />
              <Space>
                <ProFormSwitch name="isDefault" label="默认" />
                <ProFormSwitch name="isPublic" label="公开" />
              </Space>
            </ModalForm>
          ),
        access.canUpdateRoles && (
          <a key="perms" onClick={() => setPermissionTarget(record)}>
            权限
          </a>
        ),
        access.canUpdateRoles && (
          <a key="claims" onClick={() => setClaimTarget(record)}>
            声明
          </a>
        ),
        access.canManageRoleDataScopes && (
          <a key="dataScope" onClick={() => setDataScopeTarget(record)}>
            数据范围
          </a>
        ),
        !record.isStatic &&
          access.canDeleteRoles && (
            <Popconfirm
              key="delete"
              title="确认删除该角色？"
              onConfirm={async () => {
                await deleteRole(record.id);
                message.success('已删除');
                actionRef.current?.reload();
              }}
            >
              <a>删除</a>
            </Popconfirm>
          ),
      ],
    },
  ];

  return (
    <PageContainer>
      <ProTable<IdentityRoleDto>
        rowKey="id"
        actionRef={actionRef}
        columns={columns}
        search={{ labelWidth: 'auto' }}
        request={async (params) => {
          const result = await getRoles({
            current: params.current,
            pageSize: params.pageSize,
            filter: params.name,
          });
          return {
            data: result.items,
            total: result.totalCount,
            success: true,
          };
        }}
        toolBarRender={() => [
          <ModalForm
            key="create"
            title="新建角色"
            trigger={<Button type="primary">新建角色</Button>}
            onFinish={async (values) => {
              await createRole(values as { name: string });
              message.success('已创建');
              actionRef.current?.reload();
              return true;
            }}
          >
            <ProFormText
              name="name"
              label="名称"
              rules={[{ required: true, message: '请输入角色名称' }]}
            />
            <Space>
              <ProFormSwitch name="isDefault" label="默认" />
              <ProFormSwitch name="isPublic" label="公开" />
            </Space>
          </ModalForm>,
        ]}
      />
      <PermissionModal
        open={!!permissionTarget}
        title={`权限 - ${permissionTarget?.name || ''}`}
        providerName="R"
        providerKey={permissionTarget?.name}
        onClose={() => setPermissionTarget(undefined)}
      />
      <ClaimModal
        open={!!claimTarget}
        title={`声明 - ${claimTarget?.name || ''}`}
        roleId={claimTarget?.id}
        onClose={() => setClaimTarget(undefined)}
      />
      <DataScopeModal
        open={!!dataScopeTarget}
        title={`数据范围 - ${dataScopeTarget?.name || ''}`}
        roleName={dataScopeTarget?.name}
        onClose={() => setDataScopeTarget(undefined)}
      />
    </PageContainer>
  );
};

export default RolesPage;
