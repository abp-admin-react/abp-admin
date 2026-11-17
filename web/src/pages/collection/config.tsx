import { Navigate, type useAccess, useLocation } from '@umijs/max';
import NetworkConfigPage from './network';
import { Web3RuleSection } from './rules';
import ChainRuntimeSettings from './runtime-settings';
import ScanProvidersPage from './scan-providers';
import CollectionWalletsPage from './wallets';

export function getWeb3ConfigurationSections(
  access: ReturnType<typeof useAccess>,
) {
  const canConfigureRules = access.canConfigureCollectionNetwork;
  return [
    {
      key: 'chain-rules',
      label: '扫链与归集规则',
      available: canConfigureRules,
      render: () => <Web3RuleSection section="Chain" />,
    },
    {
      key: 'network',
      label: '链网络',
      available: access.canAdminNetwork && canConfigureRules,
      render: () => <NetworkConfigPage />,
    },
    {
      key: 'providers',
      label: '第三方扫链',
      available: access.canAdminNetwork && canConfigureRules,
      render: () => <ScanProvidersPage />,
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
}

export default function Web3ConfigPage() {
  const { search, hash } = useLocation();
  const query = new URLSearchParams(search);
  const setting = query.get('settings') ?? query.get('view') ?? 'network';
  query.delete('view');
  query.set('settings', setting);
  return <Navigate to={`/web3/operations?${query}${hash}`} replace />;
}
