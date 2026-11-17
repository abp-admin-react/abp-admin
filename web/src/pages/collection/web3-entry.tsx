import { Navigate, useLocation } from '@umijs/max';

export default function Web3Entry() {
  const { pathname, search, hash } = useLocation();

  if (pathname.replace(/\/+$/, '').toLowerCase() === '/web3/rules') {
    const view =
      new URLSearchParams(search).get('tab') === 'withdraw'
        ? 'withdraw'
        : 'deposit';
    const params = new URLSearchParams(search);
    params.delete('tab');
    params.set('settings', view);
    if (view === 'withdraw') {
      params.delete('view');
      return <Navigate to={`/collection/withdrawals?${params}${hash}`} replace />;
    }
    params.set('view', 'deposits');
    return <Navigate to={`/operations/payments?${params}${hash}`} replace />;
  }

  return (
    <Navigate
      to={`/web3/operations${search}${hash}`}
      replace
    />
  );
}
