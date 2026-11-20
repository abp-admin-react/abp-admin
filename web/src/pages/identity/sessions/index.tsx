import {
  ModalForm,
  PageContainer,
  type ProColumns,
  ProFormSelect,
  ProFormText,
} from '@ant-design/pro-components';
import { useAccess } from '@umijs/max';
import { AgGridReact } from 'ag-grid-react';
import {
  App,
  Button,
  Button as GridButton,
  Space as GridControls,
  Popconfirm,
} from 'antd';
import React from 'react';
import { getUsers } from '@/abp/identity';
import {
  type IdentitySessionDto,
  querySessions,
  revokeAllSessionsByUser,
  revokeSession,
} from '@/abp/identityAdmin';
import {
  toGridColumns,
  useSearchableRemoteGrid,
} from '@/components/remoteGrid';

const SessionsPage: React.FC = () => {
  // App.useApp() 消费 ConfigProvider 主题上下文，替代不推荐的静态 message（重构报告问题 20）
  const { message } = App.useApp();
  const access = useAccess();

  const columns: ProColumns<IdentitySessionDto>[] = [
    {
      title: '用户 Id',
      dataIndex: 'userId',
      copyable: true,
      ellipsis: true,
    },
    { title: '设备', dataIndex: 'device' },
    { title: '客户端', dataIndex: 'clientId' },
    {
      title: '会话 Id',
      dataIndex: 'sessionId',
      search: false,
      ellipsis: true,
      copyable: true,
    },
    { title: 'IP', dataIndex: 'ipAddresses', search: false },
    {
      // 归属地按最近 IP 读时解析（后端批量+缓存）；内网/回环显示「内网IP」，解析不了留空
      title: '归属地',
      dataIndex: 'ipRegion',
      search: false,
      render: (_, record) => record.ipRegion ?? '—',
    },
    {
      title: '登录时间',
      dataIndex: 'signedIn',
      valueType: 'dateTime',
      search: false,
    },
    {
      title: '最近访问',
      dataIndex: 'lastAccessed',
      valueType: 'dateTime',
      search: false,
    },
    // 无 Revoke 权限时整列不渲染：空数组会留下无内容的「操作」列头（视觉噪音）
    ...(access.canRevokeSessions
      ? ([
          {
            title: '操作',
            valueType: 'option',
            render: (_, record) => [
              <Popconfirm
                key="revoke"
                title="撤销该会话？"
                description="撤销后该会话下次请求即被强制下线。"
                onConfirm={async () => {
                  await revokeSession(record.id);
                  message.success('已撤销');
                  grid.refresh();
                }}
              >
                <a>撤销</a>
              </Popconfirm>,
            ],
          },
        ] as ProColumns<IdentitySessionDto>[])
      : []),
  ];

  const grid = useSearchableRemoteGrid<IdentitySessionDto>({
    columnDefs: toGridColumns<IdentitySessionDto>(columns, {
      pro: true,
      kinds: { userId: 'uuid', ipRegion: 'display' },
    }),
    ariaLabel: '在线会话',
    getRowId: ({ data }) => String(data.id),
    pageSize: 10,
    query: querySessions,
  });

  return (
    <PageContainer>
      <GridControls wrap style={{ marginBottom: 8 }}>
        {grid.searchBox}
        {grid.hasFilters && (
          <GridButton onClick={grid.clearFilters}>清空筛选</GridButton>
        )}{' '}
        {access.canRevokeSessions
          ? [
              <ModalForm
                key="revoke-all"
                title="按用户吊销全部会话"
                trigger={<Button danger>按用户吊销全部</Button>}
                width={480}
                onFinish={async (values) => {
                  await revokeAllSessionsByUser(values.userId);
                  message.success('已吊销该用户全部会话');
                  grid.refresh();
                  return true;
                }}
              >
                {access.canManageUsers ? (
                  <ProFormSelect
                    name="userId"
                    label="用户"
                    showSearch
                    debounceTime={300}
                    rules={[{ required: true, message: '请选择用户' }]}
                    request={async ({ keyWords }) => {
                      const result = await getUsers({
                        current: 1,
                        pageSize: 20,
                        filter: keyWords,
                      });
                      return (result.items || []).map((item) => ({
                        label: `${item.userName}${
                          item.email ? ` (${item.email})` : ''
                        }`,
                        value: item.id,
                      }));
                    }}
                  />
                ) : (
                  <ProFormText
                    name="userId"
                    label="用户 ID"
                    placeholder="可从会话表复制完整的用户 ID"
                    rules={[
                      { required: true, message: '请输入用户 ID' },
                      {
                        pattern:
                          /^[0-9a-f]{8}(?:-[0-9a-f]{4}){3}-[0-9a-f]{12}$/i,
                        message: '请输入带连字符的完整用户 ID',
                      },
                    ]}
                    fieldProps={{
                      autoCapitalize: 'none',
                      autoCorrect: 'off',
                      spellCheck: false,
                    }}
                  />
                )}
              </ModalForm>,
            ]
          : undefined}
      </GridControls>
      <AgGridReact<IdentitySessionDto> {...grid.gridProps} />
    </PageContainer>
  );
};

export default SessionsPage;
