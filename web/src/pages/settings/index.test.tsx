import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { App as AntdApp } from 'antd';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import FrameworkConfiguration from './FrameworkConfiguration';
import SettingsPage from './index';

const mocks = vi.hoisted(() => ({
  useAccess: vi.fn(),
  getSettingGroups: vi.fn(),
  getEmailSettings: vi.fn(),
  updateEmailSettings: vi.fn(),
  sendTestEmail: vi.fn(),
  getTimezone: vi.fn(),
  getTimezones: vi.fn(),
  updateTimezone: vi.fn(),
}));

vi.mock('@umijs/max', () => ({
  useAccess: mocks.useAccess,
  useLocation: () => ({ pathname: '/administration/settings', search: '' }),
  Link: ({ to, children }: { to: string; children: React.ReactNode }) => (
    <a href={to}>{children}</a>
  ),
  history: { replace: vi.fn() },
}));
vi.mock('@/abp/settings', () => mocks);
vi.mock('@/abp/settingUi', () => ({
  getSettingGroups: mocks.getSettingGroups,
}));
vi.mock('./SettingGroupPanel', () => ({
  default: ({ group }: { group: API.SettingGroup }) =>
    group.settingInfos?.map((info) => <div key={info.name}>{info.name}</div>),
}));

const emailSettings = {
  defaultFromDisplayName: '管理员',
  defaultFromAddress: 'admin@example.test',
  smtpHost: 'smtp.example.test',
  smtpPort: 587,
  smtpEnableSsl: true,
  smtpUseDefaultCredentials: false,
};

function renderPage(owner?: 'notification' | 'timezone') {
  return render(
    <AntdApp>
      {owner ? (
        <FrameworkConfiguration owner={owner}>{() => null}</FrameworkConfiguration>
      ) : (
        <SettingsPage />
      )}
    </AntdApp>,
  );
}

