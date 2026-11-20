import {
  ModalForm,
  PageContainer,
  type ProColumns,
  ProFormSwitch,
  ProFormText,
  ProFormTextArea,
} from '@ant-design/pro-components';
import { useAccess } from '@umijs/max';
import { AgGridReact } from 'ag-grid-react';
import {
  App,
  Button,
  Dropdown,
  Button as GridButton,
  Space as GridControls,
  type MenuProps,
  Space,
  Tag,
} from 'antd';
import React, { useState } from 'react';
import {
  createRole,
  deleteRole,
  type IdentityRoleDto,
  queryRoles,
  updateRole,
} from '@/abp/identity';
import ClaimModal from '@/components/ClaimModal';
import DataScopeModal from '@/components/DataScopeModal';
import { identityOrgHelpHeader } from '@/components/IdentityOrgHelp';
import PermissionModal from '@/components/PermissionModal';
import {
  toGridColumns,
  useSearchableRemoteGrid,
} from '@/components/remoteGrid';
import RoleUsersDrawer from './components/RoleUsersDrawer';

const ROLE_REMARK_MAX_LENGTH = 300;

type RoleFormValues = { name: string; isDefault?: boolean; remark?: string };

const RoleForm: React.FC<{
  record?: IdentityRoleDto;
  onSuccess: () => void;
}> = ({ record, onSuccess }) => {
  const { message } = App.useApp();
  const isStatic = record?.isStatic === true;
  const editLabel = isStatic ? '编辑备注' : '编辑角色';
  return (
    <ModalForm<RoleFormValues>
      title={record ? editLabel : '新建角色'}
      trigger={
        record ? (
          <a>{isStatic ? editLabel : '编辑'}</a>
        ) : (
          <Button type="primary">新建角色</Button>
        )
      }
      width={480}
      initialValues={{
        name: record?.name,
        isDefault: record?.isDefault ?? false,
        remark: record?.extraProperties?.Remark ?? '',
      }}
      modalProps={{ destroyOnHidden: true }}
      onFinish={async (values) => {
        const input = {
          name: isStatic ? record.name : values.name,
          isDefault:
            (isStatic ? undefined : values.isDefault) ??
            record?.isDefault ??
            false,
          // 本系统不使用公开角色身份；从普通编辑中隐藏，保留存量属性。
          isPublic: record?.isPublic ?? false,
          extraProperties: { Remark: values.remark?.trim() ?? '' },
        };
        if (record) {
          await updateRole(record.id, {
            ...input,
            concurrencyStamp: record.concurrencyStamp,
          });
        } else {
          await createRole(input);
        }
        message.success(record ? '已更新' : '已创建');
        onSuccess();
        return true;
      }}
    >
      <ProFormText
        name="name"
        label="名称"
        disabled={isStatic}
        rules={[{ required: true, message: '请输入角色名称' }]}
      />
      <ProFormTextArea
        name="remark"
        label="备注"
        fieldProps={{
          maxLength: ROLE_REMARK_MAX_LENGTH,
          autoSize: { minRows: 2, maxRows: 4 },
        }}
      />
      {!isStatic && (
        <ProFormSwitch
          name="isDefault"
          label="注册时默认分配"
          extra="用户自行注册时自动获得此角色。"
        />
      )}
    </ModalForm>
  );
};

const RolesPage: React.FC = () => {
  const { message, modal } = App.useApp();
  const [permissionTarget, setPermissionTarget] = useState<IdentityRoleDto>();
  const [claimTarget, setClaimTarget] = useState<IdentityRoleDto>();
  const [usersTarget, setUsersTarget] = useState<IdentityRoleDto>();
  const [dataScopeTarget, setDataScopeTarget] = useState<IdentityRoleDto>();
  const access = useAccess();

  const columns: ProColumns<IdentityRoleDto>[] = [
    {
      title: '名称',
      dataIndex: 'name',
      width: 240,
      render: (_, record) => (
        <Space>
          <span>{record.name}</span>
          {record.isStatic && <Tag>系统</Tag>}
          {record.isDefault && <Tag>默认</Tag>}
        </Space>
      ),
    },
    {
      title: '备注',
      dataIndex: ['extraProperties', 'Remark'],
      ellipsis: true,
    },
    {
      title: '操作',
      valueType: 'option',
      width: 280,
      render: (_, record) => {
        const items: MenuProps['items'] = [];
        if (access.canUpdateRoles && access.canManageClaimTypes) {
          items.push({
            key: 'claims',
            label: '声明（高级）',
            onClick: () => setClaimTarget(record),
          });
        }
        if (access.canManageRoleDataScopes) {
          items.push({
            key: 'data-scope',
            label: '业务数据范围',
            onClick: () => setDataScopeTarget(record),
          });
        }
        if (!record.isStatic && access.canDeleteRoles) {
          items.push({
            key: 'delete',
            label: '删除角色',
            danger: true,
            onClick: () =>
              modal.confirm({
                title: '确认删除该角色？',
                onOk: async () => {
                  await deleteRole(record.id);
                  message.success('已删除');
                  grid.refresh();
                },
              }),
          });
        }
        return [
          access.canUpdateRoles && (
            <RoleForm
              key="edit"
              record={record}
              onSuccess={() => grid.refresh()}
            />
          ),
          access.canManageRolePermissions && (
            <a key="perms" onClick={() => setPermissionTarget(record)}>
              功能权限
            </a>
          ),
          <a key="users" onClick={() => setUsersTarget(record)}>
            用户
          </a>,
          items.length > 0 && (
            <Dropdown key="more" menu={{ items }} trigger={['click']}>
              <a>更多</a>
            </Dropdown>
          ),
        ];
      },
    },
  ];

  const grid = useSearchableRemoteGrid<IdentityRoleDto>({
    columnDefs: toGridColumns<IdentityRoleDto>(columns, {
      pro: true,
    }),
    ariaLabel: '角色',
    getRowId: ({ data }) => String(data.id),
    pageSize: 10,
    query: queryRoles,
  });

  return (
    <PageContainer {...identityOrgHelpHeader('roles', '角色')}>
      <GridControls wrap style={{ marginBottom: 8 }}>
        {grid.searchBox}
        {grid.hasFilters && (
          <GridButton onClick={grid.clearFilters}>清空筛选</GridButton>
        )}{' '}
        {[
          access.canCreateRoles && (
            <RoleForm key="create" onSuccess={() => grid.refresh()} />
          ),
        ]}
      </GridControls>
      <AgGridReact<IdentityRoleDto> {...grid.gridProps} />

      <PermissionModal
        open={!!permissionTarget}
        title={`功能权限 - ${permissionTarget?.name || ''}`}
        providerName="R"
        providerKey={permissionTarget?.name}
        onClose={() => setPermissionTarget(undefined)}
      />
      {claimTarget && (
        <ClaimModal
          key={claimTarget.id}
          title={`声明 - ${claimTarget.name}`}
          roleId={claimTarget.id}
          onClose={() => setClaimTarget(undefined)}
        />
      )}
      <RoleUsersDrawer
        role={usersTarget}
        onClose={() => setUsersTarget(undefined)}
      />
      <DataScopeModal
        open={!!dataScopeTarget && access.canManageRoleDataScopes}
        title={`业务数据范围 - ${dataScopeTarget?.name || ''}`}
        roleName={dataScopeTarget?.name}
        onClose={() => setDataScopeTarget(undefined)}
      />
    </PageContainer>
  );
};

export default RolesPage;
