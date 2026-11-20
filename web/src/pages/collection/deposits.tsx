import { PageContainer } from '@ant-design/pro-components';
import { Link, useAccess, useModel } from '@umijs/max';
import { AgGridReact } from 'ag-grid-react';
import {
  Alert,
  App,
  Button,
  Card,
  Popconfirm,
  Select,
  Space,
  Tag,
  Tooltip,
  Typography,
} from 'antd';
import {
  useCallback,
  useEffect,
  useRef as useGridRef,
  useRef,
  useState,
} from 'react';
import { businessGridQueries, gridScope } from '@/abp/businessGrid';
import {
  approveLargeDeposit,
  DEPOSIT_STATUS_LABELS,
  type DepositOrderListDto,
  depositStatusLabel,
  getDepositOrders,
} from '@/abp/chainAdmin';
import {
  type ChainConnState,
  onChainConnState,
  onChainDepositChanged,
  startChainSignalR,
} from '@/abp/chainSignalR';
import {
  toGridColumns,
  useSearchableLocalGrid,
  useSearchableRemoteGrid,
} from '@/components/remoteGrid';
import { useScopedGridQuery } from '@/hooks/useScopedGridQuery';
import { depositVouchersUrl } from '@/pages/finance/vouchers/navigation';
import { isValidRecordId, RecordFocus } from './record-focus';
import {
  depositRecordUrl,
  memberFundsUrl,
  sweepTaskUrl,
  topUpOrderRecordsUrl,
} from './record-links';
import { useChainRecordRefresh } from './use-chain-record-refresh';

const { Text } = Typography;
const DEPOSIT_LIST_PATH = '/operations/payments?view=deposits';

/**
 * 充值记录：接收链上推送，并定时核对最终状态。
 */
