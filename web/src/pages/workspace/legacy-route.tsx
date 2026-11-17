import { Navigate, useLocation } from '@umijs/max';

const aliases: Record<string, [string, Record<string, string>?]> = {
  '/collection/order-settings': ['/collection/orders', { settings: 'rules' }],
  '/collection/settings': ['/collection/orders', { settings: 'balance' }],
  '/administration/payments': ['/operations/payments'],
  '/payments': ['/operations/payments'],
  '/alipay': ['/operations/payments', { view: 'alipay' }],
  '/alipay/payments': ['/operations/payments', { view: 'alipay' }],
  '/alipay/settings': ['/operations/payments', { view: 'alipay', settings: 'channels' }],
  '/wechat': ['/operations/payments', { view: 'wechat' }],
  '/wechat/payments': ['/operations/payments', { view: 'wechat' }],
  '/wechat/settings': ['/operations/payments', { view: 'wechat', settings: 'channels' }],
  '/stripe': ['/operations/payments', { view: 'stripe' }],
  '/stripe/payments': ['/operations/payments', { view: 'stripe' }],
  '/stripe/settings': ['/operations/payments', { view: 'stripe', settings: 'channels' }],
  '/collection/dashboard': ['/web3/operations', { view: 'overview' }],
  '/collection/deposits': ['/operations/payments', { view: 'deposits' }],
  '/collection/sweeps': ['/web3/operations', { view: 'sweeps' }],
  '/collection/vault': ['/web3/operations', { view: 'vault' }],
  '/collection/hot-wallet': ['/web3/operations', { view: 'hot-wallet' }],
  '/collection/network': ['/web3/operations', { settings: 'network' }],
  '/collection/wallets': ['/web3/operations', { settings: 'wallets' }],
  '/web3/settings': ['/operations/payments', { view: 'deposits', settings: 'deposit' }],
  '/web3/configuration': ['/web3/config'],
  '/settings': ['/administration/settings'],
};

export default function LegacyWorkspaceRoute() {
  const { pathname, search, hash } = useLocation();
  const alias = aliases[pathname.replace(/\/+$/, '').toLowerCase()];
  if (!alias) return <Navigate to="/welcome" replace />;

  const [target, forced] = alias;
  const query = new URLSearchParams(search);
  for (const [key, value] of Object.entries(forced ?? {})) {
    query.set(key, value);
  }
  return (
    <Navigate
      to={{ pathname: target, search: query.size ? `?${query}` : '', hash }}
      replace
    />
  );
}
