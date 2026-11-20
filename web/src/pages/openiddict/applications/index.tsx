import { PageContainer } from '@ant-design/pro-components';
import { useAccess } from '@umijs/max';
import { AgGridReact } from 'ag-grid-react';
import {
  App,
  Button,
  Button as GridButton,
  Space as GridControls,
  Popconfirm,
} from 'antd';
import React, { useEffect, useState } from 'react';
import {
  deleteOpenIddictApplication,
  getOpenIddictScopeAll,
  type OpenIddictApplicationDto,
  type OpenIddictScopeLookupDto,
  queryOpenIddictApplications,
} from '@/abp/openIddictApplications';
import PermissionModal from '@/components/PermissionModal';
import {
  toGridColumns,
  useSearchableRemoteGrid,
} from '@/components/remoteGrid';
import AppFormModal from './components/AppFormModal';
import GenerateTokenModal from './components/GenerateTokenModal';
import TokenLifetimeDrawer from './components/TokenLifetimeDrawer';

const ApplicationsPage: React.FC = () => {
  const { message } = App.useApp();
  const access = useAccess();
  const [permissionKey, setPermissionKey] = useState<string>();
  const [scopeOptions, setScopeOptions] = useState<OpenIddictScopeLookupDto[]>(
    [],
  );
  const canManagePermissions = access.canManageOpenIddictApplicationPermissions;
  const canCreateApplication =
    access.canCreateOpenIddictApplication && canManagePermissions;
  const canEditApplication =
    access.canUpdateOpenIddictApplication && canManagePermissions;

  useEffect(() => {
    if (
      !access.canManageOpenIddictScopes ||
      (!canCreateApplication && !canEditApplication)
    )
      return;
    getOpenIddictScopeAll()
      .then((result) => setScopeOptions(result.items ?? []))
      .catch(() => message.warning('scope 列表加载失败'));
  }, [
    access.canManageOpenIddictScopes,
    canCreateApplication,
    canEditApplication,
    message,
  ]);

  const renderActions = (record: OpenIddictApplicationDto) => {
    const actions: React.ReactNode[] = [];

    if (canEditApplication) {
      actions.push(
        <AppFormModal
          key="edit"
          title="编辑应用"
          trigger={<a>编辑</a>}
          isEdit
          record={record}
          scopeOptions={scopeOptions}
          onSuccess={() => grid.refresh()}
        />,
      );
    }

    // 四条件前后端都判：权限 + Confidential + 已启用 CC flow + 请求 scope 全部分配
    // （scope 条件在弹窗里用「只能从已分配 scopes 中选择」镜像）
    if (
      access.canGenerateOpenIddictAccessToken &&
      record.clientType === 'confidential' &&
      record.allowClientCredentialsFlow
    ) {
      actions.push(
        <GenerateTokenModal
          key="token"
          clientId={record.clientId}
          applicationId={record.id}
          assignedScopes={record.scopes ?? []}
        />,
      );
    }

    actions.push(
      <TokenLifetimeDrawer
        key="lifetime"
        applicationId={record.id}
        clientId={record.clientId}
        canUpdate={access.canUpdateOpenIddictApplication}
      />,
      canManagePermissions && record.clientId ? (
        <a
          key="perms"
          onClick={() => setPermissionKey(record.clientId ?? undefined)}
        >
          权限
        </a>
      ) : null,
    );

    if (access.canDeleteOpenIddictApplication) {
      actions.push(
        <Popconfirm
          key="delete"
          title="确认删除？"
          onConfirm={async () => {
            await deleteOpenIddictApplication(record.id);
            message.success('已删除');
            grid.refresh();
          }}
        >
          <a>删除</a>
        </Popconfirm>,
      );
    }
    return actions;
  };

  const grid = useSearchableRemoteGrid<OpenIddictApplicationDto>({
    columnDefs: toGridColumns<OpenIddictApplicationDto>(
      [
        { title: 'Client Id', dataIndex: 'clientId' },
        { title: '显示名', dataIndex: 'displayName' },
        { title: '类型', dataIndex: 'clientType' },
        { title: '同意', dataIndex: 'consentType' },
        {
          title: '操作',
          valueType: 'option',
          render: (_, record) => renderActions(record),
        },
      ],
      { pro: true, kinds: {} },
    ),
    ariaLabel: '应用',
    getRowId: ({ data }) => String(data.id),
    pageSize: 10,
    query: queryOpenIddictApplications,
  });

  return (
    <PageContainer>
      <GridControls wrap style={{ marginBottom: 8 }}>
        {grid.searchBox}
        {grid.hasFilters && (
          <GridButton onClick={grid.clearFilters}>清空筛选</GridButton>
        )}{' '}
        {canCreateApplication
          ? [
              <AppFormModal
                key="create"
                title="新建应用"
                trigger={<Button type="primary">新建</Button>}
                isEdit={false}
                scopeOptions={scopeOptions}
                onSuccess={() => grid.refresh()}
              />,
            ]
          : []}
      </GridControls>
      <AgGridReact<OpenIddictApplicationDto> {...grid.gridProps} />

      <PermissionModal
        open={!!permissionKey}
        title={`权限 - ${permissionKey}`}
        providerName="C"
        providerKey={permissionKey}
        onClose={() => setPermissionKey(undefined)}
      />
    </PageContainer>
  );
};

export default ApplicationsPage;
