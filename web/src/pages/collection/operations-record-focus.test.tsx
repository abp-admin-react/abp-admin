vi.mock('ag-grid-react', () => import('../../../tests/gridTestDouble'));

import {
  act,
  fireEvent,
  render,
  screen,
  waitFor,
  within,
} from '@testing-library/react';
import { App } from 'antd';
import type { ComponentPropsWithoutRef } from 'react';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import PaymentsPage from '../payments';
import Web3OperationsPage from './operations';
import WithdrawalsPage from './withdrawals';

const mocks = vi.hoisted(() => ({
  getNetworks: vi.fn(),
  getVaultChecks: vi.fn(),
  getDepositOrders: vi.fn(),
  getWithdrawals: vi.fn(),
  getWithdrawStatus: vi.fn(),
  getHotWallet: vi.fn(),
  getSweepTasks: vi.fn(),
  useAccess: vi.fn(),
  useLocation: vi.fn(),
  push: vi.fn(),
}));

vi.mock('@umijs/max', () => ({
  useAccess: mocks.useAccess,
  useLocation: mocks.useLocation,
  useModel: () => ({ initialState: undefined }),
  history: { push: mocks.push },
  Navigate: ({ to }: { to: string }) => <a href={to}>迁往记录页</a>,
  Link: ({ to, ...props }: ComponentPropsWithoutRef<'a'> & { to: string }) => (
    <a href={to} {...props} />
  ),
}));
vi.mock('@/abp/chainAdmin', async (original) => ({
  ...(await original<typeof import('@/abp/chainAdmin')>()),
  getNetworks: mocks.getNetworks,
  getVaultChecks: mocks.getVaultChecks,
  getDepositOrders: mocks.getDepositOrders,
  getSweepTasks: mocks.getSweepTasks,
}));
vi.mock('@/abp/withdrawal', async (original) => ({
  ...(await original<typeof import('@/abp/withdrawal')>()),
  getWithdrawals: mocks.getWithdrawals,
  getWithdrawStatus: mocks.getWithdrawStatus,
  getHotWallet: mocks.getHotWallet,
}));
vi.mock('@/abp/chainSignalR', () => ({
  onChainConnState: () => () => {},
  onChainDepositChanged: () => () => {},
  onChainSweepChanged: () => () => {},
  onChainVaultCheck: () => () => {},
  startChainSignalR: () => {},
}));
vi.mock('@/abp/finance', () => ({
  financeErrorText: (error: Error) => error.message,
}));
vi.mock('./dashboard', () => ({ default: () => null }));
vi.mock('./hot-wallet', () => ({ default: () => null }));
vi.mock('./scan-status', () => ({ default: () => null }));

const depositId = 'c6d8f751-c115-4fff-aee2-6964056bcfad';
const withdrawalId = '21a49a4f-d42e-4fc1-8665-75f0cce722a9';
const sweepTaskId = 'bb6893bd-c711-49ea-bab7-7cdc836e8b39';
const userId = '7ecdbee4-1032-442c-babf-f5f3bd9e6168';
const address = '0x1111111111111111111111111111111111111111';
const deposit = {
  id: depositId,
  txHash: `0x${'1'.repeat(64)}`,
  logIndex: 0,
  userId,
  toAddress: address,
  fromAddress: address,
  amount: '12.01',
  rawAmount: '12010000',
  symbol: 'USDT',
  networkName: 'BSC',
  blockNumber: 1221,
  status: 'Confirmed',
  manuallyReleased: false,
  creationTime: '2026-11-13T08:09:00Z',
};
const withdrawal = {
  id: withdrawalId,
  userId,
  currency: 'Usdc',
  networkName: 'BSC',
  toAddress: address,
  amount: '34.56',
  fee: '0.2',
  actualAmount: '34.36',
  status: 'Completed',
  retryCount: 0,
  creationTime: '2026-11-13T08:09:00Z',
};

function show(search: string, pathname = '/web3/operations') {
  mocks.useLocation.mockReturnValue({ pathname, search, hash: '' });
  const Page =
    pathname === '/operations/payments'
      ? PaymentsPage
      : pathname === '/collection/withdrawals'
        ? WithdrawalsPage
        : Web3OperationsPage;
  render(
    <App>
      <Page />
    </App>,
  );
}

