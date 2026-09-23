import { App, Button, Space, Table } from 'antd';
import React from 'react';
import {
  getExternalLogins,
  linkExternalLogin,
  removeExternalLogin,
  type UserLoginDto,
} from '@/abp/accountSecurity';
import { useAsyncData } from '@/hooks/useAsyncData';

const LINKABLE_PROVIDERS = ['GitHub', 'Microsoft', 'Weixin', 'Google'];

/** 账号关联 Tab：外部登录绑定管理（自 index.tsx 拆分，见重构报告问题 4） */
const ExternalLoginsTab: React.FC = () => {
  const { message } = App.useApp();
  const {
    data: logins,
    loading,
    refresh,
  } = useAsyncData<UserLoginDto[]>(() => getExternalLogins());

  return (
    <Space orientation="vertical" style={{ width: '100%' }} size="middle">
      <Space wrap>
        {LINKABLE_PROVIDERS.map((provider) => (
          <Button key={provider} onClick={() => linkExternalLogin(provider)}>
            绑定 {provider}
          </Button>
        ))}
      </Space>
      <Table<UserLoginDto>
        rowKey={(row) => `${row.loginProvider}:${row.providerKey}`}
        size="small"
        loading={loading}
        dataSource={logins ?? []}
        pagination={false}
        columns={[
          { title: '提供方', dataIndex: 'loginProvider' },
          { title: '显示名', dataIndex: 'providerDisplayName' },
          {
            title: '操作',
            render: (_, record) => (
              <Button
                type="link"
                danger
                size="small"
                onClick={async () => {
                  await removeExternalLogin({
                    loginProvider: record.loginProvider,
                    providerKey: record.providerKey,
                  });
                  message.success('已解绑');
                  await refresh();
                }}
              >
                解绑
              </Button>
            ),
          },
        ]}
      />
    </Space>
  );
};

export default ExternalLoginsTab;
