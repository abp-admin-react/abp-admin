import { act, fireEvent, render, screen, waitFor } from '@testing-library/react';
import type { ReactNode } from 'react';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import MemberFundsPage from './member-funds';

const mocks = vi.hoisted(() => ({
  users: vi.fn(),
  balances: vi.fn(),
  transactions: vi.fn(),
  access: vi.fn(),
}));

vi.mock('@umijs/max', () => ({
  useAccess: mocks.access,
  Link: ({ to, children }: { to: string; children: ReactNode }) => (
    <a href={to}>{children}</a>
  ),
}));
vi.mock('@ant-design/pro-components', () => ({
  PageContainer: ({ children }: { children: ReactNode }) => children,
}));
vi.mock('@/abp/identity', () => ({ getUsers: mocks.users }));
vi.mock('@/abp/collectionAdmin', () => ({
  BALANCE_ACTION_LABELS: {},
  getMemberBalances: mocks.balances,
  getMemberTxns: mocks.transactions,
}));

function balance(value: string) {
  return { items: [{ currency: 'Usdt', balance: value, frozen: '0', total: value }] };
}

async function selectUser(keyword: string) {
  const input = screen.getByRole('combobox');
  fireEvent.mouseDown(input);
  fireEvent.change(input, { target: { value: keyword } });
  fireEvent.click(await screen.findByRole('option', { name: keyword }));
}

describe('用户余额查询', () => {
  beforeEach(() => {
    vi.resetAllMocks();
    mocks.access.mockReturnValue({ canCreateBalanceAdjustments: true });
    mocks.users.mockImplementation(({ filter }) => Promise.resolve({
      items: [{ id: filter, userName: filter }],
    }));
    mocks.transactions.mockResolvedValue({ items: [], totalCount: 0 });
  });

  it('更换用户后忽略旧余额查询，并保留精确金额和调账入口', async () => {
    let resolveOld!: (value: ReturnType<typeof balance>) => void;
    mocks.balances.mockImplementation((id) => id === 'Alice'
      ? new Promise((resolve) => { resolveOld = resolve; })
      : Promise.resolve(balance('3.000000000000000001')));
    render(<MemberFundsPage />);
    await selectUser('Alice');
    await waitFor(() => expect(mocks.balances).toHaveBeenCalledWith('Alice'));
    await selectUser('Betty');
    await waitFor(() => expect(screen.getAllByText('3.000000000000000001')).toHaveLength(2));
    await act(async () => resolveOld(balance('42.99')));
    expect(screen.queryByText('42.99')).toBeNull();
    expect(screen.getByRole('link', { name: '申请余额调整' })).toHaveAttribute(
      'href', '/finance/balance-adjustments?createFor=Betty',
    );
  });

  it('余额查询失败时可以重试，查看权限不会提供调整入口', async () => {
    mocks.access.mockReturnValue({});
    mocks.balances.mockRejectedValueOnce(new Error('network')).mockResolvedValue(balance('7.123'));
    render(<MemberFundsPage />);
    await selectUser('Alice');
    expect(await screen.findByText('加载余额和流水失败')).toBeTruthy();
    fireEvent.click(screen.getByRole('button', { name: /重\s*试/ }));
    await waitFor(() => expect(screen.getAllByText('7.123')).toHaveLength(2));
    expect(screen.queryByRole('link', { name: '申请余额调整' })).toBeNull();
  });
});
