import { PageContainer } from '@ant-design/pro-components';
import { ProCard, StatisticCard } from '@ant-design/pro-components';
import { Alert, Card, Space, Spin, Table, Tag, Typography } from 'antd';
import React, { useCallback, useEffect, useState } from 'react';
import {
  type TreasuryDashboardDto,
  getTreasuryDashboard,
} from '@/abp/chainAdmin';
import { useResizableColumns } from '@/components/AutoHeightProTable/useResizableColumns';

const { Text } = Typography;

/**
 * 资金看板页（/collection/dashboard，收款查看权限）：纯 DB 聚合（30s 轮询）——
 * 今日充提、双侧手续费、在途敞口、用户负债、大额门持有、金库核对。
 * 「今日」= UTC 日界（与当日笔数限额同口径）；热钱包余额/熔断在热钱包监控页。
 */
export default function CollectionDashboardPage() {
  const [data, setData] = useState<TreasuryDashboardDto>();
  const [loading, setLoading] = useState(false);

  const reload = useCallback(async () => {
    setLoading(true);
    try {
      setData(await getTreasuryDashboard());
    } catch {
      /* interceptor */
    } finally {
      setLoading(false);
    }
  }, []);

  useEffect(() => {
    reload();
    const timer = setInterval(reload, 30_000);
    return () => clearInterval(timer);
  }, [reload]);

  const fmtTime = (v?: string) => {
    if (!v) return '-';
    const d = new Date(v);
    return Number.isNaN(d.getTime()) ? v : d.toLocaleString();
  };

  const statusColumns = [
    { title: '状态', dataIndex: 'status', width: 140 },
    { title: '单数', dataIndex: 'count', width: 80, align: 'right' as const },
    { title: '金额合计', dataIndex: 'total', width: 140, align: 'right' as const },
  ];
  const depositTable = useResizableColumns(statusColumns);
  const withdrawTable = useResizableColumns(statusColumns);

  return (
    <PageContainer>
      <Space direction="vertical" style={{ width: '100%' }} size={16}>
        <Alert
          type="info"
          showIcon
          message="资金看板（30 秒自动刷新；今日按 UTC 日界）。热钱包余额/熔断与逐笔异常在「热钱包监控」页。"
        />
        {!data ? (
          <Card loading>
            <Spin />
          </Card>
        ) : (
          <>
            <StatisticCard.Group direction="row">
              <StatisticCard
                statistic={{
                  title: `今日充值（${data.depositTodayCount} 笔）`,
                  value: data.depositTodayAmount,
                  precision: 2,
                }}
              />
              <StatisticCard.Divider />
              <StatisticCard
                statistic={{
                  title: `今日提现（${data.withdrawTodayCount} 笔）`,
                  value: data.withdrawTodayAmount,
                  precision: 2,
                }}
              />
              <StatisticCard.Divider />
              <StatisticCard
                statistic={{
                  title: '在途提现净额',
                  value: data.withdrawInFlightAmount,
                  precision: 2,
                  description: '待审+待广播+广播中（热钱包待付敞口）',
                }}
              />
              <StatisticCard.Divider />
              <StatisticCard
                statistic={{
                  title: '大额门持有',
                  value: data.heldDepositTotal,
                  precision: 2,
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
                    precision: 2,
                    description: '已入账（Swept）单合计',
                  }}
                />
              </ProCard>
              <ProCard colSpan={{ xs: 24, md: 6 }}>
                <StatisticCard
                  statistic={{
                    title: '提现手续费累计',
                    value: data.withdrawFeeTotal,
                    precision: 2,
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

            <ProCard title="金库核对（各币最新留痕）" gutter={16} wrap>
              {data.vaultChecks.length === 0 ? (
                <Text type="secondary">暂无核对记录（每日作业留痕）</Text>
              ) : (
                data.vaultChecks.map((v) => {
                  const negative = v.difference.startsWith('-');
                  return (
                    <ProCard key={v.symbol} colSpan={{ xs: 24, md: 8 }}>
                      <StatisticCard
                        statistic={{
                          title: `${v.symbol} 差额（金库 − 用户）`,
                          value: v.difference,
                          precision: 2,
                          description: (
                            <Space size={8}>
                              <span>金库 {v.vaultBalance}</span>
                              <span>用户 {v.userTotal}</span>
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
                <Table
                  rowKey="status"
                  dataSource={data.depositByStatus}
                  columns={depositTable.columns}
                  components={depositTable.components}
                  scroll={{ x: depositTable.scrollX }}
                  pagination={false}
                  size="small"
                  loading={loading}
                  locale={{ emptyText: '暂无进款单' }}
                />
              </ProCard>
              <ProCard
                colSpan={{ xs: 24, md: 12 }}
                title="提现单按状态"
                headerBordered
              >
                <Table
                  rowKey="status"
                  dataSource={data.withdrawByStatus}
                  columns={withdrawTable.columns}
                  components={withdrawTable.components}
                  scroll={{ x: withdrawTable.scrollX }}
                  pagination={false}
                  size="small"
                  loading={loading}
                  locale={{ emptyText: '暂无提现单' }}
                />
              </ProCard>
            </ProCard>

            <Text type="secondary">
              生成于 {fmtTime(data.generatedAt)}（数据为 DB 聚合快照）
            </Text>
          </>
        )}
      </Space>
    </PageContainer>
  );
}
