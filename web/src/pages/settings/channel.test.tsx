import {
  act,
  fireEvent,
  render,
  screen,
  waitFor,
  within,
} from '@testing-library/react';
import { App as AntdApp } from 'antd';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import ChannelSettingsPage from './channel';

const mocks = vi.hoisted(() => ({
  useAccess: vi.fn(),
  useLocation: vi.fn(),
  getSettingGroups: vi.fn(),
  setSettingValues: vi.fn(),
  resetSettingValues: vi.fn(),
}));

vi.mock('@umijs/max', () => ({
  useAccess: mocks.useAccess,
  useLocation: mocks.useLocation,
}));
vi.mock('@/abp/settingUi', async (importOriginal) => ({
  ...(await importOriginal<typeof import('@/abp/settingUi')>()),
  getSettingGroups: mocks.getSettingGroups,
  setSettingValues: mocks.setSettingValues,
  resetSettingValues: mocks.resetSettingValues,
}));
vi.mock('@/requestErrorConfig', () => ({
  isNotFound: (error: { status?: number }) => error.status === 404,
}));
const quoteFields = [
  'PercentRate',
  'FixedFee',
  'SettlementFloor',
  'FixedFeeRate',
];
const quoteKeys = (channel: string) =>
  quoteFields.map((field) => `Ordering.Quote.${channel}.${field}`);
const alipayLimit = 'Collection.MaxAmount.AlipayPrecreate.Cny';
const wechatLimit = 'Collection.MaxAmount.WechatNative.Cny';
const stripeLimit = 'Collection.MaxAmount.StripeCheckout.Usd';
const balanceTimeout = 'Collection.Balance.DebitCommandTimeoutSeconds';
const commonQuoteKeys = [
  'Ordering.Quote.UsdCnyRate',
  'Ordering.Quote.ValidityMinutes',
  'Ordering.Order.ExpiryHours',
];
const financeKeys = [
  'Finance.Ledger.Enabled',
  'Finance.Wallet.UseLedgerProjection',
];
const group = (groupName: string, names: string[]): API.SettingGroup => ({
  groupName,
  settingInfos: names.map((name) => ({ name, displayName: name, value: '1' })),
});
const groups = [
  group('Web3', ['Collection.Chain.ScanIntervalSeconds']),
  group('Alipay', [alipayLimit]),
  group('WechatPay', [wechatLimit]),
  group('Stripe', [stripeLimit]),
  group('Collection', [balanceTimeout]),
  group('Ordering', [
    ...quoteKeys('Alipay'),
    ...quoteKeys('Wechat'),
    ...quoteKeys('Stripe'),
    ...quoteKeys('BalanceUsdt'),
    ...quoteKeys('BalanceUsdc'),
    ...commonQuoteKeys,
    'Ordering.Quote.Alipay.Other',
  ]),
  group('Finance', financeKeys),
];
const allKeys = groups.flatMap((item) =>
  (item.settingInfos ?? []).flatMap((info) => (info.name ? [info.name] : [])),
);

function page() {
  return (
    <AntdApp>
      <ChannelSettingsPage />
    </AntdApp>
  );
}

