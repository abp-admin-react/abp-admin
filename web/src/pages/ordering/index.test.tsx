import { actionCell, fetchGridBlock } from '@root/tests/gridInteraction';
import {
  act,
  fireEvent,
  render,
  screen,
  waitFor,
  within,
} from '@testing-library/react';
import { App as AntdApp, ConfigProvider } from 'antd';
import type { ComponentPropsWithoutRef } from 'react';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { ModuleAvailabilityContext } from '@/hooks/useModuleAvailability';
import OrderingPage from './index';

const mocks = vi.hoisted(() => ({
  request: vi.fn(),
  access: vi.fn(),
  model: vi.fn(),
  search: vi.fn(),
  tableRequest: vi.fn(),
  push: vi.fn(),
  replace: vi.fn(),
  order: { id: 'order-1', orderNo: 'ORD-1', status: 'PendingPayment' },
}));

vi.mock('@umijs/max', () => ({
  request: mocks.request,
  useAccess: mocks.access,
  useLocation: () => ({
    pathname: '/collection/orders',
    search: mocks.search(),
  }),
  useModel: mocks.model,
  history: { push: mocks.push, replace: mocks.replace },
  Link: ({ to, ...props }: ComponentPropsWithoutRef<'a'> & { to: string }) => (
    <a href={to} {...props} />
  ),
}));
vi.mock('@/requestErrorConfig', () => ({
  isNotFound: (error: { response?: { status?: number } }) =>
    error.response?.status === 404,
}));
vi.mock('ag-grid-react', () => ({
  AgGridReact: (props: any) => {
    mocks.tableRequest(props);
    return actionCell(props, mocks.order);
  },
}));

function renderPage(acceptsOrders = true) {
  render(
    <ModuleAvailabilityContext.Provider
      value={{
        version: 1,
        initialized: true,
        states: { Orders: acceptsOrders, Collection: acceptsOrders },
      }}
    >
      <ConfigProvider theme={{ token: { motion: false } }}>
        <AntdApp>
          <OrderingPage />
        </AntdApp>
      </ConfigProvider>
    </ModuleAvailabilityContext.Provider>,
  );
}

