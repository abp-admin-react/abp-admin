vi.mock('ag-grid-react', () => import('../../../tests/gridTestDouble'));

import { render, screen } from '@testing-library/react';
import { App } from 'antd';
import type { ComponentPropsWithoutRef } from 'react';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import type { DepositOrderListDto } from '@/abp/chainAdmin';
import DepositsPage from './deposits';

const mocks = vi.hoisted(() => ({
  access: vi.fn(),
  model: vi.fn(),
  deposits: vi.fn(),
}));

vi.mock('@umijs/max', () => ({
  useAccess: mocks.access,
  useModel: mocks.model,
  Link: ({ to, ...props }: ComponentPropsWithoutRef<'a'> & { to: string }) => (
    <a href={to} {...props} />
  ),
}));
vi.mock('@/abp/chainAdmin', async (original) => ({
  ...(await original<typeof import('@/abp/chainAdmin')>()),
  getDepositOrders: mocks.deposits,
}));
vi.mock('@/abp/chainSignalR', () => ({
  onChainConnState: () => () => {},
  onChainDepositChanged: () => () => {},
  startChainSignalR: () => {},
}));

const depositId = 'c6d8f751-c115-4fff-aee2-6964056bcfad';
const txnId = '21a49a4f-d42e-4fc1-8665-75f0cce722a9';
const sweepTaskId = 'bb6893bd-c711-49ea-bab7-7cdc836e8b39';
const userId = '7ecdbee4-1032-442c-babf-f5f3bd9e6168';
const address = '0x1111111111111111111111111111111111111111';
const deposit: DepositOrderListDto = {
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
  status: 'Swept',
  manuallyReleased: false,
  sweepTaskId,
  balanceTxnId: txnId,
  creationTime: '2026-11-13T08:09:00Z',
};

function show(props: React.ComponentProps<typeof DepositsPage>) {
  render(
    <App>
      <DepositsPage embedded {...props} />
    </App>,
  );
}

describe('充值记录关联', () => {
  beforeEach(() => {
    vi.resetAllMocks();
    mocks.access.mockReturnValue({});
    mocks.model.mockReturnValue({});
    mocks.deposits.mockResolvedValue({ items: [deposit], totalCount: 1 });
  });

  it('精确定位后提供真实归集、流水和充值入账记录链接', async () => {
    mocks.access.mockReturnValue({
      canViewWeb3Operations: true,
      canViewCollections: true,
      canViewMemberFunds: true,
      canViewOwnOrders: true,
    });
    mocks.model.mockReturnValue({
      initialState: { currentUser: { userid: userId } },
    });
    show({ depositId });
    expect(await screen.findByText('已定位充值单')).toBeTruthy();
    expect(
      screen.getByRole('link', { name: `查看归集任务 ${sweepTaskId}` }),
    ).toHaveAttribute(
      'href',
      `/web3/operations?view=sweeps&sweepTaskId=${sweepTaskId}`,
    );
    expect(
      screen.getByRole('link', { name: `查看充值余额流水 ${txnId}` }),
    ).toHaveAttribute(
      'href',
      `/collection/member-funds?userId=${userId}&txnId=${txnId}`,
    );
    expect(
      screen.getByRole('link', {
        name: `查看充值单 ${depositId} 的充值入账记录`,
      }),
    ).toHaveAttribute(
      'href',
      `/collection/orders?orderType=TopUp&userId=${userId}&balanceTxnId=${txnId}`,
    );
    expect(mocks.deposits).toHaveBeenCalledExactlyOnceWith({
      depositId,
      skipCount: 0,
      maxResultCount: 1,
    });
  });

  it('只读记录角色会显示关联 ID，且本人权限不能从他人充值查看订单', async () => {
    mocks.access.mockReturnValue({ canViewOwnOrders: true });
    mocks.model.mockReturnValue({
      initialState: { currentUser: { userid: txnId } },
    });
    show({ depositId });
    expect(await screen.findByText(`余额流水 ${txnId}`)).toBeTruthy();
    expect(screen.getByText(`归集任务 ${sweepTaskId}`)).toBeTruthy();
    expect(
      screen.queryByRole('link', {
        name: /查看充值余额流水|查看归集任务|的充值入账记录/,
      }),
    ).toBeNull();
  });

  it('归集反查保留实际任务筛选，充值列表为记录提供准确深链', async () => {
    show({ sweepTaskId });
    expect(
      await screen.findByRole('link', { name: `查看充值单 ${depositId}` }),
    ).toHaveAttribute(
      'href',
      `/operations/payments?view=deposits&depositId=${depositId}`,
    );
    expect(mocks.deposits).toHaveBeenCalledExactlyOnceWith(
      expect.objectContaining({
        sweepTaskId,
        skipCount: 0,
        maxResultCount: 20,
      }),
    );
  });

  it('无效的归集任务链接不请求充值列表', async () => {
    show({ sweepTaskId: 'invalid' });
    expect(await screen.findByText('无效的归集任务链接')).toBeTruthy();
    expect(mocks.deposits).not.toHaveBeenCalled();
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
        mocks.deposits({
          skipCount: input.skipCount,
          maxResultCount: input.maxResultCount,
          ...Object.fromEntries(new URLSearchParams(scope)),
        }),
    },
  };
});
