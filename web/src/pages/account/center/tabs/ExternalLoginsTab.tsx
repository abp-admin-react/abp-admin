import { AgGridReact } from 'ag-grid-react';
import { App, Button, Space } from 'antd';
import React from 'react';
import {
  linkExternalLogin,
  queryExternalLogins,
  removeExternalLogin,
  type UserLoginDto,
} from '@/abp/accountSecurity';
import {
  toGridColumns,
  useSearchableRemoteGrid,
} from '@/components/remoteGrid';

const LINKABLE_PROVIDERS = ['GitHub', 'Microsoft', 'Weixin', 'Google'];

/** 账号关联 Tab：外部登录绑定管理（自 index.tsx 拆分，见重构报告问题 4） */

const ExternalLoginsTab: React.FC = () => {
  const { message } = App.useApp();

  const grid = useSearchableRemoteGrid<UserLoginDto>({
    columnDefs: toGridColumns<UserLoginDto>(
      [
        { title: '提供方', dataIndex: 'loginProvider', width: 140 },
        { title: '显示名', dataIndex: 'providerDisplayName', width: 180 },
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
                grid.refresh();
              }}
            >
              解绑
            </Button>
          ),
        },
      ],
      { kinds: {} },
    ),
    getRowId: ({ data }) =>
      JSON.stringify([data.loginProvider, data.providerKey]),
    ariaLabel: '外部登录',
    query: queryExternalLogins,
  });

  return (
    <Space orientation="vertical" style={{ width: '100%' }} size="middle">
      <Space wrap>
        {LINKABLE_PROVIDERS.map((provider) => (
          <Button key={provider} onClick={() => linkExternalLogin(provider)}>
            绑定 {provider}
          </Button>
        ))}
      </Space>

      <div
        style={{
          display: 'flex',
          flexWrap: 'wrap',
          alignItems: 'center',
          gap: 8,
          marginBottom: 8,
        }}
      >
        {grid.searchBox}
      </div>
      <AgGridReact<UserLoginDto>
        {...grid.gridProps}
        containerStyle={{ width: '100%', height: 320 }}
      />
    </Space>
  );
};

export default ExternalLoginsTab;