describe('Web3 运营明细定位', () => {
  beforeEach(() => {
    vi.resetAllMocks();
    mocks.useAccess.mockReturnValue({
      canViewWeb3Operations: true,
      canViewCollections: true,
      canViewDepositRecords: true,
      canViewCollectionPayments: true,
      canAdminWithdrawals: true,
      canAdminNetwork: true,
    });
    mocks.getWithdrawStatus.mockResolvedValue({ enabled: false, halt: false });
    mocks.getHotWallet.mockResolvedValue({ configured: false });
    mocks.getDepositOrders.mockResolvedValue({ items: [], totalCount: 0 });
    mocks.getWithdrawals.mockResolvedValue({ items: [], totalCount: 0 });
    mocks.getNetworks.mockResolvedValue([]);
    mocks.getVaultChecks.mockResolvedValue({ items: [], totalCount: 0 });
    mocks.getSweepTasks.mockResolvedValue({ items: [], totalCount: 0 });
  });

  it('链网配置将第三方扫链放在同级导航', async () => {
    mocks.useAccess.mockReturnValue({
      canViewWeb3Operations: true,
      canAdminNetwork: true,
      canViewDepositRecords: true,
      canConfigureCollectionNetwork: true,
    });
    show('?view=overview&settings=network');
    expect(screen.getByRole('link', { name: '第三方扫链' })).toHaveAttribute(
      'href',
      '/web3/operations?view=overview&settings=providers',
    );
    expect(await screen.findByText('暂无链网络')).toBeTruthy();
  });

  it('链网管理员的储备核对旧入口保持可用，不要求充值总览权限', async () => {
    mocks.useAccess.mockReturnValue({
      canViewWeb3Operations: true,
      canAdminNetwork: true,
    });
    show('?view=vault');
    expect(screen.getByRole('tab', { name: '储备核对' })).toHaveAttribute(
      'aria-selected',
      'true',
    );
    await waitFor(() =>
      expect(mocks.getVaultChecks).toHaveBeenCalledWith({
        skipCount: 0,
        maxResultCount: 20,
      }),
    );
    expect(mocks.getDepositOrders).not.toHaveBeenCalled();
    expect(
      screen.queryByRole('tab', { name: '资金概览' }),
    ).not.toBeInTheDocument();
  });

  it('链网管理员可以定位充值，已经人工放行的尾单继续显示待归集', async () => {
    mocks.useAccess.mockReturnValue({
      canViewWeb3Operations: true,
      canAdminNetwork: true,
      canViewDepositRecords: true,
    });
    mocks.getDepositOrders.mockResolvedValue({
      items: [{ ...deposit, manuallyReleased: true }],
      totalCount: 1,
    });
    show(`?view=deposits&depositId=${depositId}`, '/operations/payments');
    const focus = within(await screen.findByRole('alert'));
    expect(await focus.findByText('已确认/待归集')).toBeTruthy();
    expect(screen.queryByRole('button', { name: /放\s*行/ })).toBeNull();
  });

  it('精确定位充值，摘要可读且保留原操作；切换栏目时清除定位', async () => {
    mocks.getDepositOrders.mockResolvedValue({
      items: [deposit],
      totalCount: 1,
    });
    show(`?view=deposits&depositId=${depositId}`, '/operations/payments');
    const focus = within(await screen.findByRole('alert'));
    expect(await focus.findByText('已定位充值单')).toBeTruthy();
    expect(focus.getByText('12.01 USDT')).toBeTruthy();
    expect(focus.getByText('大额待放行')).toBeTruthy();
    expect(focus.getByText(`充值单号 ${depositId}`)).toBeTruthy();
    expect(focus.getByRole('link', { name: '查看全部充值单' })).toHaveAttribute(
      'href',
      '/operations/payments?view=deposits',
    );
    expect(screen.getByRole('button', { name: /放\s*行/ })).toBeTruthy();
    expect(mocks.getDepositOrders).toHaveBeenCalledExactlyOnceWith({
      skipCount: 0,
      maxResultCount: 1,
      depositId,
    });

    fireEvent.click(screen.getByRole('tab', { name: '第三方收款' }));
    expect(mocks.push).toHaveBeenCalledWith({
      pathname: '/operations/payments',
      search: '?view=payments',
      hash: '',
    });
  });

  it('精确定位非待审核的提现并可重试加载失败', async () => {
    mocks.useAccess.mockReturnValue({
      canViewWeb3Operations: true,
      canAdminWithdrawals: true,
      canViewFinanceVouchers: true,
      canViewMemberFunds: true,
    });
    mocks.getWithdrawals
      .mockRejectedValueOnce(new Error('offline'))
      .mockResolvedValue({ items: [withdrawal], totalCount: 1 });
    show(`?status=all&withdrawalId=${withdrawalId}`, '/collection/withdrawals');
    expect(await screen.findByText('加载提现单失败')).toBeTruthy();
    fireEvent.click(screen.getByRole('button', { name: /重\s*试/ }));
    expect(await screen.findByText('已定位提现单')).toBeTruthy();
    const focus = within(screen.getByRole('alert'));
    expect(focus.getByText('34.56 USDC')).toBeTruthy();
    expect(focus.getByText('已完成')).toBeTruthy();
    expect(focus.getByText(`提现单号 ${withdrawalId}`)).toBeTruthy();
    expect(focus.getByRole('link', { name: '查看全部提现单' })).toHaveAttribute(
      'href',
      '/collection/withdrawals?status=all',
    );
    expect(focus.getByRole('link', { name: '查看会计凭证' })).toHaveAttribute(
      'href',
      `/finance/vouchers?bizType=withdraw&bizRefId=${withdrawalId}`,
    );
    expect(focus.getByRole('link', { name: '查看用户资金' })).toHaveAttribute(
      'href',
      `/collection/member-funds?userId=${userId}`,
    );
    expect(
      focus.getByRole('link', { name: '查看提现关联流水' }),
    ).toHaveAttribute(
      'href',
      `/collection/member-funds?userId=${userId}&withdrawalId=${withdrawalId}`,
    );
    expect(mocks.getWithdrawals).toHaveBeenLastCalledWith({
      skipCount: 0,
      maxResultCount: 1,
      withdrawalId,
    });
  });

  it('可以查看提现单的用户只有获授凭证访问权限才看到凭证链接', async () => {
    mocks.getWithdrawals.mockResolvedValue({
      items: [withdrawal],
      totalCount: 1,
    });
    show(`?status=all&withdrawalId=${withdrawalId}`, '/collection/withdrawals');
    expect(await screen.findByText('已定位提现单')).toBeTruthy();
    expect(screen.queryByRole('link', { name: '查看会计凭证' })).toBeNull();
    expect(screen.queryByRole('link', { name: '查看提现关联流水' })).toBeNull();
  });

  it('提现列表中的单号可打开该笔提现详情', async () => {
    mocks.getWithdrawals.mockResolvedValue({
      items: [withdrawal],
      totalCount: 1,
    });
    show('?status=all', '/collection/withdrawals');
    expect(
      await screen.findByRole('link', { name: `查看提现单 ${withdrawalId}` }),
    ).toHaveAttribute(
      'href',
      `/collection/withdrawals?withdrawalId=${withdrawalId}`,
    );
  });

  it('进行中的提现跟随后台一直更新到完成，并停止轮询', async () => {
    vi.useFakeTimers();
    try {
      let broadcastingCount = 0;
      mocks.getWithdrawStatus.mockImplementation(async () => ({
        enabled: true,
        broadcastingCount,
      }));
      mocks.getWithdrawals
        .mockResolvedValueOnce({
          items: [{ ...withdrawal, status: 'Processing' }],
          totalCount: 1,
        })
        .mockImplementationOnce(async () => {
          await Promise.resolve();
          broadcastingCount = 1;
          return {
            items: [{ ...withdrawal, status: 'Broadcasting' }],
            totalCount: 1,
          };
        })
        .mockImplementationOnce(async () => {
          await Promise.resolve();
          broadcastingCount = 0;
          return { items: [withdrawal], totalCount: 1 };
        });
      show(`?withdrawalId=${withdrawalId}`, '/collection/withdrawals');
      await act(async () => {});
      expect(
        within(screen.getByRole('alert')).getByText('待广播'),
      ).toBeTruthy();
      await act(async () => vi.advanceTimersByTimeAsync(5_000));
      expect(
        within(screen.getByRole('alert')).getByText('广播中'),
      ).toBeTruthy();
      expect(screen.getByText('广播中 1')).toBeTruthy();
      await act(async () => vi.advanceTimersByTimeAsync(5_000));
      expect(
        within(screen.getByRole('alert')).getByText('已完成'),
      ).toBeTruthy();
      expect(screen.getByText('广播中 0')).toBeTruthy();
      await act(async () => vi.advanceTimersByTimeAsync(15_000));
      expect(mocks.getWithdrawals).toHaveBeenCalledTimes(3);
    } finally {
      vi.useRealTimers();
    }
  });

  it('无效链接不发起列表读取，且允许查看全部', async () => {
    show('?view=deposits&depositId=invalid', '/operations/payments');
    expect(await screen.findByText('充值单链接无效')).toBeTruthy();
    expect(screen.getByRole('link', { name: '查看全部充值单' })).toBeTruthy();
    expect(mocks.getDepositOrders).not.toHaveBeenCalled();
  });

  it('服务端未返回请求记录时不误显示同批其他单据', async () => {
    mocks.getDepositOrders.mockResolvedValue({
      items: [{ ...deposit, id: withdrawalId }],
      totalCount: 1,
    });
    show(`?view=deposits&depositId=${depositId}`, '/operations/payments');
    expect(await screen.findByText('未找到该充值单')).toBeTruthy();
    expect(screen.queryByText('12.01 USDT')).toBeNull();
  });

  it('无提现审核权限不能通过记录链接加载提现列表', async () => {
    mocks.useAccess.mockReturnValue({
      canViewWeb3Operations: true,
      canViewCollections: true,
    });
    show(`?status=all&withdrawalId=${withdrawalId}`, '/collection/withdrawals');
    expect(screen.getByText('暂无提现查看权限')).toBeInTheDocument();
    expect(mocks.getWithdrawals).not.toHaveBeenCalled();
  });

  it.each([
    [
      '?view=deposits&depositId=old&status=Confirmed',
      '/operations/payments?view=deposits&depositId=old&status=Confirmed',
    ],
    [
      '?view=withdrawals&withdrawalId=old&status=all',
      '/collection/withdrawals?withdrawalId=old&status=all',
    ],
    ['?settings=withdraw', '/collection/withdrawals?settings=withdraw'],
  ])('旧钱包入口直达正确记录或配置 (%s)', (search, destination) => {
    show(search);
    expect(screen.getByRole('link', { name: '迁往记录页' })).toHaveAttribute(
      'href',
      destination,
    );
    expect(mocks.getDepositOrders).not.toHaveBeenCalled();
    expect(mocks.getWithdrawals).not.toHaveBeenCalled();
  });

  it('按充值关系定位归集任务并返回相关充值列表', async () => {
    mocks.getDepositOrders.mockResolvedValue({
      items: [{ ...deposit, sweepTaskId }],
      totalCount: 1,
    });
    mocks.getSweepTasks.mockResolvedValue({
      items: [
        {
          id: sweepTaskId,
          symbol: 'USDT',
          amount: '12.01',
          address,
          status: 'Completed',
        },
      ],
      totalCount: 1,
    });
    show(`?view=sweeps&depositId=${depositId}`);
    expect(await screen.findByText('已定位归集任务')).toBeInTheDocument();
    expect(mocks.getSweepTasks).toHaveBeenCalledWith(
      expect.objectContaining({ sweepTaskId, maxResultCount: 1 }),
    );
    expect(screen.getByRole('link', { name: '相关充值' })).toHaveAttribute(
      'href',
      `/operations/payments?view=deposits&sweepTaskId=${sweepTaskId}`,
    );
  });
});

