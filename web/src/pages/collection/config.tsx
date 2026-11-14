import { useAccess } from '@umijs/max';
import NetworkConfigPage from './network';
import { Web3RuleSection } from './rules';
import ChainRuntimeSettings from './runtime-settings';
import CollectionWalletsPage from './wallets';
import { Web3Workspace } from './web3-workspace';

export default function Web3ConfigPage() {
  const access = useAccess();
  const canConfigureRules = access.canConfigureCollectionNetwork;
  const canManageWithdrawalRules =
    access.canManageCollectionWallets && access.canAdminWithdrawals;
  const sections = [
    {
      key: 'deposit',
      label: '充值与归集规则',
      available: canConfigureRules,
      render: () => <Web3RuleSection section="Chain" />,
    },
    {
      key: 'withdraw',
      label: '提现规则',
      available: canConfigureRules || canManageWithdrawalRules,
      render: () => <Web3RuleSection section="Withdraw" />,
    },
    {
      key: 'network',
      label: '链网络',
      available: access.canAdminNetwork && canConfigureRules,
      render: () => <NetworkConfigPage />,
    },
    {
      key: 'wallets',
      label: '链上钱包',
      available: access.canManageCollectionWallets,
      render: () => <CollectionWalletsPage />,
    },
    {
      key: 'runtime',
      label: '运行设置',
      available: access.canAdminNetwork && canConfigureRules,
      render: () => <ChainRuntimeSettings />,
    },
  ].filter((section) => section.available);

  return <Web3Workspace title="Web3 配置" sections={sections} />;
}
