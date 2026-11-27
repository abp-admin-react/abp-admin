import { fireEvent, render, screen, within } from '@testing-library/react';
import type { ReactNode } from 'react';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import CollectionDashboardPage from './dashboard';

const mocks = vi.hoisted(() => ({
  getDashboardAnkrBalance: vi.fn(),
  getTreasuryDashboard: vi.fn(),
  useAccess: vi.fn(),
}));

vi.mock('@umijs/max', () => ({
  useAccess: mocks.useAccess,
  Link: ({ to, children }: { to: string; children: ReactNode }) => (
    <a href={to}>{children}</a>
  ),
}));
vi.mock('@/abp/scanProviders', () => ({
  getDashboardAnkrBalance: mocks.getDashboardAnkrBalance,
}));
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
    mocks.getDashboardAnkrBalance.mockResolvedValue({
      status: 'available',
      label: '充值主用账户',
      credits: '900719925474099300',
      balanceUsd: '0.00',
      checkedAt: '2026-11-14T04:00:00Z',
    });
    render(<CollectionDashboardPage embedded />);
    const card = screen.getByRole('region', { name: 'Ankr 账户余额' });
    const ankr = within(card);
    expect(await ankr.findByText('剩余 Credits')).toBeVisible();
    expect(card).toHaveTextContent('充值主用账户');
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
    mocks.getDashboardAnkrBalance
      .mockResolvedValueOnce({ status: 'notConfigured' })
      .mockResolvedValueOnce({ status: 'unavailable' });
    render(<CollectionDashboardPage embedded />);
    expect(
      await screen.findByText('未配置 Ankr Platform Auth Key'),
    ).toBeVisible();
    expect(
      screen.getByRole('link', { name: '前往第三方扫链' }),
    ).toHaveAttribute(
      'href',
      '/web3/operations?view=overview&settings=providers',
    );
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
    expect(mocks.getDashboardAnkrBalance).not.toHaveBeenCalled();
    expect(screen.queryByText('Ankr 账户余额')).not.toBeInTheDocument();
  });

  it('储备卡展示核对结论并保留原始金额与核对后金额的精度', async () => {
    const empty = await mocks.getTreasuryDashboard();
    mocks.getTreasuryDashboard.mockResolvedValue({
      ...empty,
      userBalances: [
        { currency: 'Usdt', balance: '100.000000000000000001', frozen: '12' },
      ],
      vaultChecks: [
        {
          symbol: 'USDT',
          vaultBalance: '100',
          userTotal: '112',
          difference: '-12',
          reconciliationStatus: 'Shortfall',
          reconciledDifference: '-0.000000000000000001',
          checkedAt: '2026-11-26T04:00:00Z',
          referenceBlock: 100,
          referenceVaultAddress: '0x1111111111111111111111111111111111111111',
        },
      ],
    });
    mocks.getDashboardAnkrBalance.mockResolvedValue({
      status: 'notConfigured',
    });
    render(<CollectionDashboardPage embedded />);
    expect(await screen.findByText('USDT 核对后差额')).toBeVisible();
    expect(screen.getByText('-0.000000000000000001')).toBeVisible();
    expect(screen.getByText('原始差额 -12')).toBeVisible();
    expect(screen.getByText('缺口')).toBeVisible();
    expect(screen.getByText(/最近核对 .*UTC/)).toBeVisible();
    expect(screen.getByText('参考区块 100')).toBeVisible();
    expect(
      screen.getByText('核对金库 0x1111111111111111111111111111111111111111'),
    ).toBeVisible();
    expect(screen.getByText('用户 Usdt 可用余额')).toBeVisible();
    expect(screen.getByText('100.000000000000000001')).toBeVisible();
    expect(screen.getByText('冻结 12')).toBeVisible();
  });
});
