import {
  PageContainer,
  ProCard,
  ProDescriptions,
} from '@ant-design/pro-components';
import { useModel } from '@umijs/max';
import { Space, Tag } from 'antd';
import React from 'react';

const CurrentSession: React.FC = () => {
  const { initialState } = useModel('@@initialState');
  const currentUser = initialState?.currentUser;
  const tenant = initialState?.currentTenant;
  const policies = initialState?.grantedPolicies ?? {};

  return (
    <PageContainer>
      <ProCard ghost gutter={[0, 16]} direction="column">
        <ProCard title="当前用户" headerBordered colSpan={24}>
          <ProDescriptions
            column={2}
            dataSource={{
              userName: currentUser?.userName,
              name: currentUser?.name,
              email: currentUser?.email,
              userid: currentUser?.userid,
              roles: (currentUser?.roles ?? []).join(', ') || '-',
            }}
            columns={[
              { title: '用户名', dataIndex: 'userName' },
              { title: '姓名', dataIndex: 'name' },
              { title: '邮箱', dataIndex: 'email' },
              { title: '用户 Id', dataIndex: 'userid' },
              { title: '角色', dataIndex: 'roles' },
            ]}
          />
        </ProCard>
        <ProCard title="当前租户" headerBordered colSpan={24}>
          <ProDescriptions
            column={2}
            dataSource={{
              tenantName: tenant?.isAvailable ? tenant.name : 'Host',
              tenantId: tenant?.id || '-',
              isAvailable: tenant?.isAvailable,
            }}
            columns={[
              {
                title: '上下文',
                dataIndex: 'tenantName',
                render: (_, record) =>
                  record.isAvailable ? (
                    <Tag color="blue">{record.tenantName}</Tag>
                  ) : (
                    <Tag>Host</Tag>
                  ),
              },
              { title: '租户 Id', dataIndex: 'tenantId' },
            ]}
          />
        </ProCard>
        <ProCard title="已授权策略（节选）" headerBordered colSpan={24}>
          <Space wrap size={[8, 8]}>
            {Object.keys(policies)
              .filter((key) => policies[key])
              .slice(0, 30)
              .map((key) => (
                <Tag key={key}>{key}</Tag>
              ))}
          </Space>
        </ProCard>
      </ProCard>
    </PageContainer>
  );
};

export default CurrentSession;
