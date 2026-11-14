import { fireEvent, render, screen, within } from '@testing-library/react';
import type { ReactNode } from 'react';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import CollectionDashboardPage from './dashboard';

const mocks = vi.hoisted(() => ({
  getAnkrBalance: vi.fn(),
  getTreasuryDashboard: vi.fn(),
  useAccess: vi.fn(),
}));

vi.mock('@umijs/max', () => ({
  useAccess: mocks.useAccess,
  Link: ({ to, children }: { to: string; children: ReactNode }) => (
    <a href={to}>{children}</a>
  ),
}));
vi.mock('@/abp/chainAdmin', () => ({ getAnkrBalance: mocks.getAnkrBalance }));
vi.mock('@/abp/collectionAdmin', () => ({
  getTreasuryDashboard: mocks.getTreasuryDashboard,
}));

describe('Web3 运营的 Ankr 账户余额', () => {
  beforeEach(() => {
    vi.resetAllMocks();
    mocks.useAccess.mockReturnValue({ canViewCollections: true });
    mocks.getTreasuryDashboard.mockResolvedValue({
      depositTodayCount: 1,
      depositTodayAmount: '12',
      withdrawTodayCount: 0,
      withdrawTodayAmount: '0',
      withdrawInFlightAmount: '0',
      heldDepositTotal: '0',
      heldDepositCount: 0,
      depositFeeTotal: '0',
      withdrawFeeTotal: '0',
      userBalances: [],
      vaultChecks: [],
      depositByStatus: [],
      withdrawByStatus: [],
    });
  });

  it('独立展示 Credits 和美元余额，保留大数精度及有效的零余额', async () => {
    mocks.getAnkrBalance.mockResolvedValue({
      status: 'available',
      credits: '900719925474099300',
      balanceUsd: '0.00',
      checkedAt: '2026-11-14T04:00:00Z',
    });
    render(<CollectionDashboardPage embedded />);
    const card = screen.getByRole('region', { name: 'Ankr 账户余额' });
    const ankr = within(card);
    expect(await ankr.findByText('剩余 Credits')).toBeVisible();
    expect(card).toHaveTextContent('900,719,925,474,099,300');
    expect(card).toHaveTextContent('$0.00');
    expect(ankr.getByText(/查询于/)).toBeVisible();
    expect(await screen.findByText(/今日充值/)).toBeVisible();
  });

  it('区分未配置和查询失败，并可单独刷新余额', async () => {
    mocks.useAccess.mockReturnValue({
      canViewCollections: true,
      canAdminNetwork: true,
      canConfigureCollectionNetwork: true,
    });
    mocks.getAnkrBalance
      .mockResolvedValueOnce({ status: 'notConfigured' })
      .mockResolvedValueOnce({ status: 'unavailable' });
    render(<CollectionDashboardPage embedded />);
    expect(
      await screen.findByText('未配置 Ankr Platform Auth Key'),
    ).toBeVisible();
    expect(
      screen.getByRole('link', { name: '前往链网络配置' }),
    ).toHaveAttribute('href', '/web3/config?view=network');
    fireEvent.click(screen.getByRole('button', { name: '刷新余额' }));
    expect(await screen.findByText('暂时无法查询 Ankr 账户余额')).toBeVisible();
    expect(screen.queryByText('美元余额（USD）')).not.toBeInTheDocument();
    expect(await screen.findByText(/今日充值/)).toBeVisible();
    expect(mocks.getTreasuryDashboard).toHaveBeenCalledTimes(1);
  });

  it('没有收款查看权限时不读取 Ankr 余额', async () => {
    mocks.useAccess.mockReturnValue({ canViewCollections: false });
    render(<CollectionDashboardPage embedded />);
    expect(await screen.findByText(/今日充值/)).toBeVisible();
    expect(mocks.getAnkrBalance).not.toHaveBeenCalled();
    expect(screen.queryByText('Ankr 账户余额')).not.toBeInTheDocument();
  });
});
