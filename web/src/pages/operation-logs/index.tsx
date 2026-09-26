import { PageContainer, ProDescriptions } from '@ant-design/pro-components';
import { useSearchParams } from '@umijs/max';
import { Drawer, Tag, Typography } from 'antd';
import dayjs, { type Dayjs } from 'dayjs';
import React, { useState } from 'react';
import { getOperationLogs, type OperationLogDto } from '@/abp/operationLogs';
import AutoHeightProTable from '@/components/AutoHeightProTable';
import CorrelationIdText from '@/components/CorrelationIdText';
import {
  dateRangeFilter,
  enumFilters,
  firstFilterValue,
  textFilter,
} from '@/components/tableColumnFilters';
import { toDayEnd, toDayStart } from '@/utils/format';

const formatExtra = (extra?: string | null) => {
  if (!extra) {
    return undefined;
  }
  try {
    return JSON.stringify(JSON.parse(extra), null, 2);
  } catch {
    return extra;
  }
};

const OperationLogsPage: React.FC = () => {
  const [detail, setDetail] = useState<OperationLogDto>();
  // 支持从审计日志详情跳转过来（?correlationId=xxx 串联同一次请求的两类日志）。
  // 只作表单初始值：用户清空筛选后不应被 URL 初始值粘住（审查修复）
  const [searchParams] = useSearchParams();
  const initialCorrelationId = searchParams.get('correlationId') ?? undefined;

  return (
    <PageContainer>
      <AutoHeightProTable<OperationLogDto>
        rowKey="id"
        search={false}
        columns={[
          {
            title: '时间',
            dataIndex: 'executionTime',
            valueType: 'dateTime',
            width: 160,
            ...dateRangeFilter(),
          },
          { title: '用户', dataIndex: 'userName', width: 120 },
          { title: '模块', dataIndex: 'type', width: 100, ...textFilter() },
          { title: '操作', dataIndex: 'subType', width: 130, ...textFilter() },
          {
            title: '业务编号',
            dataIndex: 'bizId',
            width: 120,
            copyable: true,
            ellipsis: true,
          },
          {
            // 原「内容」搜索字段：后端整表匹配（操作名/内容/业务编号/用户名），
            // 挂在明细列头，placeholder 说清口径
            title: '明细',
            dataIndex: 'action',
            ellipsis: true,
            ...textFilter('匹配操作名/内容/业务编号/用户名'),
          },
          {
            // 关联 ID 原为隐藏搜索字段 + URL 深链；落为可见列 + 列头筛选，
            // defaultFilteredValue 作非受控初始值：用户清空后不被 URL 初始值粘住。
            // 服务端筛选标记由 textFilter 工厂携带（见 tableColumnFilters）
            title: '关联 ID',
            dataIndex: 'correlationId',
            width: 130,
            copyable: true,
            ellipsis: true,
            defaultFilteredValue: initialCorrelationId
              ? [initialCorrelationId]
              : undefined,
            ...textFilter('与审计日志串联的关联 ID'),
            render: (_, record) => (
              <CorrelationIdText
                value={record.correlationId}
                linkTo="/administration/audit-logs"
                linkText="查关联审计日志"
              />
            ),
          },
          {
            title: '结果',
            dataIndex: 'success',
            width: 80,
            filterMultiple: false,
            filters: enumFilters({ 成功: 'true', 失败: 'false' }),
            render: (_, record) =>
              record.success ? (
                <Tag color="success">成功</Tag>
              ) : (
                <Tag color="error">失败</Tag>
              ),
          },
          {
            title: '耗时',
            dataIndex: 'duration',
            width: 80,
            render: (_, record) => `${record.duration} ms`,
          },
          {
            title: '操作',
            valueType: 'option',
            width: 70,
            render: (_, record) => [
              <a key="detail" onClick={() => setDetail(record)}>
                详情
              </a>,
            ],
          },
        ]}
        request={async (params, _sorter, filter) => {
          const f = (filter ?? {}) as Record<string, unknown[] | undefined>;
          // 纯日期 RangePicker 的结束日是 00:00:00，后端为 <= 精确比较：
          // 补齐到整天边界，避免结束日整段被排除（共享 toDayStart/toDayEnd 约定）
          const range = f.executionTime ?? [];
          const start = dayjs.isDayjs(range[0])
            ? (range[0] as Dayjs).format('YYYY-MM-DD')
            : undefined;
          const end = dayjs.isDayjs(range[1])
            ? (range[1] as Dayjs).format('YYYY-MM-DD')
            : undefined;
          const result = await getOperationLogs({
            current: params.current,
            pageSize: params.pageSize,
            Filter: firstFilterValue(f, 'action'),
            Type: firstFilterValue(f, 'type'),
            SubType: firstFilterValue(f, 'subType'),
            Success:
              firstFilterValue(f, 'success') === undefined
                ? undefined
                : firstFilterValue(f, 'success') === 'true',
            CorrelationId: firstFilterValue(f, 'correlationId'),
            StartTime: toDayStart(start),
            EndTime: toDayEnd(end),
          });
          return {
            data: result.items,
            total: result.totalCount,
            success: true,
          };
        }}
      />
      <Drawer
        title={detail ? `${detail.type} · ${detail.subType}` : ''}
        open={!!detail}
        onClose={() => setDetail(undefined)}
        size={720}
        destroyOnHidden
      >
        <ProDescriptions<OperationLogDto>
          column={1}
          bordered
          dataSource={detail}
          columns={[
            { title: '时间', dataIndex: 'executionTime' },
            { title: '用户', dataIndex: 'userName' },
            { title: '模块', dataIndex: 'type' },
            { title: '操作名', dataIndex: 'subType' },
            { title: '业务编号', dataIndex: 'bizId' },
            {
              title: '明细',
              dataIndex: 'action',
              render: (_, record) => (
                <Typography.Paragraph style={{ whiteSpace: 'pre-wrap' }}>
                  {record?.action}
                </Typography.Paragraph>
              ),
            },
            {
              title: '失败原因',
              dataIndex: 'errorMessage',
              hideInDescriptions: !detail?.errorMessage,
            },
            {
              title: '附加数据',
              dataIndex: 'extra',
              render: (_, record) => {
                const text = formatExtra(record?.extra);
                return text ? (
                  <Typography.Paragraph>
                    <pre style={{ margin: 0 }}>{text}</pre>
                  </Typography.Paragraph>
                ) : (
                  '-'
                );
              },
            },
            {
              title: '请求',
              dataIndex: 'requestMethod',
              render: (_, record) =>
                record
                  ? `${record.requestMethod ?? ''} ${record.requestUrl ?? ''}`
                  : '',
            },
            {
              title: 'IP',
              dataIndex: 'clientIpAddress',
              render: (_, record) =>
                record
                  ? `${record.clientIpAddress ?? '-'}${record.ipLocation ? `（${record.ipLocation}）` : ''}`
                  : '',
            },
            { title: 'User-Agent', dataIndex: 'userAgent' },
            {
              title: '关联 ID',
              dataIndex: 'correlationId',
              render: (_, record) => (
                <CorrelationIdText
                  value={record?.correlationId}
                  linkTo="/administration/audit-logs"
                  linkText="查关联审计日志"
                />
              ),
            },
            {
              title: '耗时',
              dataIndex: 'duration',
              render: (_, record) => `${record?.duration ?? 0} ms`,
            },
          ]}
        />
      </Drawer>
    </PageContainer>
  );
};

export default OperationLogsPage;
