import { PageContainer } from '@ant-design/pro-components';
import { Link, useAccess } from '@umijs/max';
import { AgGridReact } from 'ag-grid-react';
import { Card, Tag, Typography } from 'antd';
import { useCallback, useEffect, useRef as useGridRef, useState } from 'react';
import { businessGridQueries } from '@/abp/businessGrid';
import type { VaultCheckListDto } from '@/abp/chainAdmin';
import {
  type ChainConnState,
  onChainConnState,
  onChainVaultCheck,
  startChainSignalR,
} from '@/abp/chainSignalR';
import {
  toGridColumns,
  useSearchableRemoteGrid,
} from '@/components/remoteGrid';
import { useScopedGridQuery } from '@/hooks/useScopedGridQuery';

const { Text } = Typography;

/**
 * 储备余额核对（/collection/vault）：通过 SignalR 更新核对记录。
 */
export default function VaultPage({
  embedded = false,
}: {
  embedded?: boolean;
} = {}) {
  const access = useAccess();
  const refreshGrid = useGridRef<() => void>(undefined);
  const [connState, setConnState] = useState<ChainConnState>('disconnected');

  const reload = useCallback(() => refreshGrid.current?.(), []);

  useEffect(() => {
    const offConn = onChainConnState(setConnState);
    const offVault = onChainVaultCheck(() => reload());
    startChainSignalR();
    return () => {
      offConn();
      offVault();
    };
  }, [reload]);

  const fmtTime = (v: string) => {
    const d = new Date(v);
    return Number.isNaN(d.getTime()) ? v : d.toLocaleString();
  };
  const parseNum = (s: string) => parseFloat(s) || 0;
  const connView =
    connState === 'connected'
      ? { color: 'success' as const, text: '实时推送：已连接' }
      : connState === 'connecting'
        ? { color: 'warning' as const, text: '连接中…' }
        : { color: 'error' as const, text: '已断开，重连中' };

  const columns = [
    { title: '代币', dataIndex: 'symbol', width: 80 },
    {
      title: '储备钱包余额',
      dataIndex: 'vaultBalance',
      width: 160,
      align: 'right' as const,
      render: (v: string) => (
        <Text style={{ fontFamily: 'monospace' }}>{v}</Text>
      ),
    },
    {
      title: '用户余额合计',
      dataIndex: 'userTotal',
      width: 160,
      align: 'right' as const,
      render: (v: string) => (
        <Text style={{ fontFamily: 'monospace' }}>{v}</Text>
      ),
    },
    {
      title: '差额',
      dataIndex: 'difference',
      width: 160,
      align: 'right' as const,
      render: (v: string) => {
        const n = parseNum(v);
        if (n < 0)
          return (
            <Text strong style={{ color: '#cf1322', fontFamily: 'monospace' }}>
              {v}
            </Text>
          );
        return (
          <Text style={{ fontFamily: 'monospace', color: '#389e0d' }}>{v}</Text>
        );
      },
    },
    {
      title: '状态',
      dataIndex: 'status',
      width: 80,
      render: (_: unknown, row: VaultCheckListDto) =>
        parseNum(row.difference) < 0 ? (
          <Tag color="error">缺口</Tag>
        ) : (
          <Tag color="success">正常</Tag>
        ),
    },
    {
      title: '核对时间',
      dataIndex: 'checkedAt',
      width: 180,
      render: (v: string) => fmtTime(v),
    },
  ];

  const vaultGrid = useSearchableRemoteGrid<VaultCheckListDto>({
    ariaLabel: '金库核对',
    getRowId: ({ data }) => String(data.id),
    query: useScopedGridQuery(businessGridQueries.vault, ''),
    columnDefs: toGridColumns<VaultCheckListDto>(columns, {
      kinds: {
        vaultBalance: 'decimal',
        userTotal: 'decimal',
        difference: 'decimal',
        checkedAt: 'date',
      },
    }),
  });
  refreshGrid.current = vaultGrid.refresh;
  const content = (
    <Card
      title="核对记录"
      extra={<Tag color={connView.color}>{connView.text}</Tag>}
    >
      {vaultGrid.searchBox}
      <AgGridReact<VaultCheckListDto> {...vaultGrid.gridProps} />
    </Card>
  );
  if (embedded) return content;
  return (
    <PageContainer
      title="储备余额核对"
      breadcrumb={{ items: [{ title: '链上监控' }, { title: '储备余额核对' }] }}
      extra={
        access.canViewCollections && (
          <Link to="/collection/dashboard">返回链上监控</Link>
        )
      }
    >
      {content}
    </PageContainer>
  );
}