vi.mock('@/abp/businessGrid', async (original) => {
  const grid = await original<typeof import('@/abp/businessGrid')>();
  return {
    ...grid,
    businessGridQueries: {
      ...grid.businessGridQueries,
      deposits: (
        input: import('@/abp/gridQuery').GridQuery,
        _signal: AbortSignal,
        scope: string,
      ) =>
        mocks.getDepositOrders({
          skipCount: input.skipCount,
          maxResultCount: input.maxResultCount,
          ...Object.fromEntries(new URLSearchParams(scope)),
        }),
      sweeps: (
        input: import('@/abp/gridQuery').GridQuery,
        _signal: AbortSignal,
        scope: string,
      ) =>
        mocks.getSweepTasks({
          skipCount: input.skipCount,
          maxResultCount: input.maxResultCount,
          ...Object.fromEntries(new URLSearchParams(scope)),
        }),
      withdrawals: (
        input: import('@/abp/gridQuery').GridQuery,
        _signal: AbortSignal,
        scope: string,
      ) =>
        mocks.getWithdrawals({
          skipCount: input.skipCount,
          maxResultCount: input.maxResultCount,
          ...Object.fromEntries(new URLSearchParams(scope)),
        }),
      vault: (
        input: import('@/abp/gridQuery').GridQuery,
        _signal: AbortSignal,
        scope: string,
      ) =>
        mocks.getVaultChecks({
          skipCount: input.skipCount,
          maxResultCount: input.maxResultCount,
          ...Object.fromEntries(new URLSearchParams(scope)),
        }),
    },
  };
});
