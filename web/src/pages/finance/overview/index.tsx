import {
  PageContainer,
  ProCard,
  StatisticCard,
} from '@ant-design/pro-components';
import { Link, useAccess, useIntl } from '@umijs/max';
import { Alert, Space, Spin, Table } from 'antd';
import { useEffect, useState } from 'react';
import { type FinOverviewDto, getFinanceOverview } from '@/abp/finance';
import {
  ConfigurationLink,
  ConfigurationPage,
  useConfigurationLocation,
} from '@/components/ContextConfiguration';
import FinanceHelpIcon, { financeHelpHeader } from '@/components/FinanceHelp';
import {
  FinanceEmpty,
  FinanceStateView,
  useFinanceLoad,
} from '@/components/FinanceState';
import { SettingsForm } from '../../settings/channel';
import { FinanceAccounts } from '../accounts';
import { FinancePeriod } from '../period';
import { FinancePostingRules } from '../rules';

const amounts = new Intl.NumberFormat('zh-CN', {
  minimumFractionDigits: 2,
  maximumFractionDigits: 2,
});

export default function OverviewPage() {
  const access = useAccess();
  const { formatMessage } = useIntl();
  const { setting, link } = useConfigurationLocation();
  const canViewRecords = access.canViewFinanceOverviewStats;
  const sections = [
    {
      key: 'accounts',
      allowed: access.canViewFinanceAccounts,
      label: formatMessage({ id: 'menu.navigation.finance-accounts', defaultMessage: '会计科目' }),
      content: <FinanceAccounts />,
      help: <FinanceHelpIcon page="A2" />,
    },
    {
      key: 'rules',
      allowed: access.canViewFinanceRules,
      label: formatMessage({ id: 'menu.navigation.finance-rules', defaultMessage: '自动记账规则' }),
      content: <FinancePostingRules />,
      help: <FinanceHelpIcon page="A2b" />,
    },
    {
      key: 'period',
      allowed: access.canViewFinancePeriod,
      label: formatMessage({ id: 'menu.navigation.finance-period', defaultMessage: '会计期间' }),
      content: <FinancePeriod />,
      help: <FinanceHelpIcon page="A4" />,
    },
    {
      key: 'ledger',
      allowed: access.canManagePaymentSettings,
      label: formatMessage({ id: 'menu.navigation.finance-settings', defaultMessage: '高级运行设置' }),
      content: <SettingsForm path="/finance/settings" />,
    },
  ].filter(({ allowed }) => allowed);
  const current = sections.find(({ key }) => key === setting) ?? sections[0];
  const overview = formatMessage({ id: 'menu.navigation.finance-overview', defaultMessage: '会计概览' });
  const configuration = formatMessage({ id: 'menu.navigation.finance-config', defaultMessage: '会计设置' });
  if (current && (setting || !canViewRecords)) {
    return (
      <ConfigurationPage
        parent={overview}
        title={configuration}
        returnTo={canViewRecords ? link() : undefined}
      >
        <nav aria-label={configuration} style={{ marginBottom: 12 }}>
          <Space wrap size="middle">
            {sections.map(({ key, label }) => (
              <Link
                key={key}
                to={link(key)}
                aria-current={key === current.key ? 'page' : undefined}
                style={key === current.key ? { fontWeight: 600, textDecoration: 'underline' } : undefined}
              >
                {label}
              </Link>
            ))}
            {current.help}
          </Space>
        </nav>
        {current.content}
      </ConfigurationPage>
    );
  }

  return canViewRecords ? (
    <OverviewRecords
      title={overview}
      configurationTitle={configuration}
      configurationPath={current && link(current.key)}
    />
  ) : null;
}

function OverviewRecords({ title, configurationTitle, configurationPath }: {
  title: string;
  configurationTitle: string;
  configurationPath?: string;
}) {
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
    <PageContainer
      {...financeHelpHeader('A1', title)}
      extra={configurationPath && (
        <ConfigurationLink to={configurationPath}>{configurationTitle}</ConfigurationLink>
      )}
    >
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
              {(data.currencyTotals?.length ?? 0) > 0 && (
                <ProCard title="按币种" style={{ marginTop: 16 }}>
                  <Table
                    rowKey="currency"
                    pagination={false}
                    size="small"
                    dataSource={data.currencyTotals}
                    columns={[
                      { title: '币种', dataIndex: 'currency' },
                      { title: '今日收款', dataIndex: 'todayIncome' },
                      { title: '今日支出', dataIndex: 'todayExpense' },
                      { title: '可用', dataIndex: 'available' },
                      { title: '冻结', dataIndex: 'frozen' },
                      { title: '预收', dataIndex: 'unearned' },
                      {
                        title: '试算',
                        dataIndex: 'isBalanced',
                        render: (value: boolean) => (value ? '平衡' : '不平衡'),
                      },
                    ]}
                  />
                </ProCard>
              )}
            </>
          ) : (
            !loading && <FinanceEmpty text="暂无账务数据" />
          )}
        </Spin>
      </FinanceStateView>
    </PageContainer>
  );
}
