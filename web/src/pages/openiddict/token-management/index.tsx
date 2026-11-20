import { ClearOutlined, StopOutlined } from '@ant-design/icons';
import {
  ModalForm,
  PageContainer,
  type ProColumns,
  ProFormSelect,
  ProFormText,
  QueryFilter,
} from '@ant-design/pro-components';
import { useAccess } from '@umijs/max';
import { AgGridReact } from 'ag-grid-react';
import {
  App,
  Button,
  Button as GridButton,
  Space as GridControls,
  Popconfirm,
  Space,
  Tabs,
  Tag,
  theme,
} from 'antd';
import React, { useState } from 'react';
import type { GridQuery } from '@/abp/gridQuery';
import { getOpenIddictApplications } from '@/abp/openIddictApplications';
import {
  type OpenIddictAuthorizationDto,
  type OpenIddictTokenDto,
  pruneOpenIddictTokens,
  queryOpenIddictAuthorizations,
  queryOpenIddictTokens,
  revokeOpenIddictAuthorization,
  revokeOpenIddictToken,
  revokeOpenIddictTokensBySubject,
} from '@/abp/openIddictTokens';
import {
  toGridColumns,
  useSearchableRemoteGrid,
} from '@/components/remoteGrid';

const STATUS_COLORS: Record<string, string> = {
  valid: 'success',
  active: 'success',
  revoked: 'error',
  rejected: 'error',
  redeemed: 'default',
  expired: 'warning',
  inactive: 'warning',
};

const statusTag = (status?: string | null) =>
  status ? <Tag color={STATUS_COLORS[status] ?? 'default'}>{status}</Tag> : '-';

