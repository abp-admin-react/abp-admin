import { type ActionType, PageContainer } from '@ant-design/pro-components';
import { useAccess } from '@umijs/max';
import { App, Button, Popconfirm } from 'antd';
import React, { useEffect, useRef, useState } from 'react';
import {
  deleteOpenIddictApplication,
  getOpenIddictApplications,
  getOpenIddictScopeAll,
  type OpenIddictApplicationDto,
  type OpenIddictScopeLookupDto,
} from '@/abp/openIddictApplications';
import AutoHeightProTable from '@/components/AutoHeightProTable';
import PermissionModal from '@/components/PermissionModal';
import AppFormModal from './components/AppFormModal';
import GenerateTokenModal from './components/GenerateTokenModal';
import TokenLifetimeDrawer from './components/TokenLifetimeDrawer';

const ApplicationsPage: React.FC = () => {
  const { message } = App.useApp();
  const access = useAccess();
  const actionRef = useRef<ActionType>(undefined);
  const [permissionKey, setPermissionKey] = useState<string>();
  const [scopeOptions, setScopeOptions] = useState<OpenIddictScopeLookupDto[]>(
    [],
  );

  useEffect(() => {
    getOpenIddictScopeAll()
      .then((result) => setScopeOptions(result.items ?? []))
      .catch(() => message.warning('scope 列表加载失败'));
  }, []);

  const renderActions = (record: OpenIddictApplicationDto) => {
    const actions: React.ReactNode[] = [];

    if (access.canUpdateOpenIddictApplication) {
      actions.push(
        <AppFormModal
          key="edit"
          title="编辑应用"
          trigger={<a>编辑</a>}
          isEdit
          record={record}
          scopeOptions={scopeOptions}
          onSuccess={() => actionRef.current?.reload()}
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
      />,
      <a
        key="perms"
        onClick={() => setPermissionKey(record.clientId ?? undefined)}
      >
        权限
      </a>,
    );

    if (access.canDeleteOpenIddictApplication) {
      actions.push(
        <Popconfirm
          key="delete"
          title="确认删除？"
          onConfirm={async () => {
            await deleteOpenIddictApplication(record.id);
            message.success('已删除');
            actionRef.current?.reload();
          }}
        >
          <a>删除</a>
        </Popconfirm>,
      );
    }
    return actions;
  };

  return (
    <PageContainer>
      <AutoHeightProTable<OpenIddictApplicationDto>
        rowKey="id"
        actionRef={actionRef}
        search={false}
        columns={[
          { title: 'Client Id', dataIndex: 'clientId' },
          { title: '显示名', dataIndex: 'displayName' },
          { title: '类型', dataIndex: 'clientType' },
          { title: '同意', dataIndex: 'consentType' },
          {
            title: '操作',
            valueType: 'option',
            render: (_, record) => renderActions(record),
          },
        ]}
        request={async (params) => {
          const result = await getOpenIddictApplications(params);
          return {
            data: result.items,
            total: result.totalCount,
            success: true,
          };
        }}
        toolBarRender={() =>
          access.canCreateOpenIddictApplication
            ? [
                <AppFormModal
                  key="create"
                  title="新建应用"
                  trigger={<Button type="primary">新建</Button>}
                  isEdit={false}
                  scopeOptions={scopeOptions}
                  onSuccess={() => actionRef.current?.reload()}
                />,
              ]
            : []
        }
      />
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
