import { PageContainer } from '@ant-design/pro-components';
import { App, Button, Card, Popconfirm, Select, Space, Table, Tag, Tooltip, Typography } from 'antd';
import React, { useCallback, useEffect, useState } from 'react';
import { SWEEP_STATUS_LABELS, type SweepTaskListDto, getSweepTasks, retrySweepTask } from '@/abp/chainAdmin';
import { startChainSignalR, onChainSweepChanged, onChainConnState, type ChainConnState } from '@/abp/chainSignalR';
import { useAccess } from '@umijs/max';
import { useResizableColumns } from '@/components/AutoHeightProTable/useResizableColumns';

const { Text } = Typography;

/**
 * 归集任务页（/collection/sweeps）：SignalR 实时推送任务状态变更，无需刷新按钮。
 * Failed 任务可手动重试（跳过冷却立即拉回 Queued；仅人工失败也允许——管理员显式决策）。
 */
export default function SweepsPage() {
  const { message } = App.useApp();
  const access = useAccess();
  const [data, setData] = useState<SweepTaskListDto[]>([]);
  const [total, setTotal] = useState(0);
  const [page, setPage] = useState(1);
  const [loading, setLoading] = useState(false);
  const [statusFilter, setStatusFilter] = useState<string | undefined>();
  const [connState, setConnState] = useState<ChainConnState>('disconnected');
  const [retryingId, setRetryingId] = useState<string | null>(null);
  const pageSize = 20;

  const reload = useCallback(async () => {
    setLoading(true);
    try {
      const result = await getSweepTasks({ skipCount: (page - 1) * pageSize, maxResultCount: pageSize, status: statusFilter });
      setData(result.items); setTotal(result.totalCount);
    } catch { /* interceptor */ } finally { setLoading(false); }
  }, [page, statusFilter]);

  useEffect(() => { reload(); }, [reload]);

  useEffect(() => {
    const offConn = onChainConnState(setConnState);
    const offSweep = onChainSweepChanged(() => reload());
    startChainSignalR();
    return () => { offConn(); offSweep(); };
  }, [reload]);

  const doRetry = async (row: SweepTaskListDto) => {
    setRetryingId(row.id);
    try {
      await retrySweepTask(row.id);
      message.success('已拉回队列（下一轮归集接管）');
      reload();
    } catch { /* interceptor */ } finally { setRetryingId(null); }
  };

  const fmtTime = (v: string) => { const d = new Date(v); return Number.isNaN(d.getTime()) ? v : d.toLocaleString(); };
  const connView = connState === 'connected' ? { color: 'success' as const, text: '实时推送：已连接' }
    : connState === 'connecting' ? { color: 'warning' as const, text: '连接中…' }
    : { color: 'error' as const, text: '已断开，重连中' };

  const columns = [
    { title: '代币', dataIndex: 'symbol', width: 70 },
    { title: '金额', dataIndex: 'amount', width: 110, align: 'right' as const },
    { title: '状态', dataIndex: 'status', width: 100,
      render: (v: string) => { const cfg = SWEEP_STATUS_LABELS[v] ?? { color: 'default', text: v }; return <Tag color={cfg.color}>{cfg.text}</Tag>; } },
    { title: '用户地址', dataIndex: 'address', width: 150, ellipsis: true,
      render: (v: string) => <Tooltip title={v}><Text style={{ fontFamily: 'monospace', fontSize: 11 }}>{`${v.slice(0, 8)}...${v.slice(-6)}`}</Text></Tooltip> },
    { title: 'HD索引', dataIndex: 'addressIndex', width: 70, align: 'center' as const },
    { title: '归集哈希', dataIndex: 'txHash', width: 160, ellipsis: true,
      render: (v: string | null) => v ? <Text copyable={{ text: v }} style={{ fontFamily: 'monospace', fontSize: 11 }}>{`${v.slice(0, 10)}...${v.slice(-6)}`}</Text> : '-' },
    { title: 'Nonce', dataIndex: 'nonceUsed', width: 70, render: (v: number | null) => v ?? '-' },
    { title: '可重试', dataIndex: 'retryable', width: 70,
      render: (v: boolean, row: SweepTaskListDto) => row.status === 'Failed' ? <Tag color={v ? 'orange' : 'red'}>{v ? '是' : '仅人工'}</Tag> : '-' },
    { title: '失败原因', dataIndex: 'failReason', width: 200, ellipsis: true, render: (v: string | null) => v ?? '-' },
    { title: '状态变更', dataIndex: 'statusChangedAt', width: 150, render: (v: string) => fmtTime(v) },
    ...(access.canAdminNetwork ? [{ title: '操作', width: 90, render: (_: unknown, row: SweepTaskListDto) =>
      row.status === 'Failed' ? (
        <Popconfirm
          title={`重试归集任务（${row.amount} ${row.symbol}）？${row.retryable ? '' : '该失败为「仅人工」类型，请先确认失败原因已排除。'}`}
          onConfirm={() => doRetry(row)}
          okText="重试"
          cancelText="取消"
        >
          <Button size="small" type="primary" loading={retryingId === row.id}>重试</Button>
        </Popconfirm>
      ) : <Text type="secondary">-</Text> }] : []),
  ];

  const { columns: resizableColumns, components, scrollX } = useResizableColumns(columns);

  return (
    <PageContainer>
      <Card
        title="归集任务（SignalR 实时推送状态变更）"
        extra={
          <Space>
            <Tag color={connView.color}>{connView.text}</Tag>
            <Select placeholder="状态" allowClear style={{ width: 120 }} value={statusFilter}
              onChange={(v) => { setStatusFilter(v); setPage(1); }}
              options={Object.entries(SWEEP_STATUS_LABELS).map(([value, cfg]) => ({ value, label: cfg.text }))} />
          </Space>
        }
      >
        <Table<SweepTaskListDto> rowKey="id" loading={loading} dataSource={data} columns={resizableColumns} components={components} scroll={{ x: scrollX }} size="small"
          pagination={{ current: page, pageSize, total, showSizeChanger: false, onChange: (p) => setPage(p) }} />
      </Card>
    </PageContainer>
  );
}