export default function DepositsPage({
  embedded = false,
  depositId,
  initialStatus,
  sweepTaskId,
  rulesHref = '/operations/payments?view=deposits&settings=deposit',
}: {
  embedded?: boolean;
  depositId?: string;
  initialStatus?: string;
  sweepTaskId?: string;
  rulesHref?: string;
} = {}) {
  const { message } = App.useApp();
  const access = useAccess();
  const { initialState } = useModel('@@initialState');
  const currentUserId = initialState?.currentUser?.userid;
  const refreshGrid = useGridRef<() => void>(undefined);
  const [data, setData] = useState<DepositOrderListDto[]>([]);
  const focused = depositId !== undefined;
  const invalidId = focused && !isValidRecordId(depositId);
  const invalidSweepId =
    !focused && sweepTaskId !== undefined && !isValidRecordId(sweepTaskId);
  const [loading, setLoading] = useState(focused && !invalidId);
  const [loadFailed, setLoadFailed] = useState(false);
  const [statusFilter, setStatusFilter] = useState(initialStatus);
  const [symbolFilter, setSymbolFilter] = useState<string | undefined>();
  const [connState, setConnState] = useState<ChainConnState>('disconnected');
  const latestRequest = useRef(0);

  const reload = useCallback(
    async (background = false) => {
      refreshGrid.current?.();
      if (!focused || invalidId || invalidSweepId) return;
      const request = ++latestRequest.current;
      if (!background) setLoading(true);
      try {
        const result = await getDepositOrders({
          skipCount: 0,
          maxResultCount: 1,
          depositId,
        });
        if (request !== latestRequest.current) return;
        const rows = focused
          ? result.items.filter(
              (item) => item.id.toLowerCase() === depositId.toLowerCase(),
            )
          : result.items;
        setData(rows);

        setLoadFailed(false);
      } catch {
        if (focused && !background && request === latestRequest.current) {
          setData([]);
          setLoadFailed(true);
        }
      } finally {
        if (request === latestRequest.current) setLoading(false);
      }
    },
    [depositId, focused, invalidId, invalidSweepId],
  );

  useEffect(() => {
    if (focused) void reload();
  }, [focused, reload]);
  useChainRecordRefresh(reload);

  useEffect(() => {
    const offConn = onChainConnState(setConnState);
    const offDeposit = onChainDepositChanged(() => {
      // 收到推送：重新拉当前页（轻量，全量刷新保序）
      void reload(true);
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

  const renderStatus = (_value: string, row: DepositOrderListDto) => {
    const config = depositStatusLabel(row);
    return <Tag color={config.color}>{config.text}</Tag>;
  };

  const columns = [
    ...(!focused
      ? [
          {
            title: '充值单号',
            dataIndex: 'id',
            width: 120,
            render: (id: string) => (
              <Link to={depositRecordUrl(id)} aria-label={`查看充值单 ${id}`}>
                {id.slice(0, 8)}…
              </Link>
            ),
          },
        ]
      : []),
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
      // 快照两列同写同无（后端契约）——无快照的历史单直接回退毛额展示
      render: (v?: string, row?: DepositOrderListDto) => v ?? row?.amount,
    },
    {
      title: '状态',
      dataIndex: 'status',
      width: 120,
      render: renderStatus,
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
              row.status === 'Confirmed' &&
              !row.sweepTaskId &&
              !row.manuallyReleased ? (
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

  const record = focused ? data[0] : undefined;
  const canViewTopUpOrder =
    access.canViewAllOrders ||
    (access.canViewOwnOrders &&
      currentUserId &&
      currentUserId.toLowerCase() === record?.userId.toLowerCase());
  const depositsGridScope = gridScope({
    depositId,
    sweepTaskId,
    status: focused ? undefined : statusFilter,
    symbol: focused ? undefined : symbolFilter,
  });
  const recordColumns = toGridColumns<DepositOrderListDto>(columns, {
    kinds: {
      amount: 'decimal',
      fee: 'decimal',
      actualAmount: 'decimal',
      blockNumber: 'number',
      logIndex: 'number',
      creationTime: 'date',
    },
  });
  const depositsGrid = useSearchableRemoteGrid<DepositOrderListDto>({
    ariaLabel: '充值记录',
    getRowId: ({ data }) => String(data.id),
    query: useScopedGridQuery(businessGridQueries.deposits, depositsGridScope),
    columnDefs: recordColumns,
  });
  const focusedGrid = useSearchableLocalGrid<DepositOrderListDto>({
    ariaLabel: '充值记录',
    getRowId: ({ data }) => String(data.id),
    pagination: false,
    rowData: data,
    columnDefs: recordColumns,
    keywordFields: ['txHash', 'toAddress', 'userId', 'symbol'],
  });
  const visibleGrid = focused ? focusedGrid : depositsGrid;
  refreshGrid.current = depositsGrid.refresh;
  const content = (
    <Card
      extra={
        <Space>
          <Tag color={connView.color}>{connView.text}</Tag>
          {!focused && !invalidSweepId && (
            <>
              <Select
                placeholder="状态"
                allowClear
                style={{ width: 140 }}
                value={statusFilter}
                onChange={(v) => {
                  setStatusFilter(v);
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
                }}
                options={[
                  { value: 'USDT', label: 'USDT' },
                  { value: 'USDC', label: 'USDC' },
                ]}
              />
            </>
          )}
        </Space>
      }
    >
      {focused && (
        <RecordFocus
          kind="充值"
          listPath={DEPOSIT_LIST_PATH}
          invalid={invalidId}
          failed={loadFailed}
          loading={loading}
          reload={reload}
          summary={
            record && (
              <Space wrap>
                <Text>{record.networkName}</Text>
                <Text strong>
                  {record.amount == null
                    ? `超精度 ${record.symbol}`
                    : `${record.amount} ${record.symbol}`}
                </Text>
                {renderStatus(record.status, record)}
                <Text copyable>充值单号 {record.id}</Text>
                {record.balanceTxnId &&
                  (access.canViewMemberFunds ? (
                    <Link
                      to={memberFundsUrl(record.userId, record.balanceTxnId)}
                      aria-label={`查看充值余额流水 ${record.balanceTxnId}`}
                    >
                      查看余额流水
                    </Link>
                  ) : (
                    <Text copyable={{ text: record.balanceTxnId }}>
                      余额流水 {record.balanceTxnId}
                    </Text>
                  ))}
                {record.balanceTxnId && canViewTopUpOrder && (
                  <Link
                    to={topUpOrderRecordsUrl(
                      record.userId,
                      record.balanceTxnId,
                    )}
                    aria-label={`查看充值单 ${record.id} 的充值入账记录`}
                  >
                    查看充值入账记录
                  </Link>
                )}
                {record.sweepTaskId &&
                  (access.canViewWeb3Operations &&
                  (access.canViewCollections || access.canAdminNetwork) ? (
                    <Link
                      to={sweepTaskUrl(record.sweepTaskId)}
                      aria-label={`查看归集任务 ${record.sweepTaskId}`}
                    >
                      查看归集任务
                    </Link>
                  ) : (
                    <Text copyable={{ text: record.sweepTaskId }}>
                      归集任务 {record.sweepTaskId}
                    </Text>
                  ))}
                {record.status === 'Swept' && access.canViewFinanceVouchers && (
                  <Link
                    to={depositVouchersUrl(record.id)}
                    aria-label={`查看充值单 ${record.id} 的会计凭证`}
                  >
                    查看会计凭证
                  </Link>
                )}
              </Space>
            )
          }
        />
      )}
      {!focused && sweepTaskId !== undefined && (
        <Alert
          type={invalidSweepId ? 'warning' : 'info'}
          title={
            invalidSweepId
              ? '无效的归集任务链接'
              : `归集任务 ${sweepTaskId} 的充值记录`
          }
          action={<Link to={DEPOSIT_LIST_PATH}>查看全部充值</Link>}
          style={{ marginBottom: 12 }}
        />
      )}
      {!invalidSweepId && (!focused || record) && (
        <>
          {visibleGrid.searchBox}
          <AgGridReact<DepositOrderListDto> {...visibleGrid.gridProps} />
        </>
      )}
    </Card>
  );
  if (embedded) return content;
  return (
    <PageContainer
      title="充值记录"
      extra={
        access.canConfigureCollectionNetwork && (
          <Link to={rulesHref}>充值规则</Link>
        )
      }
    >
      {content}
    </PageContainer>
  );
}
