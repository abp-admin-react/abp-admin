import { ClearOutlined, StopOutlined } from '@ant-design/icons';
import {
  type ActionType,
  ModalForm,
  PageContainer,
  type ProColumns,
  ProFormSelect,
  ProFormText,
  QueryFilter,
} from '@ant-design/pro-components';
import { useAccess } from '@umijs/max';
import { App, Button, Popconfirm, Space, Tabs, Tag, theme } from 'antd';
import React, { useRef, useState } from 'react';
import { getOpenIddictApplications } from '@/abp/openIddictApplications';
import {
  getOpenIddictAuthorizations,
  getOpenIddictTokens,
  type OpenIddictAuthorizationDto,
  type OpenIddictTokenDto,
  pruneOpenIddictTokens,
  revokeOpenIddictAuthorization,
  revokeOpenIddictToken,
  revokeOpenIddictTokensBySubject,
} from '@/abp/openIddictTokens';
import AutoHeightProTable from '@/components/AutoHeightProTable';

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
  const tokenTableRef = useRef<ActionType>(undefined);
  const authTableRef = useRef<ActionType>(undefined);
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
  }, []);

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
                  tokenTableRef.current?.reload();
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
                  authTableRef.current?.reload();
                  tokenTableRef.current?.reload();
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
        tokenTableRef.current?.reload();
        authTableRef.current?.reload();
      }}
    >
      <ProFormText name="subject" label="用户 Id" placeholder="subject" />
      <ProFormSelect
        name="applicationId"
        label="应用"
        placeholder="全部"
        options={applications}
        fieldProps={{ showSearch: true, optionFilterProp: 'label' }}
      />
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
            tokenTableRef.current?.reload();
            authTableRef.current?.reload();
          }}
        >
          <Button icon={<ClearOutlined />}>清理过期</Button>
        </Popconfirm>
      ) : null}
    </Space>
  );

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
          tokenTableRef.current?.reload();
          authTableRef.current?.reload();
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
      <Tabs
        tabBarExtraContent={tabExtra}
        items={[
          {
            key: 'tokens',
            label: '令牌',
            children: (
              <>
                {sharedFilter}
                <AutoHeightProTable<OpenIddictTokenDto>
                  rowKey="id"
                  actionRef={tokenTableRef}
                  columns={tokenColumns}
                  search={false}
                  pagination={{ defaultPageSize: 10 }}
                  request={async (params) => {
                    const res = await getOpenIddictTokens({
                      ...params,
                      ...filter,
                    });
                    return {
                      data: res.items,
                      total: res.totalCount,
                      success: true,
                    };
                  }}
                />
              </>
            ),
          },
          {
            key: 'authorizations',
            label: '授权',
            children: (
              <>
                {sharedFilter}
                <AutoHeightProTable<OpenIddictAuthorizationDto>
                  rowKey="id"
                  actionRef={authTableRef}
                  columns={authColumns}
                  search={false}
                  pagination={{ defaultPageSize: 10 }}
                  request={async (params) => {
                    const res = await getOpenIddictAuthorizations({
                      ...params,
                      ...filter,
                    });
                    return {
                      data: res.items,
                      total: res.totalCount,
                      success: true,
                    };
                  }}
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
