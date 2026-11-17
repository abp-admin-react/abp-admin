import { render, screen, waitFor, within } from '@testing-library/react';
import type { ComponentPropsWithoutRef, ReactNode } from 'react';
import { beforeEach, expect, it, vi } from 'vitest';
import access from '@/access';
import OverviewPage from './index';

const mocks = vi.hoisted(() => ({
  useAccess: vi.fn(),
  location: { pathname: '/finance/overview', search: '' },
  getFinanceOverview: vi.fn(),
  guard: (load: () => Promise<unknown>) => load(),
}));

vi.mock('@umijs/max', () => ({
  useAccess: mocks.useAccess,
  useLocation: () => mocks.location,
  useIntl: () => ({ formatMessage: ({ defaultMessage }: { defaultMessage: string }) => defaultMessage }),
  Link: ({ to, ...props }: ComponentPropsWithoutRef<'a'> & { to: string }) => (
    <a href={to} {...props} />
  ),
}));
vi.mock('@ant-design/pro-components', () => ({
  PageContainer: ({ children, extra }: { children: ReactNode; extra: ReactNode }) => (
    <main>{extra}{children}</main>
  ),
  ProCard: ({ children }: { children: ReactNode }) => children,
  StatisticCard: Object.assign(() => null, { Group: () => null }),
}));
vi.mock('@/components/FinanceState', () => ({
  FinanceEmpty: () => null,
  FinanceStateView: ({ children }: { children: ReactNode }) => children,
  useFinanceLoad: () => ({ guard: mocks.guard, nonce: 0 }),
}));
vi.mock('@/abp/finance', () => ({ getFinanceOverview: mocks.getFinanceOverview }));
vi.mock('../accounts', () => ({ FinanceAccounts: () => <p>科目内容</p> }));
vi.mock('../rules', () => ({ FinancePostingRules: () => <p>规则内容</p> }));
vi.mock('../period', () => ({ FinancePeriod: () => <p>期间内容</p> }));
vi.mock('../../settings/channel', () => ({ SettingsForm: () => <p>高级设置内容</p> }));

beforeEach(() => {
  mocks.getFinanceOverview.mockReset().mockResolvedValue(undefined);
  mocks.location.search = '';
});

it.each([
  ['Finance.Account.Manage', '会计科目', '科目内容', 'accounts'],
  ['Finance.Rule.View', '自动记账规则', '规则内容', 'rules'],
  ['Finance.Period.View', '会计期间', '期间内容', 'period'],
  ['SettingUi.ShowSettingPage', '高级运行设置', '高级设置内容', 'ledger'],
] as const)('仅有 %s 时直接进入已授权的会计设置', (permission, label, content, section) => {
  const permissions = access({ grantedPolicies: { [permission]: true } });
  expect(permissions.canOpenFinanceOverview).toBe(true);
  mocks.useAccess.mockReturnValue(permissions);
  render(<OverviewPage />);
  const links = within(screen.getByRole('navigation', { name: '会计设置' })).getAllByRole('link');
  expect(links).toHaveLength(1);
  expect(links[0]).toHaveTextContent(label);
  expect(links[0]).toHaveAttribute('aria-current', 'page');
  expect(links[0]).toHaveAttribute('href', `/finance/overview?settings=${section}`);
  expect(screen.getByText(content)).toBeTruthy();
  expect(screen.queryByRole('link', { name: '返回会计概览' })).toBeNull();
  expect(mocks.getFinanceOverview).not.toHaveBeenCalled();
});

it('账簿查看者保留概览及科目查询，不加载高级设置表单', async () => {
  mocks.useAccess.mockReturnValue(access({ grantedPolicies: { 'Finance.Ledger.View': true } }));
  const page = render(<OverviewPage />);
  await waitFor(() => expect(mocks.getFinanceOverview).toHaveBeenCalledOnce());
  expect(screen.getByRole('link', { name: '会计设置' })).toHaveAttribute('href', '/finance/overview?settings=accounts');

  mocks.location.search = '?settings=ledger';
  page.rerender(<OverviewPage />);
  expect(screen.getByText('科目内容')).toBeTruthy();
  expect(screen.queryByText('高级设置内容')).toBeNull();
  expect(screen.getByRole('link', { name: '返回会计概览' })).toHaveAttribute('href', '/finance/overview');
});

it('代理范围只查询获准的科目，All 权限恢复全局统计', async () => {
  const agent = { 'Finance.Ledger.View': true, 'Finance.Scope.Agent': true };
  mocks.useAccess.mockReturnValue(access({ grantedPolicies: agent }));
  const page = render(<OverviewPage />);
  expect(screen.getByText('科目内容')).toBeTruthy();
  expect(mocks.getFinanceOverview).not.toHaveBeenCalled();

  mocks.useAccess.mockReturnValue(access({ grantedPolicies: { ...agent, 'Finance.Scope.All': true } }));
  page.rerender(<OverviewPage />);
  await waitFor(() => expect(mocks.getFinanceOverview).toHaveBeenCalledOnce());
});
