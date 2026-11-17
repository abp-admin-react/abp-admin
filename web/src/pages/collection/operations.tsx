import { Navigate, useAccess, useLocation } from '@umijs/max';
import { Alert, Space } from 'antd';
import {
  ConfigurationLink,
  ConfigurationPage,
  useConfigurationLocation,
} from '@/components/ContextConfiguration';
import { useModuleEnabled } from '@/hooks/useModuleAvailability';
import { getWeb3ConfigurationSections } from './config';
import CollectionDashboardPage from './dashboard';
import HotWalletWatchPage from './hot-wallet';
import ScanStatus from './scan-status';
import SweepsPage from './sweeps';
import VaultPage from './vault';
import { type Web3Section, Web3Workspace } from './web3-workspace';

export default function Web3OperationsPage() {
  const access = useAccess();
  const acceptsNewWork = useModuleEnabled('Chain');
  const { setting, link } = useConfigurationLocation();
  const { search, hash } = useLocation();
  const params = new URLSearchParams(search);
  const view = params.get('view');
  const status = params.get('status') ?? undefined;
  const depositId = params.get('depositId') || undefined;
  const sweepTaskId = params.get('sweepTaskId') || undefined;

  // 旧的记录直链继续到对应记录页，钱包页面不再加载充值和提现表格。
  if (setting === 'deposit' || (!setting && view === 'deposits')) {
    params.set('view', 'deposits');
    return (
      <Navigate to={`/operations/payments?${params}${hash ?? ''}`} replace />
    );
  }
  if (setting === 'withdraw' || (!setting && view === 'withdrawals')) {
    params.delete('view');
    return (
      <Navigate
        to={`/collection/withdrawals${params.size ? `?${params}` : ''}${hash ?? ''}`}
        replace
      />
    );
  }

  const configurationSections = getWeb3ConfigurationSections(access);
  const requestedConfiguration = configurationSections.find(
    (section) => section.key === setting,
  );
  const canViewChainRecords =
    access.canViewCollections || access.canAdminNetwork;
  const sections: Web3Section[] = [
    {
      key: 'sweeps',
      label: '归集任务',
      render: () => (
        <SweepsPage
          key={`${status ?? ''}:${sweepTaskId ?? ''}:${depositId ?? ''}`}
          embedded
          initialStatus={status}
          depositId={depositId}
          sweepTaskId={sweepTaskId}
        />
      ),
      allowed: canViewChainRecords,
    },
    {
      key: 'vault',
      label: '储备核对',
      render: () => <VaultPage embedded />,
      allowed: canViewChainRecords,
    },
    {
      key: 'hot-wallet',
      label: '提现钱包监控',
      render: () => <HotWalletWatchPage embedded />,
      allowed: access.canAdminWithdrawals,
    },
    {
      key: 'scan',
      label: '扫链状态',
      render: () => <ScanStatus />,
      allowed: access.canAdminNetwork,
    },
    {
      key: 'overview',
      label: '资金概览',
      render: () => <CollectionDashboardPage embedded />,
      allowed: access.canViewCollections,
    },
  ].filter((section) => access.canViewWeb3Operations && section.allowed);

  const activeConfiguration =
    requestedConfiguration ??
    (!sections.length || setting ? configurationSections[0] : undefined);
  if (activeConfiguration) {
    return (
      <ConfigurationPage
        parent="钱包与链网"
        title={activeConfiguration.label}
        returnTo={sections.length ? link() : undefined}
      >
        <Space wrap size="middle" style={{ marginBottom: 12 }}>
          {configurationSections
            .filter((section) => section.key !== activeConfiguration.key)
            .map(({ key, label }) => (
              <ConfigurationLink key={key} to={link(key)}>
                {label}
              </ConfigurationLink>
            ))}
        </Space>
        <div key={activeConfiguration.key}>{activeConfiguration.render()}</div>
      </ConfigurationPage>
    );
  }

  const technicalConfiguration =
    configurationSections.find(({ key }) => key === 'wallets') ??
    configurationSections[0];
  return (
    <Web3Workspace
      title="钱包与链网"
      sections={sections}
      extra={
        technicalConfiguration && (
          <ConfigurationLink to={link(technicalConfiguration.key)}>
            钱包与链网配置
          </ConfigurationLink>
        )
      }
      notice={
        !acceptsNewWork && (
          <Alert
            type="warning"
            showIcon
            title="链上充提已停止受理"
            description="停止生成新充值地址和受理新提现，可以继续处理既有归集任务和查阅历史记录。"
            style={{ marginBottom: 12 }}
          />
        )
      }
    />
  );
}
