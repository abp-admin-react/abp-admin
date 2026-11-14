import { PageContainer } from '@ant-design/pro-components';
import { useAccess } from '@umijs/max';
import {
  App,
  Button,
  Card,
  Popconfirm,
  Select,
  Space,
  Table,
  Tag,
  Tooltip,
  Typography,
} from 'antd';
import React, { useCallback, useEffect, useRef, useState } from 'react';
import {
  getSweepTasks,
  retrySweepTask,
  SWEEP_STATUS_LABELS,
  type SweepTaskListDto,
} from '@/abp/chainAdmin';
import {
  type ChainConnState,
  onChainConnState,
  onChainSweepChanged,
  startChainSignalR,
} from '@/abp/chainSignalR';
import { useResizableColumns } from '@/components/AutoHeightProTable/useResizableColumns';
import { useChainRecordRefresh } from './use-chain-record-refresh';

const { Text } = Typography;

/** 收到 SignalR 推送时刷新归集任务，并定期补查漏掉的状态更新。 */
export default function SweepsPage({
  embedded = false,
  initialStatus,
}: {
  embedded?: boolean;
  initialStatus?: string;
} = {}) {
  const { message } = App.useApp();
  const access = useAccess();
  const [data, setData] = useState<SweepTaskListDto[]>([]);
  const [total, setTotal] = useState(0);
  const [page, setPage] = useState(1);
  const [loading, setLoading] = useState(false);
  const [statusFilter, setStatusFilter] = useState(
    initialStatus && Object.hasOwn(SWEEP_STATUS_LABELS, initialStatus)
      ? initialStatus
      : undefined,
  );
  const [connState, setConnState] = useState<ChainConnState>('disconnected');
  const [retryingId, setRetryingId] = useState<string | null>(null);
  const latestRequest = useRef(0);
  const pageSize = 20;

  const reload = useCallback(
    async (background = false) => {
      const request = ++latestRequest.current;
      if (!background) setLoading(true);
      try {
        const result = await getSweepTasks({
          skipCount: (page - 1) * pageSize,
          maxResultCount: pageSize,
          status: statusFilter,
        });
        if (request !== latestRequest.current) return;
        setData(result.items);
        setTotal(result.totalCount);
      } catch {
        /* interceptor */
      } finally {
        if (request === latestRequest.current) setLoading(false);
      }
    },
    [page, statusFilter],
  );

  useEffect(() => {
    reload();
  }, [reload]);
  useChainRecordRefresh(reload);

  useEffect(() => {
    const offConn = onChainConnState(setConnState);
    const offSweep = onChainSweepChanged(() => void reload(true));
    startChainSignalR();
    return () => {
      offConn();
      offSweep();
    };
  }, [reload]);

  const doRetry = async (row: SweepTaskListDto) => {
    setRetryingId(row.id);
    try {
      await retrySweepTask(row.id);
      message.success('已拉回队列（下一轮归集接管）');
      reload();
    } catch {
      /* interceptor */
    } finally {
      setRetryingId(null);
    }
  };

  const fmtTime = (v: string) => {
    const d = new Date(v);
    return Number.isNaN(d.getTime()) ? v : d.toLocaleString();
  };
  const connView =
    connState === 'connected'
      ? { color: 'success' as const, text: '实时推送：已连接' }
      : connState === 'connecting'
        ? { color: 'warning' as const, text: '连接中…' }
        : { color: 'error' as const, text: '已断开，重连中' };

  const columns = [
    { title: '代币', dataIndex: 'symbol', width: 70 },
    { title: '金额', dataIndex: 'amount', width: 110, align: 'right' as const },
    {
      title: '状态',
      dataIndex: 'status',
      width: 100,
      render: (v: string) => {
        const cfg = SWEEP_STATUS_LABELS[v] ?? { color: 'default', text: v };
        return <Tag color={cfg.color}>{cfg.text}</Tag>;
      },
    },
    {
      title: '用户地址',
      dataIndex: 'address',
      width: 150,
      ellipsis: true,
      render: (v: string) => (
        <Tooltip title={v}>
          <Text
            style={{ fontFamily: 'monospace', fontSize: 11 }}
          >{`${v.slice(0, 8)}...${v.slice(-6)}`}</Text>
        </Tooltip>
      ),
    },
    {
      title: 'HD索引',
      dataIndex: 'addressIndex',
      width: 70,
      align: 'center' as const,
    },
    {
      title: '归集哈希',
      dataIndex: 'txHash',
      width: 160,
      ellipsis: true,
      render: (v: string | null) =>
        v ? (
          <Text
            copyable={{ text: v }}
            style={{ fontFamily: 'monospace', fontSize: 11 }}
          >{`${v.slice(0, 10)}...${v.slice(-6)}`}</Text>
        ) : (
          '-'
        ),
    },
    {
      title: 'Nonce',
      dataIndex: 'nonceUsed',
      width: 70,
      render: (v: number | null) => v ?? '-',
    },
    {
      title: '可重试',
      dataIndex: 'retryable',
      width: 70,
      render: (v: boolean, row: SweepTaskListDto) =>
        row.status === 'Failed' ? (
          <Tag color={v ? 'orange' : 'red'}>{v ? '是' : '仅人工'}</Tag>
        ) : (
          '-'
        ),
    },
    {
      title: '失败原因',
      dataIndex: 'failReason',
      width: 200,
      ellipsis: true,
      render: (v: string | null) => v ?? '-',
    },
    {
      title: '状态变更',
      dataIndex: 'statusChangedAt',
      width: 150,
      render: (v: string) => fmtTime(v),
    },
    ...(access.canAdminNetwork
      ? [
          {
            title: '操作',
            width: 90,
            render: (_: unknown, row: SweepTaskListDto) =>
              row.status === 'Failed' ? (
                <Popconfirm
                  title={`重试归集任务（${row.amount} ${row.symbol}）？${row.retryable ? '' : '该失败为「仅人工」类型，请先确认失败原因已排除。'}`}
                  onConfirm={() => doRetry(row)}
                  okText="重试"
                  cancelText="取消"
                >
                  <Button
                    size="small"
                    type="primary"
                    loading={retryingId === row.id}
                  >
                    重试
                  </Button>
                </Popconfirm>
              ) : (
                <Text type="secondary">-</Text>
              ),
          },
        ]
      : []),
  ];

  const {
    columns: resizableColumns,
    components,
    scrollX,
  } = useResizableColumns(columns);

  const content = (
    <Card
      title="归集任务"
      extra={
        <Space>
          <Tag color={connView.color}>{connView.text}</Tag>
          <Select
            placeholder="状态"
            allowClear
            style={{ width: 120 }}
            value={statusFilter}
            onChange={(v) => {
              setStatusFilter(v);
              setPage(1);
            }}
            options={Object.entries(SWEEP_STATUS_LABELS).map(
              ([value, cfg]) => ({ value, label: cfg.text }),
            )}
          />
        </Space>
      }
    >
      <Table<SweepTaskListDto>
        rowKey="id"
        loading={loading}
        dataSource={data}
        columns={resizableColumns}
        components={components}
        scroll={{ x: scrollX }}
        size="small"
        pagination={{
          current: page,
          pageSize,
          total,
          showSizeChanger: false,
          onChange: (p) => setPage(p),
        }}
      />
    </Card>
  );
  return embedded ? (
    content
  ) : (
    <PageContainer title="充值归集任务">{content}</PageContainer>
  );
}
