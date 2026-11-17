import { Navigate, useLocation } from '@umijs/max';

const sections: Record<string, string> = {
  '/finance/accounts': 'accounts',
  '/finance/rules': 'rules',
  '/finance/period': 'period',
  '/finance/settings': 'ledger',
};

export default function LegacyFinanceConfiguration() {
  const { pathname, search, hash } = useLocation();
  const query = new URLSearchParams(search);
  query.set('settings', sections[pathname.replace(/\/+$/, '').toLowerCase()] ?? 'configuration');

  return <Navigate replace to={`/finance/overview?${query}${hash}`} />;
}