describe('订单操作', () => {
  beforeEach(() => {
    vi.resetAllMocks();
    mocks.request.mockResolvedValue([]);
    mocks.access.mockReturnValue({ canViewOwnOrders: true });
    mocks.model.mockReturnValue({});
    mocks.search.mockReturnValue('');
  });

  it('创建请求只发一次，提交中无法关闭弹窗', async () => {
    const create = Promise.withResolvers<object>();
    mocks.request.mockImplementation((path: string) =>
      path === '/api/app/order' ? create.promise : Promise.resolve([]),
    );
    renderPage();
    fireEvent.click(screen.getByRole('button', { name: /创建订单/ }));
    const dialog = await screen.findByRole('dialog');
    fireEvent.change(within(dialog).getByLabelText('标题'), {
      target: { value: '测试订单' },
    });
    const submit = within(dialog).getByRole('button', { name: '创建订单' });
    act(() => {
      submit.click();
      submit.click();
    });
    const createCalls = () =>
      mocks.request.mock.calls.filter(([path]) => path === '/api/app/order');
    await waitFor(() => expect(createCalls()).toHaveLength(1));
    const cancel = within(dialog).getByRole('button', { name: /取\s*消/ });
    expect(cancel).toBeDisabled();
    fireEvent.click(cancel);
    expect(screen.getByRole('dialog')).toBe(dialog);

    await act(async () => create.resolve(mocks.order));
    await waitFor(() => expect(screen.queryByRole('dialog')).toBeNull());
    expect(createCalls()).toHaveLength(1);
  });

  it('发起支付只请求一次，收到凭据后切换方式会清掉旧凭据', async () => {
    const payment = Promise.withResolvers<object>();
    const requestPath = '/api/app/order/start-pay/order-1';
    mocks.request.mockImplementation((path: string) =>
      path === requestPath ? payment.promise : Promise.resolve([]),
    );
    renderPage();
    fireEvent.click(screen.getByRole('button', { name: /支\s*付/ }));
    const dialog = await screen.findByRole('dialog');
    const method = within(dialog).getByRole('combobox', { name: '支付方式' });
    const submit = within(dialog).getByRole('button', { name: '发起支付' });
    act(() => {
      submit.click();
      submit.click();
    });
    await waitFor(() => expect(method).toBeDisabled());
    expect(
      within(dialog).getByRole('button', { name: /取\s*消/ }),
    ).toBeDisabled();
    await act(async () =>
      payment.resolve({
        outcome: 'ChannelCreated',
        channelPayload: 'weixin://wxpay/test-order',
      }),
    );
    expect(
      await within(dialog).findByText('weixin://wxpay/test-order'),
    ).toBeTruthy();
    expect(
      within(dialog).queryByRole('button', { name: '发起支付' }),
    ).toBeNull();
    expect(
      within(dialog).getByRole('button', { name: '复制支付凭据' }),
    ).toBeTruthy();
    fireEvent.click(
      within(dialog).getByRole('button', { name: '更换支付方式' }),
    );
    expect(within(dialog).queryByText('weixin://wxpay/test-order')).toBeNull();
    expect(
      within(dialog).getByRole('combobox', { name: '支付方式' }),
    ).toBeEnabled();
    expect(
      mocks.request.mock.calls.filter(([path]) => path === requestPath),
    ).toHaveLength(1);
  });

  it('停止受理后保留我的订单和旧订单取消动作', async () => {
    await act(async () => renderPage(false));
    expect(screen.getByRole('tab', { name: '我的订单' })).toBeTruthy();
    expect(screen.queryByRole('tab', { name: '全部订单' })).toBeNull();
    expect(screen.getByRole('button', { name: /创建订单/ })).toBeDisabled();
    expect(screen.getByRole('button', { name: /支\s*付/ })).toBeDisabled();
    expect(screen.getByRole('button', { name: /取\s*消/ })).toBeEnabled();
  });

  it.each([
    ['', 'Business'],
    ['?orderType=TopUp', 'TopUp'],
  ])('给我的订单接口传入网址所选类型（%s）', async (search, orderType) => {
    mocks.search.mockReturnValue(search);
    mocks.request.mockResolvedValue({ items: [], totalCount: 0 });
    await act(async () => renderPage());
    const tableRequest = mocks.tableRequest.mock.lastCall?.[0];
    await fetchGridBlock(tableRequest, { endRow: 10 });
    expect(mocks.request).toHaveBeenCalledWith(
      `/api/app/order/query?orderType=${orderType}`,
      expect.objectContaining({
        method: 'POST',
        data: expect.objectContaining({ maxResultCount: 10 }),
      }),
    );
    expect(!!screen.queryByRole('button', { name: /创建订单/ })).toBe(
      orderType === 'Business',
    );
  });

  it('精确打开充值订单并用真实流水号链接，保留充值口径', async () => {
    const orderId = '85326c91-6b75-4ef0-9429-3f6e8af7c65f';
    const owner = '74326c91-6b75-4ef0-9429-3f6e8af7c65f';
    const txnId = '50326c91-6b75-4ef0-9429-3f6e8af7c65f';
    mocks.search.mockReturnValue(`?orderId=${orderId}`);
    mocks.access.mockReturnValue({
      canViewAllOrders: true,
      canViewMemberFunds: true,
    });
    mocks.request.mockResolvedValue({
      id: orderId,
      orderNo: 'TOPUP-785',
      userId: owner,
      orderType: 'TopUp',
      status: 'Paid',
      payChannel: 'BalanceUsdt',
      payAmount: '7.13',
      payCurrency: 'USDT',
      paidAt: '2026-11-17T01:00:00Z',
      balanceTxnId: txnId,
    });
    renderPage(false);
    await screen.findByText('充值入账详情 · TOPUP-785');
    const dialog = screen.getByRole('dialog');
    expect(within(dialog).getByText('账户充值 · 入账 7.13 USDT')).toBeTruthy();
    expect(within(dialog).getByText('已入账')).toBeTruthy();
    expect(within(dialog).queryByText(/余额支付/)).toBeNull();
    expect(
      within(dialog).getByRole('link', { name: `查看余额流水 ${txnId}` }),
    ).toHaveAttribute(
      'href',
      `/collection/member-funds?userId=${owner}&txnId=${txnId}`,
    );
    expect(mocks.request).toHaveBeenCalledExactlyOnceWith(
      `/api/app/order-admin/${orderId}`,
      { method: 'GET' },
    );
  });

  it('订单查看角色可见收款单号，且不会额外请求受限收款详情', async () => {
    const orderId = '85326c91-6b75-4ef0-9429-3f6e8af7c65f';
    const paymentId = '74326c91-6b75-4ef0-9429-3f6e8af7c65f';
    mocks.search.mockReturnValue(`?orderId=${orderId}`);
    mocks.access.mockReturnValue({ canViewAllOrders: true });
    mocks.request.mockResolvedValue({
      id: orderId,
      orderNo: 'ORD-5',
      orderType: 'Business',
      subject: '测试订单',
      status: 'Paid',
      payChannel: 'AlipayPrecreate',
      collectionId: paymentId,
    });
    renderPage();
    await screen.findByText('订单详情 · ORD-5');
    const dialog = screen.getByRole('dialog');
    expect(within(dialog).getByText(`渠道收款单 ${paymentId}`)).toBeTruthy();
    expect(
      within(dialog).queryByRole('link', { name: /查看渠道收款单/ }),
    ).toBeNull();
    expect(mocks.request).toHaveBeenCalledExactlyOnceWith(
      `/api/app/order-admin/${orderId}`,
      { method: 'GET' },
    );
  });

  it('订单不存在时保留列表并可清除深链', async () => {
    const orderId = '85326c91-6b75-4ef0-9429-3f6e8af7c65f';
    mocks.search.mockReturnValue(`?view=all&orderId=${orderId}`);
    mocks.access.mockReturnValue({ canViewAllOrders: true });
    mocks.request.mockRejectedValue({ response: { status: 404 } });
    renderPage();
    expect(await screen.findByText('未找到该订单')).toBeTruthy();
    expect(screen.queryByRole('dialog')).toBeNull();
    fireEvent.click(screen.getByRole('button', { name: '查看订单列表' }));
    expect(mocks.replace).toHaveBeenCalledWith('/collection/orders?view=all');
  });

  it('充值入账关联通过用户与流水 ID 精确筛选，空结果保持该范围', async () => {
    const userId = '74326c91-6b75-4ef0-9429-3f6e8af7c65f';
    const txnId = '50326c91-6b75-4ef0-9429-3f6e8af7c65f';
    mocks.search.mockReturnValue(
      `?orderType=TopUp&userId=${userId}&balanceTxnId=${txnId}`,
    );
    mocks.access.mockReturnValue({ canViewAllOrders: true });
    mocks.request.mockResolvedValue({ items: [], totalCount: 0 });
    await act(async () => renderPage());
    const tableRequest = mocks.tableRequest.mock.lastCall?.[0];
    expect(await fetchGridBlock(tableRequest)).toEqual({
      items: [],
      totalCount: 0,
    });
    expect(mocks.request).toHaveBeenCalledExactlyOnceWith(
      `/api/app/order-admin/query?orderType=TopUp&userId=${userId}&balanceTxnId=${txnId}`,
      expect.objectContaining({
        method: 'POST',
        data: expect.objectContaining({ maxResultCount: 20 }),
      }),
    );
    expect(screen.getByText(`余额流水 ${txnId} 的充值入账记录`)).toBeTruthy();
  });

  it('仅有本人订单权限时，拒绝他人的充值入账筛选', async () => {
    const userId = '74326c91-6b75-4ef0-9429-3f6e8af7c65f';
    const txnId = '50326c91-6b75-4ef0-9429-3f6e8af7c65f';
    mocks.search.mockReturnValue(
      `?orderType=TopUp&userId=${userId}&balanceTxnId=${txnId}`,
    );
    mocks.model.mockReturnValue({
      initialState: { currentUser: { userid: txnId } },
    });
    await act(async () => renderPage());
    expect(screen.getByText('充值入账记录链接无效或没有查看权限')).toBeTruthy();
    expect(mocks.tableRequest).not.toHaveBeenCalled();
    expect(mocks.request).not.toHaveBeenCalledWith(
      '/api/app/order',
      expect.anything(),
    );
  });
});
