import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import {
  fireEvent,
  render,
  screen,
  waitFor,
  within,
} from '@testing-library/react';
import type { ReactNode } from 'react';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import OperationsOverview from './OperationsOverview';
import type { OperationAccess } from './operationsData';

const mocks = vi.hoisted(() => ({
  adminGetOrders: vi.fn(),
  getMyOrders: vi.fn(),
  getCollectionPayments: vi.fn(),
  getDepositOrders: vi.fn(),
  getSweepTasks: vi.fn(),
  getWithdrawals: vi.fn(),
  getWithdrawStatus: vi.fn(),
  getBalanceAdjustments: vi.fn(),
  useLocation: vi.fn(),
  replace: vi.fn(),
}));

vi.mock('@umijs/max', () => ({
  useLocation: mocks.useLocation,
  history: { replace: mocks.replace },
  Link: ({ to, children }: { to: string; children: ReactNode }) => (
    <a href={to}>{children}</a>
  ),
}));
vi.mock('@/abp/ordering', async (original) => ({
  ...(await original<typeof import('@/abp/ordering')>()),
  adminGetOrders: mocks.adminGetOrders,
  getMyOrders: mocks.getMyOrders,
}));
vi.mock('@/abp/chainAdmin', async (original) => ({
  ...(await original<typeof import('@/abp/chainAdmin')>()),
  getDepositOrders: mocks.getDepositOrders,
  getSweepTasks: mocks.getSweepTasks,
}));
vi.mock('@/abp/withdrawal', async (original) => ({
  ...(await original<typeof import('@/abp/withdrawal')>()),
  getWithdrawals: mocks.getWithdrawals,
  getWithdrawStatus: mocks.getWithdrawStatus,
}));
vi.mock('@/abp/balanceAdjustments', () => ({
  getBalanceAdjustments: mocks.getBalanceAdjustments,
}));
vi.mock('@/abp/collectionAdmin', () => ({
  getCollectionPayments: mocks.getCollectionPayments,
}));

function show(access: OperationAccess, userId?: string) {
  const client = new QueryClient();
  const element = (id = userId) => (
    <QueryClientProvider client={client}>
      <OperationsOverview access={access} userId={id} />
    </QueryClientProvider>
  );
  const view = render(element());
  return {
    changeUser: (id: string) => view.rerender(element(id)),
    navigate: (search: string) => {
      mocks.useLocation.mockReturnValue({ pathname: '/welcome', search });
      view.rerender(element());
    },
  };
}

const sampleOrder = {
  id: 'order-1',
  orderNo: 'ORDER-1',
  subject: '订单说明',
  amount: '4.01',
  currency: 'Usd',
  status: 'Paid',
  creationTime: '2026-11-13T07:08:00Z',
};
const samplePayment = {
  id: 'payment-1',
  collectionNo: 'PAYMENT-1',
  channel: 'AlipayPrecreate',
  status: 'Completed',
  currency: 'Cny',
  amount: '200.11',
  creationTime: '2026-11-13T08:09:00Z',
};

