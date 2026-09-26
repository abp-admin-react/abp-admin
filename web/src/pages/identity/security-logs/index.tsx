import type { ProColumns } from '@ant-design/pro-components';
import { PageContainer } from '@ant-design/pro-components';
import dayjs, { type Dayjs } from 'dayjs';
import React from 'react';
import { getSecurityLogs, type SecurityLogDto } from '@/abp/identityAdmin';
import AutoHeightProTable from '@/components/AutoHeightProTable';
import {
  dateRangeFilter,
  enumFilters,
  firstFilterValue,
  textFilter,
} from '@/components/tableColumnFilters';
import { toDayEnd, toDayStart } from '@/utils/format';

const SecurityLogsPage: React.FC = () => {
  // 后端 GetSecurityLogListInput 支持时间/应用/身份/操作/用户/客户端全量筛选。
  // 筛选 UI 全部走官网列头 filterDropdown / filters，值经 request 第三参 filter 交给服务端。
  const columns: ProColumns<SecurityLogDto>[] = [
    {
      title: '时间',
      dataIndex: 'creationTime',
      valueType: 'dateTime',
      ...dateRangeFilter(),
    },
    { title: '操作', dataIndex: 'action', ...textFilter('如 LoginSucceeded') },
    { title: '用户', dataIndex: 'userName', ...textFilter() },
    { title: '应用', dataIndex: 'applicationName', ...textFilter() },
    {
      title: '身份',
      dataIndex: 'identity',
      filterMultiple: false,
      filters: enumFilters({ Identity: 'Identity', OpenIddict: 'OpenIddict' }),
    },
    { title: '客户端', dataIndex: 'clientId', ...textFilter() },
    { title: 'IP', dataIndex: 'clientIpAddress' },
  ];

  return (
    <PageContainer>
      <AutoHeightProTable<SecurityLogDto>
        rowKey="id"
        columns={columns}
        search={false}
        request={async (params, _sorter, filter) => {
          const f = (filter ?? {}) as Record<string, unknown[] | undefined>;
          // 纯日期 RangePicker 的结束日是 00:00:00，后端为 <= 精确比较：
          // 补齐到整天边界，避免结束日整段被排除（共享 toDayStart/toDayEnd 约定）
          const range = (f.creationTime ?? []) as unknown[];
          const start = dayjs.isDayjs(range[0])
            ? (range[0] as Dayjs).format('YYYY-MM-DD')
            : undefined;
          const end = dayjs.isDayjs(range[1])
            ? (range[1] as Dayjs).format('YYYY-MM-DD')
            : undefined;
          const result = await getSecurityLogs({
            current: params.current,
            pageSize: params.pageSize,
            userName: firstFilterValue(f, 'userName'),
            action: firstFilterValue(f, 'action'),
            applicationName: firstFilterValue(f, 'applicationName'),
            identity: firstFilterValue(f, 'identity'),
            clientId: firstFilterValue(f, 'clientId'),
            startTime: toDayStart(start),
            endTime: toDayEnd(end),
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
