import { PageContainer } from '@ant-design/pro-components';
import {
  Alert,
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
import { useResizableColumns } from '@/components/AutoHeightProTable/useResizableColumns';

const { Text } = Typography;

/**
 * 会员资金页（/collection/member-funds，收款查看权限）：
 * identity 用户搜索 → 该用户的 USDT/USDC 余额（可用/冻结/合计）+ 余额流水（充值行带链上凭证）。
 * 与用户侧 my-balances/my-txns 同口径（只读）。
 */
export default function MemberFundsPage() {
  const [userId, setUserId] = useState<string>();
  const [userOptions, setUserOptions] = useState<
    { label: string; value: string }[]
  >([]);
  const [searching, setSearching] = useState(false);
  const [balances, setBalances] = useState<MemberBalanceDto[]>([]);
  const [txns, setTxns] = useState<BalanceTxnDto[]>([]);
  const [total, setTotal] = useState(0);
  const [page, setPage] = useState(1);
  const [loading, setLoading] = useState(false);
  const pageSize = 20;

  const [searchError, setSearchError] = useState<string | null>(null);
  const searchTimer = useRef<ReturnType<typeof setTimeout> | null>(null);
  useEffect(
    () => () => {
      if (searchTimer.current != null) {
        clearTimeout(searchTimer.current);
      }
    },
    [],
  );
  const searchUsers = useCallback(async (keyword: string) => {
    // 至少 2 字符才搜（每键一次 DB LIKE，不设门槛就是噪声）
    if (keyword.trim().length < 2) {
      setUserOptions([]);
      return;
    }
    setSearching(true);
    setSearchError(null);
    try {
      const result = await getUsers({ filter: keyword.trim(), pageSize: 20 });
      setUserOptions(
        result.items.map((u) => ({
          label: `${u.userName}${u.email ? ` (${u.email})` : ''}`,
          value: u.id,
        })),
      );
    } catch {
      // 用户查询需要身份管理权限（AbpIdentity.Users）——无权限时明示而不是空下拉装死
      setUserOptions([]);
      setSearchError('用户搜索失败（可能缺少身份管理查询权限，或服务不可用）');
    } finally {
      setSearching(false);
    }
  }, []);

  const reload = useCallback(async () => {
    if (!userId) {
      setBalances([]);
      setTxns([]);
      setTotal(0);
      return;
    }
    setLoading(true);
    try {
      const [balanceResult, txnResult] = await Promise.all([
        getMemberBalances(userId),
        getMemberTxns({
          userId,
          skipCount: (page - 1) * pageSize,
          maxResultCount: pageSize,
        }),
      ]);
      setBalances(balanceResult.items);
      setTxns(txnResult.items);
      setTotal(txnResult.totalCount);
    } catch {
      /* interceptor */
    } finally {
      setLoading(false);
    }
  }, [userId, page]);

  useEffect(() => {
    reload();
  }, [reload]);

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
      render: (v: string) => <Tag color="cyan">{v}</Tag>,
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
      title: '余额（after）',
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

  const { columns: resizableColumns, components, scrollX } =
    useResizableColumns(columns);

  return (
    <PageContainer>
      <Space direction="vertical" style={{ width: '100%' }} size={16}>
        <Alert
          type="info"
          showIcon
          message="会员资金（只读）：按用户名搜索后查看该用户的稳定币余额与流水；金额与用户侧「我的余额」同口径。"
        />
        <Card title="选择用户" size="small">
          {searchError && (
            <Alert
              type="error"
              showIcon
              style={{ marginBottom: 8 }}
              message={searchError}
            />
          )}
          <Select
            showSearch
            filterOption={false}
            placeholder="输入用户名或邮箱搜索（至少 2 个字符）"
            style={{ width: 420 }}
            loading={searching}
            value={userId}
            onSearch={(kw: string) => {
              if (searchTimer.current != null) {
                clearTimeout(searchTimer.current);
              }
              searchTimer.current = setTimeout(() => {
                searchTimer.current = null;
                searchUsers(kw);
              }, 300);
            }}
            onChange={(v) => {
              setUserId(v);
              setPage(1);
            }}
            options={userOptions}
            allowClear
            popupMatchSelectWidth={false}
          />
        </Card>

        {userId && (
          <>
            <Card title="余额" size="small" loading={loading}>
              <Space size={48} wrap>
                {balances.map((b) => (
                  <Space key={b.currency} direction="vertical" size={0}>
                    <Tag color="cyan">{b.currency}</Tag>
                    <Space size={24}>
                      <Statistic
                        title="可用"
                        value={b.balance}
                        precision={2}
                        valueStyle={{ fontSize: 18 }}
                      />
                      <Statistic
                        title="冻结"
                        value={b.frozen}
                        precision={2}
                        valueStyle={{ fontSize: 18 }}
                      />
                      <Statistic
                        title="合计"
                        value={b.total}
                        precision={2}
                        valueStyle={{ fontSize: 18 }}
                      />
                    </Space>
                  </Space>
                ))}
              </Space>
            </Card>
            <Card title="余额流水（充值行带链上凭证）">
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
