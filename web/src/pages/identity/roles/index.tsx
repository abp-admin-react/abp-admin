import {
  ModalForm,
  PageContainer,
  type ProColumns,
  ProFormSwitch,
  ProFormText,
  ProFormTextArea,
} from '@ant-design/pro-components';
import { useAccess, useModel } from '@umijs/max';
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
import React, { useEffect, useMemo, useState } from 'react';
import {
  createRole,
  deleteRole,
  type IdentityRoleDto,
  queryRoles,
  updateRole,
} from '@/abp/identity';
import { refreshRolePermissionSession } from '@/abp/rolePermissionSession';
import ClaimModal from '@/components/ClaimModal';
import DataScopeModal from '@/components/DataScopeModal';
import { identityOrgHelpHeader } from '@/components/IdentityOrgHelp';
import {
  toGridColumns,
  useSearchableRemoteGrid,
} from '@/components/remoteGrid';
import RolePermissionEditor from './components/RolePermissionEditor';
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
  const { initialState, setInitialState } = useModel('@@initialState');
  const tenant = initialState?.currentTenant;
  const userId = initialState?.currentUser?.userid;
  const sessionKey = JSON.stringify([tenant?.id, userId]);
  const [permissionTarget, setPermissionTarget] = useState<{
    role: IdentityRoleDto;
    sessionKey: string;
  }>();
  const [claimTarget, setClaimTarget] = useState<IdentityRoleDto>();
  const [usersTarget, setUsersTarget] = useState<IdentityRoleDto>();
  const [dataScopeTarget, setDataScopeTarget] = useState<IdentityRoleDto>();
  const access = useAccess();

  useEffect(() => {
    setPermissionTarget((target) =>
      target?.sessionKey === sessionKey ? target : undefined,
    );
  }, [sessionKey]);

  const columnDefs = useMemo(() => {
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
              <Button
                key="perms"
                id={`role-permissions-${record.id}`}
                size="small"
                type="link"
                style={{ paddingInline: 0 }}
                onClick={() =>
                  setPermissionTarget({ role: record, sessionKey })
                }
              >
                功能权限
              </Button>
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
    return toGridColumns<IdentityRoleDto>(columns, { pro: true });
  }, [
    sessionKey,
    modal,
    message,
    access.canUpdateRoles,
    access.canManageClaimTypes,
    access.canManageRoleDataScopes,
    access.canDeleteRoles,
    access.canManageRolePermissions,
  ]);

  const grid = useSearchableRemoteGrid<IdentityRoleDto>({
    columnDefs,
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

      {userId && permissionTarget?.sessionKey === sessionKey && (
        <RolePermissionEditor
          key={permissionTarget.role.id}
          role={permissionTarget.role}
          userId={userId}
          tenantId={tenant?.id ?? undefined}
          tenantName={tenant?.name ?? undefined}
          onClose={() => {
            setPermissionTarget(undefined);
            const triggerId = `role-permissions-${permissionTarget.role.id}`;
            requestAnimationFrame(() =>
              document.getElementById(triggerId)?.focus(),
            );
          }}
          onStaleRole={() => grid.refresh()}
          onSaved={() => {
            void refreshRolePermissionSession(
              permissionTarget.role.name,
              initialState,
              setInitialState,
            ).catch(() =>
              message.warning('当前会话的权限未能刷新，请刷新页面。'),
            );
          }}
        />
      )}
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
