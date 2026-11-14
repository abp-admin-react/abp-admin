import { PageContainer } from '@ant-design/pro-components';
import { Link, useAccess } from '@umijs/max';
import { Card, Table, Tag, Typography } from 'antd';
import React, { useCallback, useEffect, useState } from 'react';
import { getVaultChecks, type VaultCheckListDto } from '@/abp/chainAdmin';
import {
  type ChainConnState,
  onChainConnState,
  onChainVaultCheck,
  startChainSignalR,
} from '@/abp/chainSignalR';
import { useResizableColumns } from '@/components/AutoHeightProTable/useResizableColumns';

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
  const [data, setData] = useState<VaultCheckListDto[]>([]);
  const [total, setTotal] = useState(0);
  const [page, setPage] = useState(1);
  const [loading, setLoading] = useState(false);
  const [connState, setConnState] = useState<ChainConnState>('disconnected');
  const pageSize = 20;

  const reload = useCallback(async () => {
    setLoading(true);
    try {
      const result = await getVaultChecks({
        skipCount: (page - 1) * pageSize,
        maxResultCount: pageSize,
      });
      setData(result.items);
      setTotal(result.totalCount);
    } catch {
      /* interceptor */
    } finally {
      setLoading(false);
    }
  }, [page]);

  useEffect(() => {
    reload();
  }, [reload]);

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

  const {
    columns: resizableColumns,
    components,
    scrollX,
  } = useResizableColumns(columns);

  const content = (
    <Card
      title="核对记录"
      extra={<Tag color={connView.color}>{connView.text}</Tag>}
    >
      <Table<VaultCheckListDto>
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
