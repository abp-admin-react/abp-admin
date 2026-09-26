import {
  type ActionType,
  ModalForm,
  PageContainer,
  type ProColumns,
  ProFormSelect,
} from '@ant-design/pro-components';
import { useAccess } from '@umijs/max';
import { App, Button, Popconfirm } from 'antd';
import React, { useRef } from 'react';
import { getUsers } from '@/abp/identity';
import {
  getSessions,
  type IdentitySessionDto,
  revokeAllSessionsByUser,
  revokeSession,
} from '@/abp/identityAdmin';
import AutoHeightProTable from '@/components/AutoHeightProTable';
import { firstFilterValue, textFilter } from '@/components/tableColumnFilters';

const SessionsPage: React.FC = () => {
  const actionRef = useRef<ActionType>(undefined);
  // App.useApp() 消费 ConfigProvider 主题上下文，替代不推荐的静态 message（重构报告问题 20）
  const { message } = App.useApp();
  const access = useAccess();

  const columns: ProColumns<IdentitySessionDto>[] = [
    {
      title: '用户 Id',
      dataIndex: 'userId',
      copyable: true,
      ellipsis: true,
      // 非法 GUID 会被后端绑定拒绝（400）：改在 request 里校验并提示
      ...textFilter('用户 Id（GUID）'),
    },
    { title: '设备', dataIndex: 'device', ...textFilter() },
    { title: '客户端', dataIndex: 'clientId', ...textFilter() },
    {
      title: '会话 Id',
      dataIndex: 'sessionId',
      search: false,
      ellipsis: true,
      copyable: true,
    },
    { title: 'IP', dataIndex: 'ipAddresses', search: false },
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
                  actionRef.current?.reload();
                }}
              >
                <a>撤销</a>
              </Popconfirm>,
            ],
          },
        ] as ProColumns<IdentitySessionDto>[])
      : []),
  ];

  return (
    <PageContainer>
      <AutoHeightProTable<IdentitySessionDto>
        rowKey="id"
        actionRef={actionRef}
        columns={columns}
        search={false}
        request={async (params, _sorter, filter) => {
          const userId = firstFilterValue(filter, 'userId');
          // 非法 GUID 会被后端绑定拒绝（400 报错而非空结果）：前端拦截并明确提示，
          // 避免用户把「输入错误」误读成「无会话」
          if (
            userId &&
            !/^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i.test(
              userId,
            )
          ) {
            message.warning('请输入有效的用户 Id（GUID）');
            return { data: [], total: 0, success: true };
          }
          const result = await getSessions({
            current: params.current,
            pageSize: params.pageSize,
            userId,
            device: firstFilterValue(filter, 'device'),
            clientId: firstFilterValue(filter, 'clientId'),
          });
          return {
            data: result.items,
            total: result.totalCount,
            success: true,
          };
        }}
        toolBarRender={
          access.canRevokeSessions
            ? () => [
                <ModalForm
                  key="revoke-all"
                  title="按用户吊销全部会话"
                  trigger={<Button danger>按用户吊销全部</Button>}
                  width={480}
                  onFinish={async (values) => {
                    await revokeAllSessionsByUser(values.userId);
                    message.success('已吊销该用户全部会话');
                    actionRef.current?.reload();
                    return true;
                  }}
                >
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
                </ModalForm>,
              ]
            : undefined
        }
      />
    </PageContainer>
  );
};

export default SessionsPage;
