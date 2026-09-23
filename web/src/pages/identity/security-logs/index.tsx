import { PageContainer, ProTable } from '@ant-design/pro-components';
import type { ProColumns } from '@ant-design/pro-components';
import React from 'react';
import { getSecurityLogs, type SecurityLogDto } from '@/abp/identityAdmin';

const SecurityLogsPage: React.FC = () => {
  // 后端 GetSecurityLogListInput 支持时间/应用/身份/操作/用户/客户端全量筛选，前端对齐
  //（原实现只放开了 userName/action 两个筛选，后端能力被前端浪费）
  const columns: ProColumns<SecurityLogDto>[] = [
    {
      title: '时间',
      dataIndex: 'creationTime',
      valueType: 'dateTime',
      search: false,
    },
    {
      title: '时间范围',
      dataIndex: 'creationTimeRange',
      valueType: 'dateTimeRange',
      hideInTable: true,
      search: {
        transform: (value: string[]) => ({
          startTime: value?.[0],
          endTime: value?.[1],
        }),
      },
    },
    { title: '操作', dataIndex: 'action' },
    { title: '用户', dataIndex: 'userName' },
    { title: '应用', dataIndex: 'applicationName' },
    { title: '身份', dataIndex: 'identity' },
    { title: '客户端', dataIndex: 'clientId' },
    { title: 'IP', dataIndex: 'clientIpAddress', search: false },
  ];

  return (
    <PageContainer>
      <ProTable<SecurityLogDto>
        rowKey="id"
        columns={columns}
        search={{ labelWidth: 'auto' }}
        request={async (params) => {
          const result = await getSecurityLogs({
            current: params.current,
            pageSize: params.pageSize,
            userName: params.userName,
            action: params.action,
            applicationName: params.applicationName,
            identity: params.identity,
            clientId: params.clientId,
            startTime: params.startTime,
            endTime: params.endTime,
          });
          return {
            data: result.items,
            total: result.totalCount,
            success: true,
          };
        }}
      />
    </PageContainer>
  );
};

export default SecurityLogsPage;
