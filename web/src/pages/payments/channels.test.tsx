import {
  fireEvent,
  render,
  screen,
  waitFor,
  within,
} from '@testing-library/react';
import type { ReactNode } from 'react';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { ModuleAvailabilityContext } from '@/hooks/useModuleAvailability';
import PaymentsPage from './index';

const mocks = vi.hoisted(() => ({
  table: vi.fn(),
  getPayments: vi.fn(),
  getPayment: vi.fn(),
  getRefunds: vi.fn(),
  refund: vi.fn(),
  access: vi.fn(),
  useLocation: vi.fn(),
  push: vi.fn(),
  replace: vi.fn(),
  deposits: vi.fn(),
}));

vi.mock('@umijs/max', () => ({
  useAccess: mocks.access,
  useLocation: mocks.useLocation,
  useModel: () => ({
    initialState: { currentUser: { userid: 'current-user' } },
  }),
  history: { push: mocks.push, replace: mocks.replace },
  Link: ({ to, children }: { to: string; children: ReactNode }) => (
    <a href={to}>{children}</a>
  ),
}));
vi.mock('@ant-design/pro-components', () => ({
  PageContainer: ({
    children,
    extra,
  }: {
    children: ReactNode;
    extra?: ReactNode;
  }) => (
    <>
      {extra}
      {children}
    </>
  ),
}));
vi.mock('@/components/AutoHeightProTable', () => ({
  default: (props: unknown) => {
    mocks.table(props);
    return null;
  },
}));
vi.mock('@/abp/collectionAdmin', () => ({
  getCollectionPayments: mocks.getPayments,
  getCollectionPayment: mocks.getPayment,
  getCollectionRefunds: mocks.getRefunds,
  requestCollectionRefund: mocks.refund,
}));
vi.mock('@/requestErrorConfig', () => ({ isNotFound: () => false }));
vi.mock('../settings/channel', () => ({
  PaymentChannelSettings: () => <p>渠道配置面板</p>,
}));
vi.mock('../collection/deposits', () => ({
  default: (props: unknown) => {
    mocks.deposits(props);
    return <p>充值记录面板</p>;
  },
}));
vi.mock('../collection/network', () => ({
  default: () => <p>网络充值配置面板</p>,
}));
vi.mock('../collection/rules', () => ({
  Web3RuleSection: () => <p>充值确认配置面板</p>,
}));

