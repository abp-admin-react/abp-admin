import type { ProColumns } from '@ant-design/pro-components';
import { PageContainer } from '@ant-design/pro-components';
import { AgGridReact } from 'ag-grid-react';
import { Button as GridButton, Space as GridControls } from 'antd';
import React from 'react';
import type { SecurityLogDto } from '@/abp/identityAdmin';
import { querySecurityLogs } from '@/abp/identityAdmin';
import {
  toGridColumns,
  useSearchableRemoteGrid,
} from '@/components/remoteGrid';
import FrameworkConfiguration from '@/pages/settings/FrameworkConfiguration';

const SecurityLogsPage: React.FC<{ configurationEntry?: React.ReactNode }> = ({
  configurationEntry,
}) => {
  const columns: ProColumns<SecurityLogDto>[] = [
    {
      title: '时间',
      dataIndex: 'creationTime',
      valueType: 'dateTime',
    },
    { title: '操作', dataIndex: 'action' },
    { title: '用户', dataIndex: 'userName' },
    { title: '应用', dataIndex: 'applicationName' },
    {
      title: '身份',
      dataIndex: 'identity',
      valueEnum: { Identity: 'Identity', OpenIddict: 'OpenIddict' },
    },
    { title: '客户端', dataIndex: 'clientId' },
    { title: 'IP', dataIndex: 'clientIpAddress' },
  ];

  const grid = useSearchableRemoteGrid<SecurityLogDto>({
    columnDefs: toGridColumns<SecurityLogDto>(columns, {
      pro: true,
      kinds: {},
    }),
    ariaLabel: '安全日志',
    getRowId: ({ data }) => String(data.id),
    pageSize: 10,
    query: querySecurityLogs,
  });

  return (
    <PageContainer title="登录安全日志" extra={configurationEntry}>
      <GridControls wrap style={{ marginBottom: 8 }}>
        {grid.searchBox}
        {grid.hasFilters && (
          <GridButton onClick={grid.clearFilters}>清空筛选</GridButton>
        )}
      </GridControls>
      <AgGridReact<SecurityLogDto> {...grid.gridProps} />
    </PageContainer>
  );
};

export default function Page() {
  return (
    <FrameworkConfiguration owner="login">
      {(configurationEntry) => (
        <SecurityLogsPage configurationEntry={configurationEntry} />
      )}
    </FrameworkConfiguration>
  );
}
