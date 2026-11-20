import { PageContainer } from '@ant-design/pro-components';
import { history, Link, useAccess, useLocation, useModel } from '@umijs/max';
import { AgGridReact, type CustomCellRendererProps } from 'ag-grid-react';
import {
  Alert,
  Button,
  Card,
  Form,
  Input,
  Select,
  Space,
  Statistic,
  Tag,
  Tooltip,
  Typography,
} from 'antd';
import { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import { businessGridQueries, gridScope } from '@/abp/businessGrid';
import {
  BALANCE_ACTION_LABELS,
  type BalanceTxnDto,
  getMemberBalances,
  getMemberTxns,
  type MemberBalanceDto,
  type MemberBalanceGridRowDto,
  queryMemberBalanceAccounts,
} from '@/abp/collectionAdmin';
import { getUsers } from '@/abp/identity';
import { currencyLabel } from '@/abp/ordering';
import {
  type RemoteColumn,
  toGridColumns,
  useSearchableLocalGrid,
  useSearchableRemoteGrid,
} from '@/components/remoteGrid';
import { useScopedGridQuery } from '@/hooks/useScopedGridQuery';
import { isValidRecordId } from './record-focus';
import {
  depositRecordUrl,
  memberFundsUrl,
  orderRecordUrl,
  withdrawalFundsUrl,
  withdrawalRecordUrl,
} from './record-links';
import { useChainRecordRefresh } from './use-chain-record-refresh';

const { Text } = Typography;
const userIdPattern =
  /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;

const fmtTime = (value: string) => {
  const time = new Date(value);
  return Number.isNaN(time.getTime()) ? value : time.toLocaleString();
};

/**
 * 无 userId 时列出余额账户，指定用户时展示该用户的余额与流水。
 * 用户目录权限控制账户身份标签和按用户名或邮箱搜索。资金详情也可用用户 ID 打开。
 */
export default function MemberFundsPage() {
  const access = useAccess();
  const { search } = useLocation();
  const { initialState } = useModel('@@initialState');
  const currentUser = initialState?.currentUser?.userid;
  const query = new URLSearchParams(search);
  const requestedUserId = query.get('userId');
  const requestedTxnId = query.get('txnId');
  const requestedWithdrawalId = query.get('withdrawalId');
  const hasRecordLink =
    requestedUserId !== null ||
    requestedTxnId !== null ||
    requestedWithdrawalId !== null;
  const invalidRecordLink =
    hasRecordLink &&
    (!requestedUserId ||
      !isValidRecordId(requestedUserId) ||
      (requestedTxnId !== null && !isValidRecordId(requestedTxnId)) ||
      (requestedWithdrawalId !== null &&
        !isValidRecordId(requestedWithdrawalId)));
  const linkedUserId = !invalidRecordLink
    ? requestedUserId?.toLowerCase()
    : undefined;
  const txnId = !invalidRecordLink ? requestedTxnId?.toLowerCase() : undefined;
  const withdrawalId = !invalidRecordLink
    ? requestedWithdrawalId?.toLowerCase()
    : undefined;
  const userId = linkedUserId;
  const userLabels = useRef<Record<string, string>>({});
  const [userOptions, setUserOptions] = useState<
    { label: string; value: string }[]
  >([]);
  const [searching, setSearching] = useState(false);
  const [funds, setFunds] = useState<{
    userId: string;
    queryKey: string;
    balances: MemberBalanceDto[];
    txns: BalanceTxnDto[];
    total: number;
  }>();
  const paginationKey = `${userId ?? ''}:${withdrawalId ?? ''}`;
  const queryKey = `${paginationKey}:${txnId ?? ''}:`;
  const balances = funds?.userId === userId ? (funds?.balances ?? []) : [];
  const txns = funds?.queryKey === queryKey ? funds.txns : [];
  const [loading, setLoading] = useState(!!linkedUserId);
  const [loadFailed, setLoadFailed] = useState(false);
  const refreshTxns = useRef<() => void>(undefined);
  const fundsRequest = useRef(0);

  const [searchError, setSearchError] = useState<string | null>(null);
  const searchTimer = useRef<ReturnType<typeof setTimeout> | null>(null);
  const searchVersion = useRef(0);
  useEffect(
    () => () => {
      searchVersion.current++;
      if (searchTimer.current != null) {
        clearTimeout(searchTimer.current);
      }
    },
    [],
  );
  const searchUsers = useCallback(async (keyword: string, version: number) => {
    try {
      const result = await getUsers({ filter: keyword.trim(), pageSize: 20 });
      if (version !== searchVersion.current) return;
      setUserOptions(
        result.items.map((u) => ({
          label: `${u.userName}${u.email ? ` (${u.email})` : ''}`,
          value: u.id,
        })),
      );
    } catch {
      if (version !== searchVersion.current) return;
      setUserOptions([]);
      setSearchError('用户搜索失败，请稍后重试。');
    } finally {
      if (version === searchVersion.current) setSearching(false);
    }
  }, []);

  const reloadFunds = useCallback(
    async (background = false) => {
      if (!userId) return;
      const request = ++fundsRequest.current;
      if (!background) setLoading(true);
      try {
        if (background) refreshTxns.current?.();
        const [balanceResult, txnResult] = await Promise.all([
          getMemberBalances(userId),
          txnId
            ? getMemberTxns({
                userId,
                skipCount: 0,
                maxResultCount: 1,
                ...(txnId ? { txnId } : {}),
                ...(withdrawalId ? { withdrawalId } : {}),
              })
            : Promise.resolve({ items: [], totalCount: 0 }),
        ]);
        // 切换用户或筛选条件后，旧请求不能覆盖当前流水。
        if (request !== fundsRequest.current) return;
        setFunds({
          userId,
          queryKey,
          balances: balanceResult.items ?? [],
          txns: txnResult.items ?? [],
          total: txnResult.totalCount ?? 0,
        });
        setLoadFailed(false);
      } catch {
        if (!background && request === fundsRequest.current)
          setLoadFailed(true);
      } finally {
        if (request === fundsRequest.current) setLoading(false);
      }
    },
    [userId, queryKey, txnId, withdrawalId],
  );
  useEffect(() => {
    void reloadFunds();
    return () => {
      fundsRequest.current++;
    };
  }, [reloadFunds]);
  useChainRecordRefresh(reloadFunds);

  const focusedTxn = txnId
    ? txns.find((item) => item.id.toLowerCase() === txnId)
    : undefined;
  const canViewRelatedOrder =
    access.canViewAllOrders ||
    (access.canViewOwnOrders &&
      currentUser &&
      userId &&
      currentUser.toLowerCase() === userId.toLowerCase());

  function renderBusiness(row: BalanceTxnDto) {
    const depositId = row.depositOrderId;
    const businessId = row.businessId;
    let label = row.businessType ?? '—';
    let id: string | undefined;
    let href: string | undefined;
    if (row.action === 'DepositCredit') {
      label = '账户充值';
      id = depositId ?? undefined;
      if (depositId && access.canViewDepositRecords)
        href = depositRecordUrl(depositId);
    } else if (row.businessType === 'Order') {
      label = '业务订单';
      id = businessId;
      if (businessId && canViewRelatedOrder) href = orderRecordUrl(businessId);
    } else if (row.businessType === 'Withdraw') {
      label = '提现单';
      id = businessId;
      if (businessId && access.canAdminWithdrawals)
        href = withdrawalRecordUrl(businessId);
    }
    if (href)
      return (
        <Link to={href} aria-label={`查看${label} ${id}`}>
          {label}
        </Link>
      );
    return id ? (
      <Text copyable={{ text: id }}>
        {label} {id}
      </Text>
    ) : (
      label
    );
  }

  const columns = [
    {
      title: '时间',
      dataIndex: 'creationTime',
      width: 150,
      render: (v: string) => fmtTime(v),
    },
    {
      title: '动作',
      dataIndex: 'action',
      width: 110,
      render: (v: string) => {
        const cfg = BALANCE_ACTION_LABELS[v] ?? { color: 'default', text: v };
        return <Tag color={cfg.color}>{cfg.text}</Tag>;
      },
    },
    {
      title: '币种',
      dataIndex: 'currency',
      width: 80,
      render: (v: string) => <Tag color="cyan">{currencyLabel(v)}</Tag>,
    },
    {
      title: '金额',
      dataIndex: 'amount',
      width: 130,
      align: 'right' as const,
      render: (v: string) => (
        <Text type={v.startsWith('-') ? 'danger' : undefined}>{v}</Text>
      ),
    },
    {
      title: '变动后余额',
      dataIndex: 'balanceAfter',
      width: 130,
      align: 'right' as const,
    },
    {
      title: '业务',
      dataIndex: 'businessType',
      width: 180,
      ellipsis: true,
      render: (_: string | undefined, row: BalanceTxnDto) =>
        renderBusiness(row),
    },
    {
      title: '链上凭证',
      dataIndex: 'txHash',
      width: 160,
      ellipsis: true,
      render: (v: string | undefined) =>
        v ? (
          <Text
            copyable={{ text: v }}
            style={{ fontFamily: 'monospace', fontSize: 11 }}
          >{`${v.slice(0, 12)}...${v.slice(-6)}`}</Text>
        ) : (
          '-'
        ),
    },
    {
      title: '流水号',
      dataIndex: 'id',
      width: 130,
      render: (v: string) =>
        userId && !txnId ? (
          <Link
            to={
              withdrawalId
                ? withdrawalFundsUrl(userId, withdrawalId, v)
                : memberFundsUrl(userId, v)
            }
            aria-label={`查看余额流水 ${v}`}
          >
            {v.slice(0, 8)}…
          </Link>
        ) : (
          <Tooltip title={v}>
            <Text>{v.slice(0, 8)}…</Text>
          </Tooltip>
        ),
    },
  ];

  const userLabel = (userId && userLabels.current[userId]) || linkedUserId;
  const options =
    userId &&
    userLabel &&
    !userOptions.some((option) => option.value === userId)
      ? [{ label: userLabel, value: userId }, ...userOptions]
      : userOptions;

  function openUser(nextId?: string, label?: string) {
    if (!nextId) {
      if (userId) history.push('/collection/member-funds');
      return;
    }
    const normalized = nextId.trim().toLowerCase();
    if (label) userLabels.current[normalized] = label;
    if (userId === normalized && !txnId && !withdrawalId) return;
    fundsRequest.current += 1;
    setFunds(undefined);
    setLoadFailed(false);
    setLoading(true);

    const url = memberFundsUrl(normalized);
    if (userId) history.replace(url);
    else history.push(url);
  }

  const memberTxnsGridScope = gridScope({ userId, txnId, withdrawalId });
  const recordColumns = toGridColumns<BalanceTxnDto>(columns, {
    kinds: { amount: 'decimal', balanceAfter: 'decimal', creationTime: 'date' },
  });
  const memberTxnsGrid = useSearchableRemoteGrid<BalanceTxnDto>({
    ariaLabel: '会员余额流水',
    getRowId: ({ data }) => String(data.id),
    query: useScopedGridQuery(
      businessGridQueries.memberTxns,
      memberTxnsGridScope,
    ),
    columnDefs: recordColumns,
  });
  const focusedGrid = useSearchableLocalGrid<BalanceTxnDto>({
    ariaLabel: '会员余额流水',
    getRowId: ({ data }) => String(data.id),
    pagination: false,
    rowData: focusedTxn ? [focusedTxn] : [],
    columnDefs: recordColumns,
    keywordFields: ['action', 'currency', 'txHash'],
  });
  const visibleGrid = txnId ? focusedGrid : memberTxnsGrid;
  refreshTxns.current = memberTxnsGrid.refresh;
  return (
    <PageContainer title="用户余额">
      <Space orientation="vertical" style={{ width: '100%' }} size={16}>
        {invalidRecordLink && (
          <Alert
            type="warning"
            showIcon
            title="余额流水链接无效"
            action={<Link to="/collection/member-funds">返回用户余额</Link>}
          />
        )}
        {userId && <Link to="/collection/member-funds">返回用户余额</Link>}
        <Card
          title="选择用户"
          size="small"
          extra={
            userId &&
            access.canCreateBalanceAdjustments && (
              <Link
                to={`/finance/balance-adjustments?createFor=${encodeURIComponent(userId)}`}
              >
                申请余额调整
              </Link>
            )
          }
        >
          {searchError && (
            <Alert
              type="error"
              showIcon
              style={{ marginBottom: 8 }}
              title={searchError}
            />
          )}
          {access.canManageUsers ? (
            <Select
              aria-label="用户名或邮箱"
              placeholder="输入用户名或邮箱搜索（至少 2 个字符）"
              style={{ width: 420 }}
              loading={searching}
              value={userId}
              showSearch={{
                filterOption: false,
                onSearch: (keyword) => {
                  const version = ++searchVersion.current;
                  if (searchTimer.current != null) {
                    clearTimeout(searchTimer.current);
                  }
                  setSearchError(null);
                  setUserOptions([]);
                  setSearching(keyword.trim().length >= 2);
                  if (keyword.trim().length < 2) return;
                  searchTimer.current = setTimeout(() => {
                    searchTimer.current = null;
                    void searchUsers(keyword, version);
                  }, 300);
                },
              }}
              onChange={(value, option) => {
                const label =
                  !Array.isArray(option) && typeof option?.label === 'string'
                    ? option.label
                    : undefined;
                openUser(value, label);
              }}
              options={options}
              allowClear
              popupMatchSelectWidth={false}
            />
          ) : (
            <Form<{ userId: string }>
              key={linkedUserId ?? 'manual'}
              initialValues={{ userId: linkedUserId }}
              layout="vertical"
              onFinish={({ userId: value }) => openUser(value.trim())}
              scrollToFirstError={{ focus: true }}
              style={{ maxWidth: 420 }}
            >
              <Form.Item
                name="userId"
                label="用户 ID"
                normalize={(value: string) => value.trim()}
                rules={[
                  { required: true, message: '请输入用户 ID' },
                  {
                    pattern: userIdPattern,
                    message: '请输入带连字符的完整用户 ID',
                  },
                ]}
                validateTrigger="onBlur"
                extra="输入带连字符的完整用户 ID。没有余额记录的用户也可以查询。"
              >
                <Input
                  autoCapitalize="none"
                  autoCorrect="off"
                  spellCheck={false}
                />
              </Form.Item>
              <Button type="primary" htmlType="submit">
                查询余额与流水
              </Button>
            </Form>
          )}
        </Card>

        {userId ? (
          <>
            {loadFailed && (
              <Alert
                type="error"
                showIcon
                title="加载余额和流水失败"
                action={
                  <Button onClick={() => void reloadFunds()}>重试</Button>
                }
              />
            )}
            <Card title="余额" size="small" loading={loading}>
              <Space size={48} wrap>
                {balances.map((b) => (
                  <Space key={b.currency} orientation="vertical" size={0}>
                    <Tag color="cyan">{currencyLabel(b.currency)}</Tag>
                    <Space size={24}>
                      <Statistic
                        title="可用"
                        value={b.balance}
                        formatter={() => b.balance}
                        styles={{ content: { fontSize: 18 } }}
                      />
                      <Statistic
                        title="冻结"
                        value={b.frozen}
                        formatter={() => b.frozen}
                        styles={{ content: { fontSize: 18 } }}
                      />
                      <Statistic
                        title="合计"
                        value={b.total}
                        formatter={() => b.total}
                        styles={{ content: { fontSize: 18 } }}
                      />
                    </Space>
                  </Space>
                ))}
              </Space>
            </Card>
            <Card title={withdrawalId ? '提现关联流水' : '余额流水'}>
              {withdrawalId && (
                <Alert
                  type="info"
                  title={
                    <Text copyable={{ text: withdrawalId }}>
                      提现单号 {withdrawalId}
                    </Text>
                  }
                  action={
                    <Link to={memberFundsUrl(userId)}>查看该用户全部流水</Link>
                  }
                  style={{ marginBottom: 12 }}
                />
              )}
              {txnId && (
                <Alert
                  type={focusedTxn || loading ? 'info' : 'warning'}
                  showIcon
                  title={
                    focusedTxn
                      ? `已定位余额流水 ${txnId}`
                      : loading
                        ? '正在定位余额流水'
                        : loadFailed
                          ? '暂无法定位该流水'
                          : '未找到该余额流水'
                  }
                  action={
                    withdrawalId ? (
                      <Link to={withdrawalFundsUrl(userId, withdrawalId)}>
                        查看该提现的全部流水
                      </Link>
                    ) : (
                      <Link to={memberFundsUrl(userId)}>
                        查看该用户全部流水
                      </Link>
                    )
                  }
                  style={{ marginBottom: 12 }}
                />
              )}
              {visibleGrid.searchBox}
              <AgGridReact<BalanceTxnDto> {...visibleGrid.gridProps} />
            </Card>
          </>
        ) : (
          <MemberBalanceAccounts
            canManageUsers={access.canManageUsers}
            canAdjust={access.canCreateBalanceAdjustments}
          />
        )}
      </Space>
    </PageContainer>
  );
}

const getAccountRowId = ({ data }: { data: MemberBalanceGridRowDto }) =>
  data.userId;

function MemberBalanceAccounts({
  canManageUsers,
  canAdjust,
}: {
  canManageUsers: boolean;
  canAdjust: boolean;
}) {
  const columnDefs = useMemo<RemoteColumn<MemberBalanceGridRowDto>[]>(
    () => [
      {
        field: 'userId',
        headerName: '用户 ID',
        headerTooltip:
          '列筛选需要完整用户 ID，搜索余额账户时可以输入 ID 片段。',
        kind: 'uuid',
        width: 270,
        cellRenderer: ({
          data,
        }: CustomCellRendererProps<MemberBalanceGridRowDto>) =>
          data && (
            <Space>
              <Text
                copyable={{ text: data.userId }}
                title={data.userId}
                style={{ fontFamily: 'monospace' }}
              >
                {data.userId.slice(0, 8)}…
              </Text>
              {canManageUsers && data.userName ? (
                <span>
                  {data.userName}
                  {data.email ? ` (${data.email})` : ''}
                </span>
              ) : null}
            </Space>
          ),
      },
      {
        field: 'usdtBalance',
        headerName: 'USDT 可用',
        kind: 'decimal',
        minWidth: 145,
        flex: 1,
      },
      {
        field: 'usdtFrozen',
        headerName: 'USDT 冻结',
        kind: 'decimal',
        minWidth: 145,
        flex: 1,
      },
      {
        field: 'usdcBalance',
        headerName: 'USDC 可用',
        kind: 'decimal',
        minWidth: 145,
        flex: 1,
      },
      {
        field: 'usdcFrozen',
        headerName: 'USDC 冻结',
        kind: 'decimal',
        minWidth: 145,
        flex: 1,
      },
      {
        field: 'updatedAt',
        headerName: '最近更新（UTC）',
        kind: 'date',
        width: 190,
        sort: 'desc',
        valueFormatter: ({ value }) =>
          value ? String(value).replace('T', ' ').slice(0, 19) : '',
      },
      {
        colId: 'actions',
        headerName: '操作',
        kind: 'display',
        width: 150,
        cellRenderer: ({
          data,
        }: CustomCellRendererProps<MemberBalanceGridRowDto>) =>
          data && (
            <Space>
              <Link to={memberFundsUrl(data.userId)}>流水</Link>
              {canAdjust && (
                <Link
                  to={`/finance/balance-adjustments?createFor=${encodeURIComponent(data.userId)}`}
                >
                  申请调整
                </Link>
              )}
            </Space>
          ),
      },
    ],
    [canAdjust, canManageUsers],
  );

  const { gridProps, searchBox } =
    useSearchableRemoteGrid<MemberBalanceGridRowDto>({
      columnDefs,
      query: queryMemberBalanceAccounts,
      getRowId: getAccountRowId,
      ariaLabel: '会员资金账户',
      search: { label: '搜索余额账户', placeholder: '输入关键词搜索余额账户' },
    });
  return (
    <Space orientation="vertical" size={8} style={{ width: '100%' }}>
      {searchBox}
      <AgGridReact<MemberBalanceGridRowDto> {...gridProps} />
    </Space>
  );
}