describe('渠道收款记录', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    mocks.access.mockReturnValue({ canViewCollectionPayments: true });
    mocks.getPayments.mockResolvedValue({ items: [], totalCount: 0 });
    mocks.getPayment.mockResolvedValue({
      id: 'payment-1',
      status: 'Completed',
      currency: 'Cny',
      amount: '20.50',
    });
    mocks.getRefunds.mockResolvedValue([]);
    mocks.useLocation.mockReturnValue({
      pathname: '/operations/payments',
      search: '',
    });
  });

  it.each([
    ['alipay', 'AlipayPrecreate'],
    ['wechat', 'WechatNative'],
    ['stripe', 'StripeCheckout'],
  ] as const)('%s 历史链接预选渠道并保留渠道列', async (view, channel) => {
    mocks.useLocation.mockReturnValue({
      pathname: '/operations/payments',
      search: `?view=${view}`,
    });
    render(<PaymentsPage />);
    const props = mocks.table.mock.calls.at(-1)?.[0];
    await props.request({
      current: 2,
      pageSize: 10,
      channel: 'Other',
      status: 'Completed',
    });
    expect(mocks.getPayments).toHaveBeenCalledWith({
      skipCount: 10,
      maxResultCount: 10,
      filter: undefined,
      status: 'Completed',
      channel,
    });
    expect(
      props.columns.find(
        (column: { dataIndex: string }) => column.dataIndex === 'channel',
      ),
    ).toMatchObject({ search: false });
    expect(props.params).toEqual({ channel });
    expect(screen.getByRole('tab', { name: '第三方收款' })).toHaveAttribute(
      'aria-selected',
      'true',
    );
  });

  it('第三方收款提供表内渠道筛选，旧“全部”不加渠道限制', async () => {
    mocks.useLocation.mockReturnValue({
      pathname: '/operations/payments',
      search: '?view=all',
    });
    render(<PaymentsPage />);
    const props = mocks.table.mock.calls.at(-1)?.[0];
    await props.request({});
    expect(mocks.getPayments).toHaveBeenCalledWith(
      expect.objectContaining({ channel: undefined }),
    );
    render(props.toolBarRender());
    expect(screen.getByRole('combobox', { name: '渠道' })).toBeTruthy();
    expect(screen.getByText('全部外部渠道')).toBeTruthy();
  });

  it('充值记录角色直接打开充值，不请求外部渠道接口', () => {
    mocks.access.mockReturnValue({ canViewDepositRecords: true });
    mocks.useLocation.mockReturnValue({
      pathname: '/operations/payments',
      search: '?view=deposits&depositId=deposit-id&status=Confirmed',
    });
    render(<PaymentsPage />);
    expect(screen.getByRole('tab', { name: '账户充值' })).toHaveAttribute(
      'aria-selected',
      'true',
    );
    expect(screen.queryByRole('tab', { name: '第三方收款' })).toBeNull();
    expect(mocks.deposits).toHaveBeenCalledWith(
      expect.objectContaining({
        depositId: 'deposit-id',
        initialStatus: 'Confirmed',
      }),
    );
    expect(mocks.table).not.toHaveBeenCalled();
    expect(mocks.getPayment).not.toHaveBeenCalled();
  });

  it('链网管理员通过收款配置管理充值，没有第三方渠道时不请求第三方配置', () => {
    mocks.access.mockReturnValue({
      canViewDepositRecords: true,
      canConfigureCollectionNetwork: true,
      canAdminNetwork: true,
    });
    mocks.useLocation.mockReturnValue({
      pathname: '/operations/payments',
      search: '?view=deposits&settings=deposit',
    });
    render(<PaymentsPage />);
    expect(screen.getByText('网络充值配置面板')).toBeTruthy();
    expect(screen.queryByText('渠道配置面板')).toBeNull();
    expect(mocks.deposits).not.toHaveBeenCalled();
  });

  it.each([
    ['Order', 'current-user', true],
    ['Order', 'another-user', false],
    ['Other', 'current-user', false],
  ] as const)(
    '收款明细订单关联按业务类型和所有者授权 (%s %s)',
    async (businessType, userId, linked) => {
      mocks.access.mockReturnValue({
        canViewCollectionPayments: true,
        canViewOwnOrders: true,
      });
      mocks.useLocation.mockReturnValue({
        pathname: '/operations/payments',
        search: '?paymentId=payment-1',
      });
      mocks.getPayment.mockResolvedValue({
        id: 'payment-1',
        status: 'Completed',
        currency: 'Cny',
        amount: '20.50',
        businessType,
        userId,
        businessId: 'order-id',
      });
      render(<PaymentsPage />);
      await screen.findByText('order-id');
      const order = screen.queryByRole('link', { name: 'order-id' });
      if (linked)
        expect(order).toHaveAttribute(
          'href',
          '/collection/orders?orderId=order-id',
        );
      else expect(order).toBeNull();
    },
  );

  it('只配置渠道时不请求交易或退款记录', () => {
    mocks.access.mockReturnValue({ canManagePaymentSettings: true });
    mocks.useLocation.mockReturnValue({
      pathname: '/operations/payments',
      search: '?paymentId=payment-1',
    });
    render(<PaymentsPage />);
    expect(screen.getByText('渠道配置面板')).toBeTruthy();
    expect(mocks.table).not.toHaveBeenCalled();
    expect(mocks.getPayment).not.toHaveBeenCalled();
    expect(mocks.getRefunds).not.toHaveBeenCalled();
  });

  it('停用后仍可查原退款，进入渠道配置再返回会保留未确认的退款请求号', async () => {
    mocks.access.mockReturnValue({
      canViewCollectionPayments: true,
      canRefundCollectionPayments: true,
      canManagePaymentSettings: true,
    });
    mocks.refund
      .mockRejectedValueOnce(new Error('response lost'))
      .mockResolvedValue({
        id: 'refund-1',
        status: 'Pending',
        currency: 1,
        amount: '2.50',
      });
    const location = {
      pathname: '/operations/payments',
      search: '?paymentId=payment-1',
    };
    mocks.useLocation.mockReturnValue(location);
    const workspace = () => (
      <ModuleAvailabilityContext.Provider
        value={{ version: 1, initialized: true, states: { Collection: false } }}
      >
        <PaymentsPage />
      </ModuleAvailabilityContext.Provider>
    );
    const view = render(workspace());
    expect(screen.getByText('收款已停止受理')).toBeTruthy();
    fireEvent.change(
      await screen.findByRole('textbox', { name: /原路退款金额/ }),
      { target: { value: '2.50' } },
    );
    fireEvent.click(screen.getByRole('button', { name: '发起退款' }));
    const confirmation = await screen.findByRole('tooltip');
    fireEvent.click(within(confirmation).getByRole('button', { name: 'OK' }));
    await waitFor(() => expect(mocks.refund).toHaveBeenCalledOnce());
    const firstInput = mocks.refund.mock.calls[0][0];
    expect(
      await screen.findByText('未收到退款请求结果，请重试原请求'),
    ).toBeTruthy();

    mocks.useLocation.mockReturnValue({
      ...location,
      search: `${location.search}&settings=channels`,
    });
    view.rerender(workspace());
    expect(screen.getByText('渠道配置面板')).toBeTruthy();
    mocks.useLocation.mockReturnValue(location);
    view.rerender(workspace());

    fireEvent.click(
      await screen.findByRole('button', { name: '重试原退款请求' }),
    );
    await waitFor(() => expect(mocks.refund).toHaveBeenCalledTimes(2));
    expect(mocks.refund).toHaveBeenLastCalledWith(firstInput);
  });
});
