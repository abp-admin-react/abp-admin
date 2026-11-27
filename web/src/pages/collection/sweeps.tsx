import { PageContainer } from '@ant-design/pro-components';
import { Link, useAccess } from '@umijs/max';
import { AgGridReact } from 'ag-grid-react';
import {
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
  getDepositOrders,
  getSweepTasks,
  retrySweepTask,
  SWEEP_STATUS_LABELS,
  type SweepTaskListDto,
} from '@/abp/chainAdmin';
import {
  type ChainConnState,
  onChainConnState,
  onChainSweepChanged,
} from '@/abp/chainSignalR';
import {
  toGridColumns,
  useSearchableLocalGrid,
  useSearchableRemoteGrid,
} from '@/components/remoteGrid';
import { useScopedGridQuery } from '@/hooks/useScopedGridQuery';
import { isValidRecordId, RecordFocus } from './record-focus';
import { depositRecordsForSweepUrl } from './record-links';
import { useChainRecordRefresh } from './use-chain-record-refresh';

const { Text } = Typography;

/** 收到 SignalR 推送时刷新归集任务，并定期补查漏掉的状态更新。 */
export default function SweepsPage({
  embedded = false,
  initialStatus,
  depositId,
  sweepTaskId,
}: {
  embedded?: boolean;
  initialStatus?: string;
  depositId?: string;
  sweepTaskId?: string;
} = {}) {
  const { message } = App.useApp();
  const access = useAccess();
  const refreshGrid = useGridRef<(preserveRows?: boolean) => void>(undefined);
  const [data, setData] = useState<SweepTaskListDto[]>([]);
  const focusId = sweepTaskId ?? depositId;
  const invalidId = !!focusId && !isValidRecordId(focusId);
  const [loading, setLoading] = useState(!!focusId && !invalidId);
  const [loadFailed, setLoadFailed] = useState(false);
  const [statusFilter, setStatusFilter] = useState(
    initialStatus && Object.hasOwn(SWEEP_STATUS_LABELS, initialStatus)
      ? initialStatus
      : undefined,
  );
  const [connState, setConnState] = useState<ChainConnState>('disconnected');
  const [retryingId, setRetryingId] = useState<string | null>(null);
  const latestRequest = useRef(0);

  const reload = useCallback(
    async (background = false) => {
      refreshGrid.current?.(background);
      if (invalidId || !focusId) return;
      const request = ++latestRequest.current;
      if (!background) {
        setLoading(true);
        setLoadFailed(false);
      }
      try {
        let taskId = sweepTaskId;
        if (!taskId && depositId) {
          const deposits = await getDepositOrders({
            skipCount: 0,
            maxResultCount: 1,
            depositId,
          });
          if (request !== latestRequest.current) return;
          taskId =
            deposits.items.find(
              (row) => row.id.toLowerCase() === depositId.toLowerCase(),
            )?.sweepTaskId ?? undefined;
          if (!taskId) {
            setData([]);

            return;
          }
        }
        const result = await getSweepTasks({
          skipCount: 0,
          maxResultCount: 1,
          ...(taskId && { sweepTaskId: taskId }),
        });
        if (request !== latestRequest.current) return;
        const rows = taskId
          ? result.items.filter(
              ({ id }) => id.toLowerCase() === taskId.toLowerCase(),
            )
          : result.items;
        setData(rows);
      } catch {
        if (focusId && !background && request === latestRequest.current)
          setLoadFailed(true);
      } finally {
        if (request === latestRequest.current) setLoading(false);
      }
    },
    [depositId, focusId, invalidId, sweepTaskId],
  );

  useEffect(() => {
    if (focusId) void reload();
  }, [focusId, reload]);
  useChainRecordRefresh(reload, onChainSweepChanged);
  useEffect(() => onChainConnState(setConnState), []);

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
      width: 180,
    },
    ...(access.canAdminNetwork || access.canViewDepositRecords
      ? [
          {
            title: '操作',
            width: 165,
            render: (_: unknown, row: SweepTaskListDto) => (
              <Space wrap>
                {access.canViewDepositRecords && (
                  <Link to={depositRecordsForSweepUrl(row.id)}>相关充值</Link>
                )}
                {access.canAdminNetwork && row.status === 'Failed' && (
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
                )}
              </Space>
            ),
          },
        ]
      : []),
  ];

  const sweepsGridScope = gridScope({
    sweepTaskId: focusId ? data[0]?.id : undefined,
    status: focusId ? undefined : statusFilter,
  });
  const recordColumns = toGridColumns<SweepTaskListDto>(columns, {
    utcDateFields: ['statusChangedAt'],
    kinds: {
      amount: 'decimal',
      addressIndex: 'number',
      nonceUsed: 'number',
      retryable: 'boolean',
    },
  });
  const sweepsGrid = useSearchableRemoteGrid<SweepTaskListDto>({
    ariaLabel: '归集任务',
    getRowId: ({ data }) => String(data.id),
    query: useScopedGridQuery(businessGridQueries.sweeps, sweepsGridScope),
    columnDefs: recordColumns,
  });
  const focusedGrid = useSearchableLocalGrid<SweepTaskListDto>({
    ariaLabel: '归集任务',
    getRowId: ({ data }) => String(data.id),
    pagination: false,
    rowData: data,
    columnDefs: recordColumns,
    keywordFields: ['address', 'symbol', 'txHash'],
  });
  const visibleGrid = focusId ? focusedGrid : sweepsGrid;
  refreshGrid.current = (background) => background ? sweepsGrid.refreshCachedRows() : sweepsGrid.refresh();
  const content = (
    <Card
      title="归集任务"
      extra={
        <Space>
          <Tag color={connView.color}>{connView.text}</Tag>
          {!focusId && (
            <Select
              aria-label="归集任务状态"
              placeholder="状态"
              allowClear
              style={{ width: 120 }}
              value={statusFilter}
              onChange={(v) => {
                setStatusFilter(v);
              }}
              options={Object.entries(SWEEP_STATUS_LABELS).map(
                ([value, cfg]) => ({ value, label: cfg.text }),
              )}
            />
          )}
        </Space>
      }
    >
      {focusId && (
        <RecordFocus
          kind="归集任务"
          listPath="/web3/operations?view=sweeps"
          invalid={invalidId}
          failed={loadFailed}
          loading={loading}
          reload={reload}
          summary={
            data[0] && (
              <Text copyable={{ text: data[0].id }}>任务 ID {data[0].id}</Text>
            )
          }
        />
      )}
      {(!focusId || data[0]) && (
        <>
          {visibleGrid.searchBox}
          <AgGridReact<SweepTaskListDto> {...visibleGrid.gridProps} />
        </>
      )}
    </Card>
  );
  return embedded ? (
    content
  ) : (
    <PageContainer title="充值归集任务">{content}</PageContainer>
  );
}