describe('设置页简化', () => {
  beforeEach(() => {
    vi.resetAllMocks();
    mocks.useAccess.mockReturnValue({
      canManageSettings: true,
      canManagePaymentSettings: true,
      canManageEmailing: true,
      canManageEmailingTest: true,
      canManageNotificationChannels: true,
      canManageTimezone: true,
    });
    mocks.getSettingGroups.mockResolvedValue([]);
    mocks.getEmailSettings.mockResolvedValue(emailSettings);
    mocks.sendTestEmail.mockResolvedValue(undefined);
    mocks.getTimezone.mockResolvedValue('Asia/Shanghai');
    mocks.getTimezones.mockResolvedValue([
      { name: 'Asia/Shanghai', value: 'Asia/Shanghai' },
    ]);
  });

  it('邮件与时区从各自功能加载，兼容页链接到功能内配置', async () => {
    const email = renderPage('notification');
    await waitFor(() => expect(mocks.getEmailSettings).toHaveBeenCalled());
    expect(screen.getByLabelText('默认发件人邮箱')).toBeTruthy();
    expect(screen.queryByRole('button', { name: /重\s*置|Reset/ })).toBeNull();
    email.unmount();
    const timezone = renderPage('timezone');
    await waitFor(() => expect(mocks.getTimezone).toHaveBeenCalled());
    expect(screen.getByRole('button', { name: '保存时区' })).toBeTruthy();
    expect(screen.queryByRole('button', { name: /重\s*置|Reset/ })).toBeNull();
    timezone.unmount();
    renderPage();
    expect(screen.getByRole('link', { name: '默认时区' })).toHaveAttribute(
      'href', '/administration/localization/languages?settings=timezone',
    );
    expect(screen.queryByLabelText('默认发件人邮箱')).toBeNull();
  });

  it('测试邮件默认收起，展开后仍可按原契约提交', async () => {
    renderPage('notification');
    await waitFor(() => expect(mocks.getEmailSettings).toHaveBeenCalled());
    expect(screen.queryByLabelText('收件人')).toBeNull();
    expect(mocks.sendTestEmail).not.toHaveBeenCalled();
    fireEvent.click(screen.getByRole('button', { name: '发送测试邮件' }));
    fireEvent.change(await screen.findByLabelText('发件人'), {
      target: { value: 'sender@example.test' },
    });
    fireEvent.change(screen.getByLabelText('收件人'), {
      target: { value: 'receiver@example.test' },
    });
    fireEvent.change(screen.getByLabelText('主题'), {
      target: { value: '连通测试' },
    });
    fireEvent.change(screen.getByLabelText('正文'), {
      target: { value: '测试正文' },
    });
    const form = screen.getByLabelText('收件人').closest('form');
    expect(form).toBeTruthy();
    if (form) fireEvent.submit(form);
    await waitFor(() =>
      expect(mocks.sendTestEmail).toHaveBeenCalledWith({
        senderEmailAddress: 'sender@example.test',
        targetEmailAddress: 'receiver@example.test',
        subject: '连通测试',
        body: '测试正文',
      }),
    );
  });

  it('只有邮件设置权限时，不暴露发送测试邮件操作', async () => {
    mocks.useAccess.mockReturnValue({
      canManageEmailing: true,
      canManageNotificationChannels: true,
    });
    renderPage('notification');
    await waitFor(() => expect(mocks.getEmailSettings).toHaveBeenCalled());
    expect(screen.queryByRole('button', { name: '发送测试邮件' })).toBeNull();
    expect(screen.queryByLabelText('收件人')).toBeNull();
    expect(mocks.getSettingGroups).not.toHaveBeenCalled();
    expect(mocks.sendTestEmail).not.toHaveBeenCalled();
  });

  it('业务设置移到专用页面，系统设置保留其他配置组', async () => {
    const dedicatedGroups = [
      'Web3',
      'Alipay',
      'WechatPay',
      'Stripe',
      'Ordering',
      'Finance',
    ];
    mocks.getSettingGroups.mockResolvedValue([
      ...dedicatedGroups.map((groupName) => ({
        groupName,
        groupDisplayName: `${groupName} 配置`,
      })),
      { groupName: 'System', groupDisplayName: '系统通用' },
    ]);
    renderPage();
    expect(await screen.findByRole('tab', { name: '系统通用' })).toBeTruthy();
    for (const groupName of dedicatedGroups) {
      expect(
        screen.queryByRole('tab', { name: `${groupName} 配置` }),
      ).toBeNull();
    }
  });

  it('宿主的余额操作超时归入余额支付设置', async () => {
    const name = 'Collection.Balance.DebitCommandTimeoutSeconds';
    mocks.getSettingGroups.mockResolvedValue([
      { groupName: 'Collection', groupDisplayName: '余额操作', settingInfos: [{ name }] },
      { groupName: 'Site', groupDisplayName: '站点设置' },
    ]);
    renderPage();
    await screen.findByRole('tab', { name: '站点设置' });
    expect(screen.queryByRole('tab', { name: '余额操作' })).toBeNull();
  });

  it('租户通用设置保留整组订单和记账字段', async () => {
    mocks.useAccess.mockReturnValue({
      canManageSettings: true,
      canManagePaymentSettings: false,
      canUpdateSettings: true,
    });
    mocks.getSettingGroups.mockResolvedValue([
      {
        groupName: 'Ordering',
        groupDisplayName: '订单配置',
        settingInfos: [
          { name: 'Ordering.Order.ExpiryHours' },
          { name: 'Ordering.Quote.Alipay.FixedFee' },
          { name: 'Ordering.Quote.BalanceUsdc.PercentRate' },
        ],
      },
      {
        groupName: 'Finance',
        groupDisplayName: '记账配置',
        settingInfos: [
          { name: 'Finance.Ledger.Enabled' },
          { name: 'Finance.Wallet.UseLedgerProjection' },
        ],
      },
      { groupName: 'Alipay', groupDisplayName: '支付宝配置' },
      { groupName: 'Collection', groupDisplayName: '余额操作' },
    ]);
    renderPage();
    fireEvent.click(await screen.findByRole('tab', { name: '订单配置' }));
    expect(screen.getByText('Ordering.Order.ExpiryHours')).toBeTruthy();
    expect(screen.getByText('Ordering.Quote.Alipay.FixedFee')).toBeTruthy();
    expect(
      screen.getByText('Ordering.Quote.BalanceUsdc.PercentRate'),
    ).toBeTruthy();
    fireEvent.click(screen.getByRole('tab', { name: '记账配置' }));
    expect(screen.getByText('Finance.Ledger.Enabled')).toBeTruthy();
    expect(screen.getByText('Finance.Wallet.UseLedgerProjection')).toBeTruthy();
    expect(screen.queryByRole('tab', { name: '支付宝配置' })).toBeNull();
    expect(screen.queryByRole('tab', { name: '余额操作' })).toBeNull();
  });
});
