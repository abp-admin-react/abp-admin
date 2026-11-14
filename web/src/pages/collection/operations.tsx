import { useAccess, useLocation } from '@umijs/max';
import CollectionDashboardPage from './dashboard';
import DepositsPage from './deposits';
import HotWalletWatchPage from './hot-wallet';
import ScanStatus from './scan-status';
import SweepsPage from './sweeps';
import VaultPage from './vault';
import { type Web3Section, Web3Workspace } from './web3-workspace';
import WithdrawalsPage from './withdrawals';

export default function Web3OperationsPage() {
  const access = useAccess();
  const { search } = useLocation();
  const params = new URLSearchParams(search);
  const status = params.get('status');
  const depositId = params.get('depositId') ?? undefined;
  const withdrawalId = params.get('withdrawalId') ?? undefined;
  const canViewChainRecords =
    access.canViewCollections || access.canAdminNetwork;
  const sections: Web3Section[] = [
    {
      key: 'overview',
      label: '概览',
      render: () => <CollectionDashboardPage embedded />,
      allowed: access.canViewCollections,
    },
    {
      key: 'deposits',
      label: '充值记录',
      render: () => (
        <DepositsPage key={depositId} embedded depositId={depositId} />
      ),
      allowed: canViewChainRecords,
    },
    {
      key: 'withdrawals',
      label: '提现审核',
      render: () => (
        <WithdrawalsPage
          key={`${status ?? ''}:${withdrawalId ?? ''}`}
          embedded
          initialStatus={status ?? undefined}
          withdrawalId={withdrawalId}
        />
      ),
      allowed: access.canAdminWithdrawals,
    },
    {
      key: 'sweeps',
      label: '归集任务',
      render: () => (
        <SweepsPage key={status} embedded initialStatus={status ?? undefined} />
      ),
      allowed: canViewChainRecords,
    },
    {
      key: 'vault',
      label: '储备核对',
      render: () => <VaultPage embedded />,
      allowed: access.canViewCollections,
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
  ].filter((section) => access.canViewWeb3Operations && section.allowed);

  return <Web3Workspace title="Web3 运营数据" sections={sections} />;
}
