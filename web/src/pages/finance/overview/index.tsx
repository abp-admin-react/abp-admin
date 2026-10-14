import { financeHelpHeader } from '@/components/FinanceHelp';
import { PageContainer, ProCard, StatisticCard } from '@ant-design/pro-components';
import { Alert, Button, Space, Spin } from 'antd';
import React, { useEffect, useState } from 'react';
import { FinanceEmpty, FinanceStateView, useFinanceLoad } from '@/components/FinanceState';
import { history, useAccess } from '@umijs/max';
import { type FinOverviewDto, getFinanceOverview } from '@/abp/finance';

const OverviewPage: React.FC = () => {
  const access = useAccess();
  const [data, setData] = useState<FinOverviewDto>();
  const [loading, setLoading] = useState(true);

  const load = useFinanceLoad();

  useEffect(() => {
    setLoading(true);
    load
      .guard(() => getFinanceOverview())
      .then((d) => setData(d ?? undefined))
      .finally(() => setLoading(false));
  }, [load.nonce]);

  const fmt = (n?: number) =>
    new Intl.NumberFormat('zh-CN', {
      minimumFractionDigits: 2,
      maximumFractionDigits: 2,
    }).format(n ?? 0);

  return (
    <PageContainer {...financeHelpHeader('A1', '资金总览')}>
      <FinanceStateView state={load.state} errorText={load.errorText} onRetry={load.retry}>
      <Spin spinning={loading}>
        {!loading && !data ? (
          <FinanceEmpty text="暂无资金数据" />
        ) : (
        <>
        {data && !data.balanced && (
          <Alert
            type="error"
            showIcon
            style={{ marginBottom: 16 }}
            message="账本借贷不平衡"
            action={
              access.canViewFinanceLedger ? (
                <Button
                  size="small"
                  type="link"
                  onClick={() => history.push('/finance/reconcile')}
                >
                  去对账
                </Button>
              ) : null
            }
          />
        )}
        {(data?.unreconciledCount ?? 0) > 0 && (
          <Alert
            type="warning"
            showIcon
            style={{ marginBottom: 16 }}
            message={`未对账 ${data?.unreconciledCount} 笔`}
            action={
              <Button
                size="small"
                type="link"
                onClick={() => history.push('/finance/reconcile')}
              >
                查看
              </Button>
            }
          />
        )}
        <StatisticCard.Group direction="row">
          <StatisticCard statistic={{ title: '今日收款', value: fmt(data?.todayIn) }} />
          <StatisticCard
            statistic={{ title: '今日退款/支出', value: fmt(data?.todayOut) }}
          />
          <StatisticCard
            statistic={{
              title: '借贷平衡',
              value: data?.balanced ? '平衡' : '不平衡',
            }}
          />
          <StatisticCard
            statistic={{ title: '未对账笔数', value: data?.unreconciledCount ?? 0 }}
          />
        </StatisticCard.Group>
        <ProCard title="余额与预收" style={{ marginTop: 16 }} gutter={16} wrap>
          <StatisticCard
            statistic={{ title: '可用合计', value: fmt(data?.availableTotal) }}
          />
          <StatisticCard
            statistic={{ title: '冻结合计', value: fmt(data?.frozenTotal) }}
          />
          <StatisticCard
            statistic={{ title: '预收合计', value: fmt(data?.prepaymentTotal) }}
          />
          <StatisticCard
            statistic={{ title: '本期确认收入', value: fmt(data?.recognizedIncome) }}
          />
        </ProCard>
        <Space style={{ marginTop: 16 }}>
          {access.canViewFinanceVouchers && (
            <Button onClick={() => history.push('/finance/vouchers')}>凭证与调账</Button>
          )}
          {access.canViewFinanceBalances && (
            <Button onClick={() => history.push('/finance/balances')}>余额投影</Button>
          )}
        </Space>
        </>
        )}
      </Spin>
      </FinanceStateView>
    </PageContainer>
  );
};

export default OverviewPage;
