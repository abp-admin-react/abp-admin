import { PageContainer } from '@ant-design/pro-components';
import { history, Link, useAccess, useLocation, useModel } from '@umijs/max';
import {
  Alert,
  Button,
  Card,
  Form,
  Input,
  Select,
  Space,
  Statistic,
  Table,
  Tag,
  Tooltip,
  Typography,
} from 'antd';
import React, { useCallback, useEffect, useRef, useState } from 'react';
import {
  BALANCE_ACTION_LABELS,
  type BalanceTxnDto,
  getMemberBalanceAccounts,
  getMemberBalances,
  getMemberTxns,
  type MemberBalanceAccountDto,
  type MemberBalanceDto,
} from '@/abp/collectionAdmin';
import { getUser, getUsers } from '@/abp/identity';
import { currencyLabel } from '@/abp/ordering';
import AutoHeightProTable from '@/components/AutoHeightProTable';
import { useResizableColumns } from '@/components/AutoHeightProTable/useResizableColumns';
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

/**
 * 用户余额（/collection/member-funds，收款查看权限）：
 * 无 userId 时列出已有余额账户；?userId= 查看该用户余额与流水。
 * 有用户目录权限时可按用户名或邮箱搜索，否则输入用户 ID。搜索到的用户即使余额为 0 也进入详情。
 * 与用户侧 my-balances/my-txns 同口径（只读）。
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
  const [pagination, setPagination] = useState<{ key: string; page: number }>();
  const page = pagination?.key === paginationKey ? pagination.page : 1;
  const queryKey = `${paginationKey}:${txnId ?? ''}:${txnId ? 1 : page}`;
  const balances = funds?.userId === userId ? (funds?.balances ?? []) : [];
  const txns = funds?.queryKey === queryKey ? funds.txns : [];
  const total = funds?.queryKey === queryKey ? funds.total : 0;
  const [loading, setLoading] = useState(!!linkedUserId);
  const [loadFailed, setLoadFailed] = useState(false);
  const pageSize = 20;
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
        const [balanceResult, txnResult] = await Promise.all([
          getMemberBalances(userId),
          getMemberTxns({
            userId,
            skipCount: txnId ? 0 : (page - 1) * pageSize,
            maxResultCount: txnId ? 1 : pageSize,
            ...(txnId ? { txnId } : {}),
            ...(withdrawalId ? { withdrawalId } : {}),
          }),
        ]);
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
    [userId, page, queryKey, txnId, withdrawalId],
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

  const fmtTime = (v: string) => {
    const d = new Date(v);
    return Number.isNaN(d.getTime()) ? v : d.toLocaleString();
  };

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

  const {
    columns: resizableColumns,
    components,
    scrollX,
  } = useResizableColumns(columns);
  const loadAccounts = useCallback(
    async (params: { current?: number; pageSize?: number }) => {
      const maxResultCount = params.pageSize ?? pageSize;
      const skipCount = ((params.current ?? 1) - 1) * maxResultCount;
      try {
        const result = await getMemberBalanceAccounts({
          skipCount,
          maxResultCount,
        });
        const items = result.items ?? [];
        const data = access.canManageUsers
          ? await Promise.all(
              items.map(async (item) => {
                try {
                  const user = await getUser(item.userId);
                  const userName = `${user.userName}${user.email ? ` (${user.email})` : ''}`;
                  return { ...item, userName };
                } catch {
                  return item;
                }
              }),
            )
          : items;
        return { data, total: result.totalCount ?? 0, success: true as const };
      } catch {
        return { data: [], total: 0, success: false as const };
      }
    },
    [access.canManageUsers],
  );

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
    setPagination(undefined);
    const url = memberFundsUrl(normalized);
    if (userId) history.replace(url);
    else history.push(url);
  }

  const accountColumns = [
    {
      title: '用户',
      dataIndex: 'userId',
      width: 220,
      render: (
        _: string,
        row: MemberBalanceAccountDto & { userName?: string },
      ) => (
        <Space orientation="vertical" size={0}>
          {row.userName ? <span>{row.userName}</span> : null}
          <Text
            copyable={{ text: row.userId }}
            style={{ fontFamily: 'monospace' }}
          >
            {row.userId.slice(0, 8)}…
          </Text>
        </Space>
      ),
    },
    {
      title: 'USDT 可用',
      dataIndex: 'usdtBalance',
      width: 120,
      align: 'right' as const,
    },
    {
      title: 'USDT 冻结',
      dataIndex: 'usdtFrozen',
      width: 120,
      align: 'right' as const,
    },
    {
      title: 'USDC 可用',
      dataIndex: 'usdcBalance',
      width: 120,
      align: 'right' as const,
    },
    {
      title: 'USDC 冻结',
      dataIndex: 'usdcFrozen',
      width: 120,
      align: 'right' as const,
    },
    {
      title: '最近更新',
      dataIndex: 'updatedAt',
      width: 170,
      render: (value: string) => fmtTime(value),
    },
    {
      title: '操作',
      width: 140,
      render: (_: unknown, row: MemberBalanceAccountDto) => (
        <Space size={8}>
          <Link to={memberFundsUrl(row.userId)}>流水</Link>
          {access.canCreateBalanceAdjustments && (
            <Link
              to={`/finance/balance-adjustments?createFor=${encodeURIComponent(row.userId)}`}
            >
              申请调整
            </Link>
          )}
        </Space>
      ),
    },
  ];

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
              <Table<BalanceTxnDto>
                rowKey="id"
                loading={loading}
                dataSource={txnId ? (focusedTxn ? [focusedTxn] : []) : txns}
                columns={resizableColumns}
                components={components}
                scroll={{ x: scrollX }}
                size="small"
                locale={
                  withdrawalId && !txnId && !loading && !loadFailed
                    ? { emptyText: '未找到该提现单的关联流水' }
                    : undefined
                }
                pagination={
                  !txnId && {
                    current: page,
                    pageSize,
                    total,
                    showSizeChanger: false,
                    onChange: (p) =>
                      setPagination({ key: paginationKey, page: p }),
                  }
                }
              />
            </Card>
          </>
        ) : (
          <AutoHeightProTable<MemberBalanceAccountDto & { userName?: string }>
            rowKey="userId"
            search={false}
            columns={accountColumns}
            request={loadAccounts}
            pagination={{ pageSize, showSizeChanger: false }}
            locale={{ emptyText: '还没有用户余额' }}
          />
        )}
      </Space>
    </PageContainer>
  );
}