describe('工作台业务运营', () => {
  beforeEach(() => {
    vi.resetAllMocks();
    for (const mock of Object.values(mocks))
      mock.mockResolvedValue({ items: [], totalCount: 0 });
    mocks.getWithdrawStatus.mockResolvedValue({
      pendingCount: 0,
      broadcastFailedCount: 0,
    });
    mocks.useLocation.mockReturnValue({ pathname: '/welcome', search: '' });
  });

  it('合并排序记录、保留币种并按类型筛选，普通用户仅查本人的订单', async () => {
    mocks.getMyOrders.mockResolvedValue({
      items: [sampleOrder],
      totalCount: 1,
    });
    mocks.getCollectionPayments.mockResolvedValue({
      items: [samplePayment],
      totalCount: 1,
    });
    const { navigate } = show(
      { canViewOrders: true, canViewCollectionPayments: true },
      'person-1',
    );
    await screen.findByRole('link', { name: 'ORDER-1' });
    await screen.findByRole('link', { name: 'PAYMENT-1' });
    const activity = within(screen.getByRole('region', { name: '最新动态' }));
    const rows = activity.getAllByRole('listitem');
    expect(rows[0]).toHaveTextContent('PAYMENT-1');
    expect(rows[1]).toHaveTextContent('ORDER-1');
    expect(rows[0]).toHaveTextContent('200.11 CNY');
    expect(rows[1]).toHaveTextContent('4.01 USD');
    expect(activity.getByRole('link', { name: 'ORDER-1' })).toHaveAttribute(
      'href',
      '/collection/orders?orderId=order-1',
    );
    expect(activity.getByRole('link', { name: 'PAYMENT-1' })).toHaveAttribute(
      'href',
      '/operations/payments?paymentId=payment-1',
    );
    fireEvent.click(activity.getByRole('button', { name: '我的订单' }));
    expect(mocks.replace).toHaveBeenCalledWith('/welcome?activity=my-orders');
    navigate('?activity=my-orders');
    expect(activity.queryByRole('link', { name: 'PAYMENT-1' })).toBeNull();
    expect(
      activity.getByRole('link', { name: '查看我的订单' }),
    ).toHaveAttribute('href', '/collection/orders');
    expect(mocks.getMyOrders).toHaveBeenCalledExactlyOnceWith({
      skipCount: 0,
      maxResultCount: 8,
    });
    expect(mocks.adminGetOrders).not.toHaveBeenCalled();
    expect(mocks.getDepositOrders).not.toHaveBeenCalled();
    expect(mocks.getWithdrawals).not.toHaveBeenCalled();
    expect(mocks.getBalanceAdjustments).not.toHaveBeenCalled();
  });

  it('记录较多时仍按类型截取最近八条，链上记录可深链到具体 ID', async () => {
    const deposits = Array.from({ length: 9 }, (_, i) => ({
      id: `deposit-${i}`,
      networkName: 'QA',
      symbol: 'USDT',
      txHash: `HASH-${i}`,
      amount: '10',
      status: 'Detected',
      creationTime: `2026-11-13T08:0${i}:00Z`,
    }));
    mocks.getDepositOrders.mockResolvedValue({
      items: deposits,
      totalCount: 9,
    });
    mocks.getWithdrawals.mockResolvedValue({
      items: [
        {
          id: 'withdrawal-1',
          networkName: 'QA',
          currency: 'Usdt',
          amount: '32',
          status: 'Pending',
          creationTime: '2026-11-13T08:15:00Z',
        },
      ],
      totalCount: 1,
    });
    const { navigate } = show(
      {
        canViewWeb3Operations: true,
        canViewCollections: true,
        canAdminWithdrawals: true,
      },
      'operator',
    );
    const activity = within(screen.getByRole('region', { name: '最新动态' }));
    expect(
      await activity.findByRole('link', { name: 'QA USDT 提现' }),
    ).toHaveAttribute(
      'href',
      '/web3/operations?view=withdrawals&status=all&withdrawalId=withdrawal-1',
    );
    navigate('?activity=deposits');
    expect(activity.getAllByRole('listitem')).toHaveLength(8);
    expect(
      activity.getAllByRole('link', { name: 'QA USDT 充值' })[0],
    ).toHaveAttribute(
      'href',
      '/web3/operations?view=deposits&depositId=deposit-8',
    );
    expect(activity.queryByText('HASH-0')).toBeNull();
    expect(activity.queryByRole('link', { name: 'QA USDT 提现' })).toBeNull();
  });

  it('精确待办计数链接到同口径的目标列表', async () => {
    mocks.getWithdrawStatus.mockResolvedValue({
      pendingCount: 12,
      broadcastFailedCount: 3,
    });
    mocks.getBalanceAdjustments.mockImplementation(async ({ status }) => ({
      items: [],
      totalCount: status === 0 ? 9 : 0,
    }));
    mocks.getSweepTasks.mockResolvedValue({ items: [], totalCount: 7 });
    mocks.adminGetOrders.mockImplementation(async ({ hasAnomaly }) => ({
      items: hasAnomaly ? [] : [sampleOrder],
      totalCount: hasAnomaly ? 6 : 1,
    }));
    show(
      {
        canViewOrders: true,
        canViewAllOrders: true,
        canViewWeb3Operations: true,
        canViewCollections: true,
        canAdminWithdrawals: true,
        canViewBalanceAdjustments: true,
      },
      'operator-1',
    );

    const tasks = within(screen.getByRole('region', { name: '待处理' }));
    const expected = [
      [/待审核提现.*12/, '/web3/operations?view=withdrawals&status=Pending'],
      [
        /打款失败.*3/,
        '/web3/operations?view=withdrawals&status=BroadcastFailed',
      ],
      [/待审批调账.*9/, '/finance/balance-adjustments?status=Pending'],
      [/归集失败.*7/, '/web3/operations?view=sweeps&status=Failed'],
      [/有异常的订单.*6/, '/collection/orders?view=all&hasAnomaly=true'],
    ] as const;
    for (const [name, path] of expected) {
      expect(await tasks.findByRole('link', { name })).toHaveAttribute(
        'href',
        path,
      );
    }
    expect(
      await screen.findByRole('link', { name: 'ORDER-1' }),
    ).toHaveAttribute('href', '/collection/orders?view=all&orderId=order-1');
    expect(mocks.adminGetOrders).toHaveBeenCalledWith({
      skipCount: 0,
      maxResultCount: 1,
      hasAnomaly: true,
    });
    expect(mocks.getMyOrders).not.toHaveBeenCalled();
  });

  it('一项查询失败仍展示其他数据，一键刷新可恢复失败来源', async () => {
    mocks.getMyOrders.mockResolvedValue({
      items: [sampleOrder],
      totalCount: 1,
    });
    mocks.getCollectionPayments
      .mockRejectedValueOnce(new Error('offline'))
      .mockResolvedValue({ items: [samplePayment], totalCount: 1 });
    show({ canViewOrders: true, canViewCollectionPayments: true }, 'person-1');
    expect(await screen.findByText('暂时无法读取收款的最新记录')).toBeTruthy();
    expect(screen.getByRole('link', { name: 'ORDER-1' })).toBeTruthy();
    fireEvent.click(screen.getByRole('button', { name: /刷新业务数据/ }));
    expect(await screen.findByRole('link', { name: 'PAYMENT-1' })).toBeTruthy();
    expect(screen.queryByText('暂时无法读取收款的最新记录')).toBeNull();
    expect(mocks.getMyOrders).toHaveBeenCalledTimes(2);
  });

  it('无运营授权或未登录时不查询，对换账号读取独立缓存', async () => {
    show({}, 'system-1');
    const { changeUser } = show({ canViewOrders: true });
    expect(screen.queryByRole('region', { name: '业务运营' })).toBeNull();
    expect(mocks.getMyOrders).not.toHaveBeenCalled();
    changeUser('person-1');
    await waitFor(() => expect(mocks.getMyOrders).toHaveBeenCalledTimes(1));
    changeUser('person-2');
    await waitFor(() => expect(mocks.getMyOrders).toHaveBeenCalledTimes(2));
  });
});
