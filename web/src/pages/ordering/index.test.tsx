import {
  act,
  fireEvent,
  render,
  screen,
  waitFor,
  within,
} from '@testing-library/react';
import { App as AntdApp, ConfigProvider } from 'antd';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import OrderingPage from './index';

const mocks = vi.hoisted(() => ({
  request: vi.fn(),
  order: { id: 'order-1', orderNo: 'ORD-1', status: 'PendingPayment' },
}));

vi.mock('@umijs/max', () => ({
  request: mocks.request,
  useAccess: () => ({ canViewOwnOrders: true }),
  useLocation: () => ({ pathname: '/collection/orders', search: '' }),
}));
vi.mock('@/requestErrorConfig', () => ({ isNotFound: () => false }));
vi.mock('@/components/AutoHeightProTable', () => ({
  default: ({
    toolBarRender,
    columns,
  }: {
    toolBarRender?: () => React.ReactNode;
    columns: {
      title: string;
      render?: (value: unknown, order: typeof mocks.order) => React.ReactNode;
    }[];
  }) => (
    <>
      {toolBarRender?.()}
      {columns.find((column) => column.title === '操作')?.render?.(null, mocks.order)}
    </>
  ),
}));

function renderPage() {
  render(
    <ConfigProvider theme={{ token: { motion: false } }}>
      <AntdApp>
        <OrderingPage />
      </AntdApp>
    </ConfigProvider>,
  );
}

describe('订单操作', () => {
  beforeEach(() => {
    mocks.request.mockReset().mockResolvedValue([]);
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
    expect(within(dialog).getByRole('button', { name: /取\s*消/ })).toBeDisabled();
    await act(async () =>
      payment.resolve({
        outcome: 'ChannelCreated',
        channelPayload: 'weixin://wxpay/test-order',
      }),
    );
    expect(await within(dialog).findByText('weixin://wxpay/test-order')).toBeTruthy();
    expect(within(dialog).queryByRole('button', { name: '发起支付' })).toBeNull();
    expect(within(dialog).getByRole('button', { name: '复制支付凭据' })).toBeTruthy();
    fireEvent.click(within(dialog).getByRole('button', { name: '更换支付方式' }));
    expect(within(dialog).queryByText('weixin://wxpay/test-order')).toBeNull();
    expect(within(dialog).getByRole('combobox', { name: '支付方式' })).toBeEnabled();
    expect(mocks.request.mock.calls.filter(([path]) => path === requestPath)).toHaveLength(1);
  });
});
