import { Navigate, useAccess, useLocation } from '@umijs/max';

export default function Web3Entry() {
  const access = useAccess();
  const { pathname, search } = useLocation();

  if (pathname === '/web3/rules') {
    const view =
      new URLSearchParams(search).get('tab') === 'withdraw'
        ? 'withdraw'
        : 'deposit';
    return <Navigate to={`/web3/config?view=${view}`} replace />;
  }

  return (
    <Navigate
      to={access.canViewWeb3Operations ? '/web3/operations' : '/web3/config'}
      replace
    />
  );
}