const TokenManagement: React.FC = () => {
  const { message } = App.useApp();
  const { token } = theme.useToken();
  const access = useAccess();
  const [revokeOpen, setRevokeOpen] = useState(false);
  const [filter, setFilter] = useState<{
    subject?: string;
    applicationId?: string;
    type?: string;
    status?: string;
  }>({});
  const [applications, setApplications] = useState<
    { value: string; label: string }[]
  >([]);

  React.useEffect(() => {
    if (!access.canManageOpenIddictApplications) return;
    getOpenIddictApplications({ current: 1, pageSize: 100 })
      .then((res) =>
        setApplications(
          (res.items ?? []).map((x) => ({
            value: x.id,
            label: x.clientId ?? x.id,
          })),
        ),
      )
      .catch(() => undefined);
  }, [access.canManageOpenIddictApplications]);

  const tokenColumns: ProColumns<OpenIddictTokenDto>[] = [
    { title: '应用', dataIndex: 'applicationClientId', ellipsis: true },
    {
      title: '类型',
      dataIndex: 'type',
      width: 130,
      renderText: (v) =>
        v === 'refresh_token' ? (
          <Tag color="processing">refresh_token</Tag>
        ) : (
          (v ?? '-')
        ),
    },
    {
      title: '状态',
      dataIndex: 'status',
      width: 100,
      render: (_, record) => statusTag(record.status),
    },
    { title: '用户', dataIndex: 'subject', ellipsis: true, copyable: true },
    {
      title: '签发时间',
      dataIndex: 'creationDate',
      width: 165,
      renderText: (v) => (v ? new Date(v).toLocaleString() : '-'),
    },
    {
      title: '过期时间',
      dataIndex: 'expirationDate',
      width: 165,
      renderText: (v) => (v ? new Date(v).toLocaleString() : '-'),
    },
    {
      title: '操作',
      valueType: 'option',
      width: 90,
      render: (_, record) =>
        record.status !== 'revoked' && access.canRevokeTokens
          ? [
              <Popconfirm
                key="revoke"
                title="确认吊销该令牌？"
                description="刷新/引用令牌立即失效；自包含访问令牌按 OAuth2 语义到期前仍有效。"
                onConfirm={async () => {
                  await revokeOpenIddictToken(record.id);
                  message.success('已吊销');
                  tokenGrid.refresh();
                }}
              >
                <a style={{ color: token.colorError }}>吊销</a>
              </Popconfirm>,
            ]
          : ['-'],
    },
  ];

  const authColumns: ProColumns<OpenIddictAuthorizationDto>[] = [
    { title: '应用', dataIndex: 'applicationClientId', ellipsis: true },
    { title: '用户', dataIndex: 'subject', ellipsis: true, copyable: true },
    {
      title: '状态',
      dataIndex: 'status',
      width: 100,
      render: (_, record) => statusTag(record.status),
    },
    { title: 'Scopes', dataIndex: 'scopes', ellipsis: true },
    {
      title: '授权时间',
      dataIndex: 'creationDate',
      width: 165,
      renderText: (v) => (v ? new Date(v).toLocaleString() : '-'),
    },
    {
      title: '操作',
      valueType: 'option',
      width: 90,
      render: (_, record) =>
        record.status !== 'revoked' && access.canRevokeTokens
          ? [
              <Popconfirm
                key="revoke"
                title="确认吊销该授权？"
                description="会连带吊销该授权下的全部令牌。"
                onConfirm={async () => {
                  await revokeOpenIddictAuthorization(record.id);
                  message.success('已吊销');
                  authGrid.refresh();
                  tokenGrid.refresh();
                }}
              >
                <a style={{ color: token.colorError }}>吊销</a>
              </Popconfirm>,
            ]
          : ['-'],
    },
  ];

  const sharedFilter = (
    <QueryFilter
      layout="inline"
      onFinish={async (values) => {
        setFilter(values as typeof filter);
      }}
    >
      <ProFormText name="subject" label="用户 Id" placeholder="subject" />
      {access.canManageOpenIddictApplications && (
        <ProFormSelect
          name="applicationId"
          label="应用"
          placeholder="全部"
          options={applications}
          fieldProps={{ showSearch: true, optionFilterProp: 'label' }}
        />
      )}
    </QueryFilter>
  );

  /** 页头标题行已全局隐藏，页面级操作挂在 Tabs 页签栏右侧（两个 tab 共用） */
  const tabExtra = (
    <Space>
      {access.canRevokeTokens ? (
        <Button
          key="by-subject"
          icon={<StopOutlined />}
          onClick={() => setRevokeOpen(true)}
        >
          按用户吊销
        </Button>
      ) : null}
      {access.canPruneTokens ? (
        <Popconfirm
          key="prune"
          title="清理全部过期令牌与授权？"
          onConfirm={async () => {
            const result = await pruneOpenIddictTokens();
            message.success(
              `已清理 ${result.prunedTokens} 个令牌、${result.prunedAuthorizations} 个授权`,
            );
            tokenGrid.refresh();
            authGrid.refresh();
          }}
        >
          <Button icon={<ClearOutlined />}>清理过期</Button>
        </Popconfirm>
      ) : null}
    </Space>
  );

  const tokenGrid = useSearchableRemoteGrid<OpenIddictTokenDto>({
    columnDefs: toGridColumns<OpenIddictTokenDto>(tokenColumns, {
      pro: true,
      kinds: {},
    }),
    ariaLabel: '令牌',
    getRowId: ({ data }) => String(data.id),
    pageSize: 10,
    query: React.useCallback(
      (input: GridQuery, signal: AbortSignal) =>
        queryOpenIddictTokens(filter, input, signal),
      [filter],
    ),
  });
  const authGrid = useSearchableRemoteGrid<OpenIddictAuthorizationDto>({
    columnDefs: toGridColumns<OpenIddictAuthorizationDto>(authColumns, {
      pro: true,
      kinds: {},
    }),
    ariaLabel: '授权',
    getRowId: ({ data }) => String(data.id),
    pageSize: 10,
    query: React.useCallback(
      (input: GridQuery, signal: AbortSignal) =>
        queryOpenIddictAuthorizations(filter, input, signal),
      [filter],
    ),
  });

  return (
    <PageContainer>
      {/* 校验失败返回 false 保持弹窗开启，不会像 modal.confirm 那样被直接关掉 */}
      <ModalForm<{ subject: string }>
        title="按用户吊销全部令牌"
        width={480}
        open={revokeOpen}
        onOpenChange={setRevokeOpen}
        modalProps={{ destroyOnHidden: true }}
        initialValues={{ subject: '' }}
        onFinish={async (values) => {
          const subject = (values.subject ?? '').trim();
          if (!subject) {
            message.warning('请输入用户 Id');
            return false;
          }
          const count = await revokeOpenIddictTokensBySubject(subject);
          message.success(`已吊销 ${count} 个令牌`);
          tokenGrid.refresh();
          authGrid.refresh();
          return true;
        }}
      >
        <div style={{ marginBottom: 8, color: 'rgba(0,0,0,0.45)' }}>
          该用户在所有应用下的令牌都会被吊销（等效强制其在所有客户端重新登录）。
        </div>
        <ProFormText
          name="subject"
          label="用户 Id（subject）"
          placeholder="用户 Id（subject）"
          rules={[{ required: true, message: '请输入用户 Id' }]}
        />
      </ModalForm>
      {sharedFilter}
      <Tabs
        tabBarExtraContent={tabExtra}
        items={[
          {
            key: 'tokens',
            label: '令牌',
            children: (
              <>
                <GridControls wrap style={{ marginBottom: 8 }}>
                  {tokenGrid.searchBox}
                  {tokenGrid.hasFilters && (
                    <GridButton onClick={tokenGrid.clearFilters}>
                      清空筛选
                    </GridButton>
                  )}
                </GridControls>
                <AgGridReact<OpenIddictTokenDto> {...tokenGrid.gridProps} />
              </>
            ),
          },
          {
            key: 'authorizations',
            label: '授权',
            children: (
              <>
                <GridControls wrap style={{ marginBottom: 8 }}>
                  {authGrid.searchBox}
                  {authGrid.hasFilters && (
                    <GridButton onClick={authGrid.clearFilters}>
                      清空筛选
                    </GridButton>
                  )}
                </GridControls>
                <AgGridReact<OpenIddictAuthorizationDto>
                  {...authGrid.gridProps}
                />
              </>
            ),
          },
        ]}
      />
    </PageContainer>
  );
};

export default TokenManagement;
