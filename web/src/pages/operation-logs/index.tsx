import {
  PageContainer,
  ProDescriptions,
  ProTable,
} from '@ant-design/pro-components';
import { useSearchParams } from '@umijs/max';
import { Drawer, Tag, Typography } from 'antd';
import React, { useState } from 'react';
import { getOperationLogs, type OperationLogDto } from '@/abp/operationLogs';
import CorrelationIdText from '@/components/CorrelationIdText';

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
      <ProTable<OperationLogDto>
        rowKey="id"
        form={{
          initialValues: { correlationId: initialCorrelationId },
        }}
        columns={[
          {
            title: '时间',
            dataIndex: 'executionTime',
            valueType: 'dateTime',
            width: 160,
            search: false,
          },
          {
            title: '时间范围',
            dataIndex: 'dateTimeRange',
            valueType: 'dateTimeRange',
            hideInTable: true,
            search: {
              transform: (value: string[]) => ({
                StartTime: value?.[0],
                EndTime: value?.[1],
              }),
            },
          },
          { title: '用户', dataIndex: 'userName', width: 120, search: false },
          { title: '模块', dataIndex: 'type', width: 100 },
          { title: '操作', dataIndex: 'subType', width: 130 },
          {
            title: '业务编号',
            dataIndex: 'bizId',
            width: 120,
            copyable: true,
            ellipsis: true,
            search: false,
          },
          {
            title: '内容',
            dataIndex: 'filter',
            hideInTable: true,
            fieldProps: { placeholder: '匹配操作名/内容/业务编号/用户名' },
          },
          {
            title: '关联 ID',
            dataIndex: 'correlationId',
            hideInTable: true,
            fieldProps: { placeholder: '与审计日志串联的关联 ID' },
          },
          {
            title: '明细',
            dataIndex: 'action',
            ellipsis: true,
            search: false,
          },
          {
            title: '结果',
            dataIndex: 'success',
            width: 80,
            valueType: 'select',
            fieldProps: {
              options: [
                { label: '成功', value: 'true' },
                { label: '失败', value: 'false' },
              ],
            },
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
            search: false,
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
        request={async (params) => {
          const result = await getOperationLogs({
            current: params.current,
            pageSize: params.pageSize,
            Filter: params.filter,
            Type: params.type,
            SubType: params.subType,
            Success:
              params.success === undefined || params.success === null
                ? undefined
                : params.success === 'true',
            CorrelationId: params.correlationId,
            StartTime: (params as Record<string, unknown>).StartTime as
              | string
              | undefined,
            EndTime: (params as Record<string, unknown>).EndTime as
              | string
              | undefined,
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
