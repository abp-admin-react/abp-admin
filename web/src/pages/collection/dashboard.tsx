import {
  PageContainer,
  ProCard,
  StatisticCard,
} from '@ant-design/pro-components';
import { Link, useAccess } from '@umijs/max';
import { AgGridReact } from 'ag-grid-react';
import { Alert, Card, Space, Spin, Tag, Typography } from 'antd';
import { useCallback, useEffect, useState } from 'react';
import {
  getTreasuryDashboard,
  type StatusBucketDto,
  type TreasuryDashboardDto,
} from '@/abp/collectionAdmin';
import { toGridColumns, useSearchableLocalGrid } from '@/components/remoteGrid';
import AnkrBalanceCard from './AnkrBalanceCard';

const { Text } = Typography;

function fmtTime(value?: string | null) {
  if (!value) return '-';
  const date = new Date(value);
  return Number.isNaN(date.getTime()) ? value : date.toLocaleString();
}

/**
 * 链上监控总览（/collection/dashboard）：汇总充提、手续费、用户负债和储备余额。
 * 「今日」按 UTC 计算，与当日笔数限额口径一致。
 */
export default function CollectionDashboardPage({
  embedded = false,
}: {
  embedded?: boolean;
} = {}) {
  const access = useAccess();
  const [data, setData] = useState<TreasuryDashboardDto>();
  const [_loading, setLoading] = useState(false);
  const [loadError, setLoadError] = useState(false);

  const reload = useCallback(async () => {
    setLoading(true);
    try {
      setData(await getTreasuryDashboard());
      setLoadError(false);
    } catch {
      setLoadError(true); // 403/5xx 不静默吞：无限骨架比错误提示更糟
    } finally {
      setLoading(false);
    }
  }, []);

  useEffect(() => {
    reload();
    // 30s 轮询仅在页面可见时跑（后台标签页不打最贵的聚合端点），切回立即刷新
    const timer = setInterval(() => {
      if (!document.hidden) {
        reload();
      }
    }, 30_000);
    const onVisible = () => {
      if (!document.hidden) {
        reload();
      }
    };
    document.addEventListener('visibilitychange', onVisible);
    return () => {
      clearInterval(timer);
      document.removeEventListener('visibilitychange', onVisible);
    };
  }, [reload]);

  const statusColumns = [
    { title: '状态', dataIndex: 'status', width: 140 },
    { title: '单数', dataIndex: 'count', width: 80, align: 'right' as const },
    {
      title: '金额合计',
      dataIndex: 'total',
      width: 140,
      align: 'right' as const,
    },
  ];
  const withdrawColumns = [
    { title: '状态', dataIndex: 'status', width: 140 },
    { title: '币种', dataIndex: 'currency', width: 90 },
    { title: '单数', dataIndex: 'count', width: 80, align: 'right' as const },
    {
      title: '金额合计',
      dataIndex: 'total',
      width: 140,
      align: 'right' as const,
    },
  ];

  const detailGrid2 = useSearchableLocalGrid<StatusBucketDto>({
    ariaLabel: '提现状态统计',
    rowData: data?.withdrawByStatus,
    keywordFields: ['status', 'currency'],
    pagination: false,
    getRowId: ({ data }) => `${data.status}|${data.currency ?? ''}`,
    columnDefs: toGridColumns<StatusBucketDto>(withdrawColumns, {
      kinds: { count: 'number', total: 'decimal' },
    }),
  });
  const detailGrid1 = useSearchableLocalGrid<StatusBucketDto>({
    ariaLabel: '充值状态统计',
    rowData: data?.depositByStatus,
    keywordFields: ['status', 'currency'],
    pagination: false,
    getRowId: ({ data }) => String(data.status),
    columnDefs: toGridColumns<StatusBucketDto>(statusColumns, {
      kinds: { count: 'number', total: 'decimal' },
    }),
  });
  const content = (
    <Space orientation="vertical" style={{ width: '100%' }} size={16}>
      <Alert
        type="info"
        showIcon
        title="充提统计每 30 秒自动刷新，今日数据按 UTC 日期统计。"
      />
      {access.canViewCollections && (
        <AnkrBalanceCard
          unconfiguredAction={
            access.canAdminNetwork &&
            access.canConfigureCollectionNetwork && (
              <Link to="/web3/operations?view=overview&settings=providers">
                前往第三方扫链
              </Link>
            )
          }
        />
      )}
      {loadError && !data && (
        <Alert
          type="error"
          showIcon
          title="链上监控数据加载失败，30 秒后自动重试。"
        />
      )}
      {!data ? (
        <Card loading={!loadError}>{loadError ? null : <Spin />}</Card>
      ) : (
        <>
          <StatisticCard.Group direction="row">
            <StatisticCard
              statistic={{
                title: `今日充值（${data.depositTodayCount} 笔）`,
                value: data.depositTodayAmount,
              }}
            />
            <StatisticCard.Divider />
            <StatisticCard
              statistic={{
                title: `今日提现（${data.withdrawTodayCount} 笔）`,
                value: data.withdrawTodayAmount,
              }}
            />
            <StatisticCard.Divider />
            <StatisticCard
              statistic={{
                title: '在途提现净额',
                value:
                  data.withdrawInFlight && data.withdrawInFlight.length > 1
                    ? data.withdrawInFlight
                        .map((row) => `${row.currency} ${row.amount}`)
                        .join(' / ')
                    : data.withdrawInFlightAmount,
                description: '审核后未打款且未退款，含广播失败',
              }}
            />
            <StatisticCard.Divider />
            <StatisticCard
              statistic={{
                title: '大额门持有',
                value: data.heldDepositTotal,
                description: `${data.heldDepositCount} 笔等放行`,
              }}
            />
          </StatisticCard.Group>

          <ProCard title="手续费收入与用户负债" gutter={16} wrap>
            <ProCard colSpan={{ xs: 24, md: 6 }}>
              <StatisticCard
                statistic={{
                  title: '充值手续费累计',
                  value: data.depositFeeTotal,
                  description: '已入账（Swept）单合计',
                }}
              />
            </ProCard>
            <ProCard colSpan={{ xs: 24, md: 6 }}>
              <StatisticCard
                statistic={{
                  title: '提现手续费累计',
                  value: data.withdrawFeeTotal,
                  description: '已完成（Completed）单合计',
                }}
              />
            </ProCard>
            {data.userBalances.map((b) => (
              <ProCard key={b.currency} colSpan={{ xs: 24, md: 6 }}>
                <StatisticCard
                  statistic={{
                    title: `用户 ${b.currency} 合计（平台负债）`,
                    value: b.balance,
                    precision: 2,
                    description: `冻结 ${b.frozen}`,
                  }}
                />
              </ProCard>
            ))}
          </ProCard>

          <ProCard title="储备余额核对" gutter={16} wrap>
            {data.vaultChecks.length === 0 ? (
              <Text type="secondary">暂无每日核对记录</Text>
            ) : (
              data.vaultChecks.map((v) => {
                const negative = v.difference.startsWith('-');
                return (
                  <ProCard key={v.symbol} colSpan={{ xs: 24, md: 8 }}>
                    <StatisticCard
                      statistic={{
                        title: `${v.symbol} 差额（储备 − 用户余额）`,
                        value: v.difference,
                        precision: 2,
                        description: (
                          <Space size={8}>
                            <span>储备 {v.vaultBalance}</span>
                            <span>用户余额 {v.userTotal}</span>
                            {negative && <Tag color="error">缺口</Tag>}
                            <span>{fmtTime(v.checkedAt)}</span>
                          </Space>
                        ),
                      }}
                    />
                  </ProCard>
                );
              })
            )}
          </ProCard>

          <ProCard gutter={16} wrap>
            <ProCard
              colSpan={{ xs: 24, md: 12 }}
              title="充值单按状态"
              headerBordered
            >
              {detailGrid1.searchBox}
              <AgGridReact<StatusBucketDto> {...detailGrid1.gridProps} />
            </ProCard>
            <ProCard
              colSpan={{ xs: 24, md: 12 }}
              title="提现单按状态"
              headerBordered
            >
              {detailGrid2.searchBox}
              <AgGridReact<StatusBucketDto> {...detailGrid2.gridProps} />
            </ProCard>
          </ProCard>

          <Text type="secondary">
            生成于 {fmtTime(data.generatedAt)}（数据为 DB 聚合快照）
          </Text>
        </>
      )}
    </Space>
  );
  if (embedded) return content;
  return (
    <PageContainer
      title="链上监控"
      extra={
        <Space wrap>
          {access.canViewCollections && (
            <Link to="/collection/vault">储备余额核对</Link>
          )}
          {access.canAdminWithdrawals && (
            <Link to="/collection/hot-wallet">提现钱包监控</Link>
          )}
        </Space>
      }
    >
      {content}
    </PageContainer>
  );
}