describe('渠道配置页', () => {
  beforeEach(() => {
    vi.resetAllMocks();
    mocks.useAccess.mockReturnValue({
      canManagePaymentSettings: true,
      canManageSettings: true,
      canUpdateSettings: true,
    });
    mocks.useLocation.mockReturnValue({ pathname: '/alipay/settings' });
    mocks.getSettingGroups.mockResolvedValue(groups);
    mocks.setSettingValues.mockResolvedValue(undefined);
    mocks.resetSettingValues.mockResolvedValue(undefined);
  });

  it.each([
    [
      '/alipay/settings',
      '支付宝收款设置',
      [alipayLimit, ...quoteKeys('Alipay')],
    ],
    [
      '/wechat/settings',
      '微信支付收款设置',
      [wechatLimit, ...quoteKeys('Wechat')],
    ],
    [
      '/stripe/settings',
      'Stripe 收款设置',
      [stripeLimit, ...quoteKeys('Stripe')],
    ],
    ['/collection/order-settings', '订单与报价设置', commonQuoteKeys],
    [
      '/collection/settings',
      '余额支付设置',
      [
        ...quoteKeys('BalanceUsdt'),
        ...quoteKeys('BalanceUsdc'),
        balanceTimeout,
      ],
    ],
    ['/finance/settings', '记账设置', financeKeys],
  ])('%s 只显示该页面可以管理的设置', async (pathname, title, settingNames) => {
    mocks.useLocation.mockReturnValue({ pathname });
    render(page());
    expect(within(screen.getByRole('navigation')).getByText(title)).toBeTruthy();
    for (const name of settingNames) {
      expect(await screen.findByLabelText(name)).toBeTruthy();
    }
    for (const name of allKeys.filter((key) => !settingNames.includes(key))) {
      expect(screen.queryByLabelText(name)).toBeNull();
    }
  });

  it('余额支付按币种分组', async () => {
    mocks.useLocation.mockReturnValue({ pathname: '/collection/settings' });
    render(page());
    expect(await screen.findByText('USDT 余额支付')).toBeInTheDocument();
    expect(screen.getByText('USDC 余额支付')).toBeInTheDocument();
  });

  it('没有渠道设置权限时不加载配置', () => {
    mocks.useAccess.mockReturnValue({ canManagePaymentSettings: false });
    render(page());
    expect(screen.getByText('没有可管理的设置权限。')).toBeTruthy();
    expect(mocks.getSettingGroups).not.toHaveBeenCalled();
  });

  it('加载失败可以重试，刷新后仍只显示当前渠道', async () => {
    mocks.getSettingGroups.mockRejectedValueOnce(new Error('temporary'));
    render(page());
    fireEvent.click(await screen.findByRole('button', { name: /重\s*试/ }));
    expect(await screen.findByLabelText(alipayLimit)).toBeTruthy();
    fireEvent.click(screen.getByRole('button', { name: /^保\s*存$/ }));
    await waitFor(() =>
      expect(mocks.getSettingGroups).toHaveBeenCalledTimes(3),
    );
    expect(await screen.findByLabelText(alipayLimit)).toBeTruthy();
    expect(screen.queryByLabelText(wechatLimit)).toBeNull();
  });

  it('保存和重置只提交当前渠道的限额、费率和结算项', async () => {
    render(page());
    const limitInput = await screen.findByLabelText(alipayLimit);
    fireEvent.change(limitInput, { target: { value: '2500.01' } });
    fireEvent.click(screen.getByRole('button', { name: /^保\s*存$/ }));
    await waitFor(() =>
      expect(mocks.setSettingValues).toHaveBeenCalledWith({
        [alipayLimit]: '2500.01',
        ...Object.fromEntries(quoteKeys('Alipay').map((name) => [name, '1'])),
      }),
    );
    await screen.findByLabelText(alipayLimit);
    fireEvent.click(await screen.findByRole('button', { name: '重置为默认值' }));
    fireEvent.click(await screen.findByRole('button', { name: /^重\s*置$/ }));
    await waitFor(() =>
      expect(mocks.resetSettingValues).toHaveBeenCalledWith([
        alipayLimit,
        ...quoteKeys('Alipay'),
      ]),
    );
  });

  it('宿主记账设置缺写权限时为只读', async () => {
    mocks.useAccess.mockReturnValue({ canManagePaymentSettings: true });
    mocks.useLocation.mockReturnValue({ pathname: '/finance/settings' });
    render(page());
    expect(await screen.findByLabelText(financeKeys[0])).toBeDisabled();
    expect(screen.queryByRole('button', { name: /^保\s*存$/ })).toBeNull();
    expect(mocks.setSettingValues).not.toHaveBeenCalled();
  });

  it.each(['/collection/order-settings', '/finance/settings'])(
    '租户有通用设置权限时，%s 仍只向宿主开放',
    (pathname) => {
      mocks.useAccess.mockReturnValue({
        canManageSettings: true,
        canManagePaymentSettings: false,
        canUpdateSettings: true,
      });
      mocks.useLocation.mockReturnValue({ pathname });
      render(page());
      expect(screen.getByText('没有可管理的设置权限。')).toBeTruthy();
      expect(mocks.getSettingGroups).not.toHaveBeenCalled();
    },
  );

  it.each([
    { available: [] },
    { available: [{ groupName: 'Alipay', settingInfos: [] }] },
  ])('当前渠道没有配置时显示空状态', async ({ available }) => {
    mocks.getSettingGroups.mockResolvedValue(available);
    render(page());
    expect(await screen.findByText('暂无可管理的设置。')).toBeTruthy();
    expect(screen.queryByRole('button', { name: /^保\s*存$/ })).toBeNull();
  });

  it('404 显示空状态', async () => {
    mocks.getSettingGroups.mockRejectedValue({ status: 404 });
    render(page());
    expect(await screen.findByText('暂无可管理的设置。')).toBeTruthy();
    expect(screen.queryByRole('button', { name: '重试' })).toBeNull();
  });

  it('切换渠道立即移除旧字段，并忽略旧渠道的迟到响应', async () => {
    let finishFirst: ((value: API.SettingGroup[]) => void) | undefined;
    mocks.getSettingGroups.mockReturnValueOnce(
      new Promise<API.SettingGroup[]>((resolve) => {
        finishFirst = resolve;
      }),
    );
    const view = render(page());
    mocks.useLocation.mockReturnValue({ pathname: '/wechat/settings' });
    view.rerender(page());
    expect(await screen.findByLabelText(wechatLimit)).toBeTruthy();
    await act(async () => finishFirst?.(groups));
    expect(screen.getByLabelText(wechatLimit)).toBeTruthy();
    expect(screen.queryByLabelText(alipayLimit)).toBeNull();
  });
});
