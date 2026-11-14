import { PageContainer } from '@ant-design/pro-components';
import { Link, useAccess } from '@umijs/max';
import {
  Alert,
  Button,
  Card,
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
  type MemberBalanceDto,
  getMemberBalances,
  getMemberTxns,
} from '@/abp/collectionAdmin';
import { getUsers } from '@/abp/identity';
import { currencyLabel } from '@/abp/ordering';
import { useResizableColumns } from '@/components/AutoHeightProTable/useResizableColumns';
import { useChainRecordRefresh } from './use-chain-record-refresh';

const { Text } = Typography;

/**
 * 会员资金页（/collection/member-funds，收款查看权限）：
 * identity 用户搜索 → 该用户的 USDT/USDC 余额（可用/冻结/合计）+ 余额流水（充值行带链上凭证）。
 * 与用户侧 my-balances/my-txns 同口径（只读）。
 */
export default function MemberFundsPage() {
  const access = useAccess();
  const [userId, setUserId] = useState<string>();
  const [selectedUserLabel, setSelectedUserLabel] = useState<string>();
  const [userOptions, setUserOptions] = useState<
    { label: string; value: string }[]
  >([]);
  const [searching, setSearching] = useState(false);
  const [balances, setBalances] = useState<MemberBalanceDto[]>([]);
  const [txns, setTxns] = useState<BalanceTxnDto[]>([]);
  const [total, setTotal] = useState(0);
  const [page, setPage] = useState(1);
  const [loading, setLoading] = useState(false);
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
      setSearchError('用户搜索失败（可能缺少身份管理查询权限，或服务不可用）');
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
            skipCount: (page - 1) * pageSize,
            maxResultCount: pageSize,
          }),
        ]);
        if (request !== fundsRequest.current) return;
        setBalances(balanceResult.items ?? []);
        setTxns(txnResult.items ?? []);
        setTotal(txnResult.totalCount ?? 0);
        setLoadFailed(false);
      } catch {
        if (!background && request === fundsRequest.current)
          setLoadFailed(true);
      } finally {
        if (request === fundsRequest.current) setLoading(false);
      }
    },
    [userId, page],
  );
  useEffect(() => {
    void reloadFunds();
    return () => {
      fundsRequest.current++;
    };
  }, [reloadFunds]);
  useChainRecordRefresh(reloadFunds);

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
      width: 100,
      render: (v: string | undefined) => v ?? '-',
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
      render: (v: string) => (
        <Tooltip title={v}>
          <Text style={{ fontFamily: 'monospace', fontSize: 11 }}>
            {`${v.slice(0, 8)}...`}
          </Text>
        </Tooltip>
      ),
    },
  ];

  const {
    columns: resizableColumns,
    components,
    scrollX,
  } = useResizableColumns(columns);
  const options =
    userId &&
    selectedUserLabel &&
    !userOptions.some((option) => option.value === userId)
      ? [{ label: selectedUserLabel, value: userId }, ...userOptions]
      : userOptions;

  return (
    <PageContainer title="用户余额与流水">
      <Space orientation="vertical" style={{ width: '100%' }} size={16}>
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
          <Select
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
            onChange={(v, option) => {
              if (v === userId) return;
              setSelectedUserLabel(
                !Array.isArray(option) ? option?.label : undefined,
              );
              setBalances([]);
              setTxns([]);
              setTotal(0);
              setLoadFailed(false);
              setLoading(!!v);
              setUserId(v);
              setPage(1);
            }}
            options={options}
            allowClear
            popupMatchSelectWidth={false}
          />
        </Card>

        {userId && (
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
            <Card title="余额流水">
              <Table<BalanceTxnDto>
                rowKey="id"
                loading={loading}
                dataSource={txns}
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
          </>
        )}
      </Space>
    </PageContainer>
  );
}
