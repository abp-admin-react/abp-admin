import {
  PageContainer,
  ProCard,
  StatisticCard,
} from '@ant-design/pro-components';
import { Link, useAccess } from '@umijs/max';
import { Alert, Spin } from 'antd';
import { useEffect, useState } from 'react';
import { type FinOverviewDto, getFinanceOverview } from '@/abp/finance';
import { financeHelpHeader } from '@/components/FinanceHelp';
import {
  FinanceEmpty,
  FinanceStateView,
  useFinanceLoad,
} from '@/components/FinanceState';

const amounts = new Intl.NumberFormat('zh-CN', {
  minimumFractionDigits: 2,
  maximumFractionDigits: 2,
});

export default function OverviewPage() {
  const access = useAccess();
  const load = useFinanceLoad();
  const { guard, nonce } = load;
  const [data, setData] = useState<FinOverviewDto>();
  const [loading, setLoading] = useState(true);

  useEffect(() => {
    let active = true;
    setLoading(true);
    guard(getFinanceOverview)
      .then((overview) => {
        if (active) setData(overview);
      })
      .finally(() => {
        if (active) setLoading(false);
      });
    return () => {
      active = false;
    };
  }, [guard, nonce]);

  const hasDifference = !!data && (!data.balanced || data.unreconciledCount > 0);

  return (
    <PageContainer {...financeHelpHeader('A1', '会计概览')}>
      <FinanceStateView state={load.state} errorText={load.errorText} onRetry={load.retry}>
        <Spin spinning={loading}>
          {data ? (
            <>
              {hasDifference && (
                <Alert
                  type={data.balanced ? 'warning' : 'error'}
                  showIcon
                  style={{ marginBottom: 16 }}
                  title={
                    data.balanced
                      ? `有差异的对账批次 ${data.unreconciledCount} 次`
                      : '账簿借贷不平衡'
                  }
                  action={
                    access.canViewFinanceReconcile && (
                      <Link to="/finance/reconcile">查看账务对账</Link>
                    )
                  }
                />
              )}
              <StatisticCard.Group direction="row">
                <StatisticCard
                  statistic={{ title: '今日收款', value: amounts.format(data.todayIn) }}
                />
                <StatisticCard
                  statistic={{ title: '今日退款与支出', value: amounts.format(data.todayOut) }}
                />
                <StatisticCard
                  statistic={{ title: '借贷试算', value: data.balanced ? '平衡' : '不平衡' }}
                />
                <StatisticCard
                  statistic={{ title: '有差异的对账批次', value: data.unreconciledCount }}
                />
              </StatisticCard.Group>
              <ProCard title="账面余额与收入" style={{ marginTop: 16 }} gutter={16} wrap>
                <StatisticCard
                  statistic={{ title: '可用余额合计', value: amounts.format(data.availableTotal) }}
                />
                <StatisticCard
                  statistic={{ title: '冻结余额合计', value: amounts.format(data.frozenTotal) }}
                />
                <StatisticCard
                  statistic={{ title: '预收款合计', value: amounts.format(data.prepaymentTotal) }}
                />
                <StatisticCard
                  statistic={{ title: '已确认收入', value: amounts.format(data.recognizedIncome) }}
                />
              </ProCard>
            </>
          ) : (
            !loading && <FinanceEmpty text="暂无账务数据" />
          )}
        </Spin>
      </FinanceStateView>
    </PageContainer>
  );
}
