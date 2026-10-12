import { PageContainer } from '@ant-design/pro-components';
import { Card, Table, Tag, Typography } from 'antd';
import React, { useCallback, useEffect, useState } from 'react';
import { VaultCheckListDto, getVaultChecks } from '@/abp/chainAdmin';
import { startChainSignalR, onChainVaultCheck, onChainConnState, ChainConnState } from '@/abp/chainSignalR';
import { useResizableColumns } from '@/components/AutoHeightProTable/useResizableColumns';

const { Text } = Typography;

/**
 * 金库核对页（/collection/vault）：SignalR 实时推送新留痕，无需刷新按钮。
 */
export default function VaultPage() {
  const [data, setData] = useState<VaultCheckListDto[]>([]);
  const [total, setTotal] = useState(0);
  const [page, setPage] = useState(1);
  const [loading, setLoading] = useState(false);
  const [connState, setConnState] = useState<ChainConnState>('disconnected');
  const pageSize = 20;

  const reload = useCallback(async () => {
    setLoading(true);
    try {
      const result = await getVaultChecks({ skipCount: (page - 1) * pageSize, maxResultCount: pageSize });
      setData(result.items); setTotal(result.totalCount);
    } catch { /* interceptor */ } finally { setLoading(false); }
  }, [page]);

  useEffect(() => { reload(); }, [reload]);

  useEffect(() => {
    const offConn = onChainConnState(setConnState);
    const offVault = onChainVaultCheck(() => reload());
    startChainSignalR();
    return () => { offConn(); offVault(); };
  }, [reload]);

  const fmtTime = (v: string) => { const d = new Date(v); return isNaN(d.getTime()) ? v : d.toLocaleString(); };
  const parseNum = (s: string) => parseFloat(s) || 0;
  const connView = connState === 'connected' ? { color: 'success' as const, text: '实时推送：已连接' }
    : connState === 'connecting' ? { color: 'warning' as const, text: '连接中…' }
    : { color: 'error' as const, text: '已断开，重连中' };

  const columns = [
    { title: '代币', dataIndex: 'symbol', width: 80 },
    { title: '金库余额', dataIndex: 'vaultBalance', width: 160, align: 'right' as const,
      render: (v: string) => <Text style={{ fontFamily: 'monospace' }}>{v}</Text> },
    { title: '用户余额合计', dataIndex: 'userTotal', width: 160, align: 'right' as const,
      render: (v: string) => <Text style={{ fontFamily: 'monospace' }}>{v}</Text> },
    { title: '差额', dataIndex: 'difference', width: 160, align: 'right' as const,
      render: (v: string) => {
        const n = parseNum(v);
        if (n < 0) return <Text strong style={{ color: '#cf1322', fontFamily: 'monospace' }}>{v}</Text>;
        return <Text style={{ fontFamily: 'monospace', color: '#389e0d' }}>{v}</Text>;
      } },
    { title: '状态', width: 80,
      render: (_: unknown, row: VaultCheckListDto) => parseNum(row.difference) < 0 ? <Tag color="error">缺口</Tag> : <Tag color="success">正常</Tag> },
    { title: '核对时间', dataIndex: 'checkedAt', width: 180, render: (v: string) => fmtTime(v) },
  ];

  const { columns: resizableColumns, components, scrollX } = useResizableColumns(columns);

  return (
    <PageContainer>
      <Card title="金库核对留痕（SignalR 实时推送新核对）" extra={<Tag color={connView.color}>{connView.text}</Tag>}>
        <Table<VaultCheckListDto> rowKey="id" loading={loading} dataSource={data} columns={resizableColumns} components={components} scroll={{ x: scrollX }} size="small"
          pagination={{ current: page, pageSize, total, showSizeChanger: false, onChange: (p) => setPage(p) }} />
      </Card>
    </PageContainer>
  );
}
