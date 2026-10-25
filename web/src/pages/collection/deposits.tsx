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
import React, { useCallback, useEffect, useState } from 'react';
import {
  approveLargeDeposit,
  DEPOSIT_STATUS_LABELS,
  type DepositOrderListDto,
  getDepositOrders,
} from '@/abp/chainAdmin';
import {
  type ChainConnState,
  onChainConnState,
  onChainDepositChanged,
  startChainSignalR,
} from '@/abp/chainSignalR';
import { useResizableColumns } from '@/components/AutoHeightProTable/useResizableColumns';

const { Text } = Typography;

/**
 * 充值单页（/collection/deposits）：SignalR 实时推送新进款单，无需刷新按钮。
 */
export default function DepositsPage() {
  const { message } = App.useApp();
  const access = useAccess();
  const [data, setData] = useState<DepositOrderListDto[]>([]);
  const [total, setTotal] = useState(0);
  const [page, setPage] = useState(1);
  const [loading, setLoading] = useState(false);
  const [statusFilter, setStatusFilter] = useState<string | undefined>();
  const [symbolFilter, setSymbolFilter] = useState<string | undefined>();
  const [connState, setConnState] = useState<ChainConnState>('disconnected');
  const pageSize = 20;

  const reload = useCallback(async () => {
    setLoading(true);
    try {
      const result = await getDepositOrders({
        skipCount: (page - 1) * pageSize,
        maxResultCount: pageSize,
        status: statusFilter,
        symbol: symbolFilter,
      });
      setData(result.items);
      setTotal(result.totalCount);
    } catch {
      /* interceptor */
    } finally {
      setLoading(false);
    }
  }, [page, statusFilter, symbolFilter]);

  useEffect(() => {
    reload();
  }, [reload]);

  useEffect(() => {
    const offConn = onChainConnState(setConnState);
    const offDeposit = onChainDepositChanged(() => {
      // 收到推送：重新拉当前页（轻量，全量刷新保序）
      reload();
    });
    startChainSignalR();
    return () => {
      offConn();
      offDeposit();
    };
  }, [reload]);

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
    {
      title: '交易哈希',
      dataIndex: 'txHash',
      width: 180,
      ellipsis: true,
      render: (v: string) => (
        <Tooltip title={v}>
          <Text
            copyable={{ text: v }}
            style={{ fontFamily: 'monospace', fontSize: 12 }}
          >{`${v.slice(0, 12)}...${v.slice(-6)}`}</Text>
        </Tooltip>
      ),
    },
    {
      title: 'Log#',
      dataIndex: 'logIndex',
      width: 60,
      align: 'center' as const,
    },
    {
      title: '用户',
      dataIndex: 'userId',
      width: 120,
      ellipsis: true,
      render: (v: string) => (
        <Text
          style={{ fontFamily: 'monospace', fontSize: 11 }}
        >{`${v.slice(0, 8)}...`}</Text>
      ),
    },
    {
      title: '网络',
      dataIndex: 'networkName',
      width: 80,
      render: (v: string) => <Tag color="cyan">{v}</Tag>,
    },
    { title: '代币', dataIndex: 'symbol', width: 70 },
    {
      title: '毛额',
      dataIndex: 'amount',
      width: 110,
      align: 'right' as const,
      render: (v: string | null, row: DepositOrderListDto) =>
        v ?? (
          <Tooltip title={row.rawAmount}>
            <Tag color="warning">超精度</Tag>
          </Tooltip>
        ),
    },
    {
      title: '手续费',
      dataIndex: 'fee',
      width: 90,
      align: 'right' as const,
      render: (v?: string) => v ?? '-',
    },
    {
      title: '净额',
      dataIndex: 'actualAmount',
      width: 110,
      align: 'right' as const,
      render: (v?: string, row?: DepositOrderListDto) =>
        v ?? (row?.fee ? <Tag color="warning">待人工</Tag> : row?.amount),
    },
    {
      title: '状态',
      dataIndex: 'status',
      width: 120,
      render: (v: string) => {
        const cfg = DEPOSIT_STATUS_LABELS[v] ?? { color: 'default', text: v };
        return <Tag color={cfg.color}>{cfg.text}</Tag>;
      },
    },
    {
      title: '区块',
      dataIndex: 'blockNumber',
      width: 100,
      render: (v: number) => (
        <Text style={{ fontFamily: 'monospace' }}>
          {Number(v).toLocaleString()}
        </Text>
      ),
    },
    {
      title: '充值地址',
      dataIndex: 'toAddress',
      width: 140,
      ellipsis: true,
      render: (v: string) => (
        <Text
          style={{ fontFamily: 'monospace', fontSize: 11 }}
        >{`${v.slice(0, 8)}...${v.slice(-6)}`}</Text>
      ),
    },
    {
      title: '时间',
      dataIndex: 'creationTime',
      width: 150,
      render: (v: string) => fmtTime(v),
    },
    ...(access.canAdminNetwork
      ? [
          {
            title: '操作',
            key: 'actions',
            width: 90,
            fixed: 'right' as const,
            render: (_: unknown, row: DepositOrderListDto) =>
              row.status === 'Confirmed' && !row.sweepTaskId ? (
                <Popconfirm
                  title={`放行进款 ${row.amount} ${row.symbol} 进归集？`}
                  onConfirm={async () => {
                    try {
                      await approveLargeDeposit(row.id);
                      message.success('已放行入队');
                      reload();
                    } catch {
                      /* 全局拦截器提示（链网运维写路径错误码已本地化） */
                    }
                  }}
                >
                  <Button size="small" type="primary">
                    放行
                  </Button>
                </Popconfirm>
              ) : (
                <Text type="secondary">—</Text>
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

  return (
    <PageContainer>
      <Card
        title="充值单（SignalR 实时推送新进款）"
        extra={
          <Space>
            <Tag color={connView.color}>{connView.text}</Tag>
            <Select
              placeholder="状态"
              allowClear
              style={{ width: 140 }}
              value={statusFilter}
              onChange={(v) => {
                setStatusFilter(v);
                setPage(1);
              }}
              options={Object.entries(DEPOSIT_STATUS_LABELS).map(
                ([value, cfg]) => ({ value, label: cfg.text }),
              )}
            />
            <Select
              placeholder="代币"
              allowClear
              style={{ width: 100 }}
              value={symbolFilter}
              onChange={(v) => {
                setSymbolFilter(v);
                setPage(1);
              }}
              options={[
                { value: 'USDT', label: 'USDT' },
                { value: 'USDC', label: 'USDC' },
              ]}
            />
          </Space>
        }
      >
        <Table<DepositOrderListDto>
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
    </PageContainer>
  );
}
