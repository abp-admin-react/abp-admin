import { PageContainer, ProDescriptions } from '@ant-design/pro-components';
import { useAccess, useSearchParams } from '@umijs/max';
import type {
  GetRowIdParams,
  GridState,
  ICellRendererParams,
} from 'ag-grid-community';
import { AgGridReact } from 'ag-grid-react';
import { Button, Drawer, Input, Space, Tag, Typography } from 'antd';
import { useCallback, useMemo, useState } from 'react';
import type { GridQuery } from '@/abp/gridQuery';
import { queryOperationLogs, type OperationLogDto } from '@/abp/operationLogs';
import CorrelationIdText from '@/components/CorrelationIdText';
import { type RemoteColumn, useRemoteGrid } from '@/components/remoteGrid';

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

const logRowId = ({ data }: GetRowIdParams<OperationLogDto>) => data.id;

const OperationLogsPage: React.FC = () => {
  const access = useAccess();
  const [detail, setDetail] = useState<OperationLogDto>();
  const [searchParams] = useSearchParams();
  const [keyword, setKeyword] = useState('');
  // 关联 ID 是深链的初值。用户清除列筛选后可继续查询其他关联记录。
  const [initialState] = useState<GridState>(() => {
    const id = searchParams.get('correlationId');
    return id
      ? {
          filter: {
            filterModel: {
              correlationId: { filterType: 'text', type: 'equals', filter: id },
            },
          },
        }
      : {};
  });
  const load = useCallback(
    (input: GridQuery, signal: AbortSignal) =>
      queryOperationLogs(keyword ? { ...input, keyword } : input, signal),
    [keyword],
  );
  const columns = useMemo<RemoteColumn<OperationLogDto>[]>(
    () => [
      {
        field: 'executionTime',
        headerName: '时间（UTC）',
        kind: 'date',
        width: 192,
        initialSort: 'desc',
        valueFormatter: ({ value }) =>
          value
            ? new Date(value).toLocaleString('zh-CN', {
                timeZone: 'UTC',
                hour12: false,
              })
            : '',
      },
      { field: 'userName', headerName: '用户', width: 150 },
      { field: 'type', headerName: '模块', width: 140 },
      { field: 'subType', headerName: '操作名', width: 150 },
      {
        field: 'bizId',
        headerName: '业务编号',
        width: 180,
        cellRenderer: ({ value }: ICellRendererParams<OperationLogDto>) =>
          value && <Typography.Text copyable>{value}</Typography.Text>,
      },
      { field: 'action', headerName: '明细', minWidth: 170, flex: 1 },
      {
        field: 'correlationId',
        headerName: '关联 ID',
        kind: 'identifier',
        width: access.canManageAuditLogs ? 440 : 320,
        cellRenderer: ({ value }: ICellRendererParams<OperationLogDto>) => (
          <CorrelationIdText
            value={value}
            linkTo={
              access.canManageAuditLogs
                ? '/administration/audit-logs'
                : undefined
            }
            linkText="查关联审计日志"
          />
        ),
      },
      {
        field: 'success',
        headerName: '结果',
        kind: 'boolean',
        booleanLabels: { true: '成功', false: '失败' },
        width: 110,
        cellRenderer: ({ data }: ICellRendererParams<OperationLogDto>) =>
          data && (
            <Tag color={data.success ? 'success' : 'error'}>
              {data.success ? '成功' : '失败'}
            </Tag>
          ),
      },
      {
        field: 'duration',
        headerName: '耗时（ms）',
        kind: 'number',
        width: 140,
      },
      {
        colId: 'details',
        headerName: '操作',
        kind: 'display',
        width: 100,
        cellRenderer: ({ data }: ICellRendererParams<OperationLogDto>) =>
          data && (
            <Button type="link" size="small" onClick={() => setDetail(data)}>
              详情
            </Button>
          ),
      },
    ],
    [access.canManageAuditLogs],
  );
  const grid = useRemoteGrid({
    columnDefs: columns,
    query: load,
    getRowId: logRowId,
    initialState,
    ariaLabel: '操作日志',
  });

  return (
    <PageContainer>
      <Space orientation="vertical" style={{ width: '100%' }}>
        <Input.Search
          aria-label="跨列搜索操作日志"
          placeholder="搜索操作名、明细、业务编号或用户名"
          style={{ width: 'min(360px, 100%)' }}
          allowClear
          enterButton="搜索"
          onSearch={(value) => setKeyword(value.trim())}
        />
        <AgGridReact<OperationLogDto> {...grid} />
      </Space>
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
                  linkTo={
                    access.canManageAuditLogs
                      ? '/administration/audit-logs'
                      : undefined
                  }
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
