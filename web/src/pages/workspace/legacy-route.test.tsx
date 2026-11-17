import { render, screen } from '@testing-library/react';
import { beforeEach, expect, it, vi } from 'vitest';
import Web3Entry from '../collection/web3-entry';
import LegacyFinanceConfiguration from '../finance/overview/legacy-configuration';
import LegacyWorkspaceRoute from './legacy-route';

const location = vi.hoisted(() => ({ pathname: '', search: '', hash: '' }));

vi.mock('@umijs/max', () => ({
  useLocation: () => location,
  Navigate: ({ to }: { to: string | typeof location }) => (
    <a href={typeof to === 'string' ? to : `${to.pathname}${to.search}${to.hash}`}>目标</a>
  ),
}));

beforeEach(() => Object.assign(location, { search: '?focus=1', hash: '#settings' }));

it.each([
  [LegacyWorkspaceRoute, '/Alipay/Settings/', '/operations/payments?focus=1&view=alipay&settings=channels#settings'],
  [Web3Entry, '/WEB3/Rules/', '/operations/payments?focus=1&settings=deposit&view=deposits#settings'],
  [LegacyWorkspaceRoute, '/collection/deposits', '/operations/payments?focus=1&view=deposits#settings'],
  [LegacyFinanceConfiguration, '/Finance/Settings/', '/finance/overview?focus=1&settings=ledger#settings'],
] as const)('历史入口规范化 %s，保留查询和定位', (Page, pathname, target) => {
  location.pathname = pathname;
  render(<Page />);
  expect(screen.getByRole('link', { name: '目标' })).toHaveAttribute('href', target);
});

it('旧提现规则进入用户资金并保留定位参数', () => {
  Object.assign(location, { pathname: '/web3/rules', search: '?tab=withdraw&withdrawalId=wd-1', hash: '#policy' });
  render(<Web3Entry />);
  expect(screen.getByRole('link', { name: '目标' })).toHaveAttribute(
    'href', '/collection/withdrawals?withdrawalId=wd-1&settings=withdraw#policy',
  );
});
