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
} from '@/abp/chainSignalR';
import {
  toGridColumns,
  useSearchableRemoteGrid,
} from '@/components/remoteGrid';
import { useScopedGridQuery } from '@/hooks/useScopedGridQuery';
import VaultReconciliationTag from './VaultReconciliationTag';

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
    return () => {
      offConn();
      offVault();
    };
  }, [reload]);

  const connView =
    connState === 'connected'
      ? { color: 'success' as const, text: '实时推送：已连接' }
      : connState === 'connecting'
        ? { color: 'warning' as const, text: '连接中…' }
        : { color: 'error' as const, text: '已断开，重连中' };

  const columns = [
    { title: '代币', dataIndex: 'symbol', width: 80 },
    {
      title: '核对结论',
      dataIndex: 'status',
      width: 112,
      render: (_: unknown, row: VaultCheckListDto) => (
        <VaultReconciliationTag status={row.reconciliationStatus} />
      ),
    },
    {
      title: '核对后差额',
      dataIndex: 'reconciledDifference',
      width: 160,
      align: 'right' as const,
      render: (v: string | null, row: VaultCheckListDto) => (
        <Text
          type={row.reconciliationStatus === 'Shortfall' ? 'danger' : undefined}
        >
          {v ?? '—'}
        </Text>
      ),
    },
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
      title: '原始差额',
      dataIndex: 'difference',
      width: 160,
      align: 'right' as const,
      render: (v: string) => <Text>{v}</Text>,
    },
    {
      title: '已到账待记账',
      dataIndex: 'pendingCredit',
      width: 160,
      render: (v: string | null) => v ?? '—',
    },
    {
      title: '参考区块后记账',
      dataIndex: 'postedAfterBlock',
      width: 170,
      render: (v: string | null) => v ?? '—',
    },
    {
      title: '参考区块',
      dataIndex: 'referenceBlock',
      width: 135,
      render: (v: number | null) => v ?? '—',
    },
    {
      title: '核对金库地址',
      dataIndex: 'referenceVaultAddress',
      width: 380,
      render: (v: string | null) => (v ? <Text copyable>{v}</Text> : '—'),
    },
    {
      title: '核对时间',
      dataIndex: 'checkedAt',
      width: 180,
    },
  ];

  const vaultGrid = useSearchableRemoteGrid<VaultCheckListDto>({
    ariaLabel: '金库核对',
    getRowId: ({ data }) => String(data.id),
    query: useScopedGridQuery(businessGridQueries.vault, ''),
    columnDefs: toGridColumns<VaultCheckListDto>(columns, {
      utcDateFields: ['checkedAt'],
      kinds: {
        vaultBalance: 'decimal',
        userTotal: 'decimal',
        difference: 'decimal',
        reconciledDifference: 'decimal',
        pendingCredit: 'decimal',
        postedAfterBlock: 'decimal',
        referenceBlock: 'number',
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
