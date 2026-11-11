import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { App as AntdApp } from 'antd';
import { beforeEach, describe, expect, it, vi } from 'vitest';
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

vi.mock('@umijs/max', () => ({ useAccess: mocks.useAccess }));
vi.mock('@/abp/settings', () => mocks);
vi.mock('@/abp/settingUi', () => ({
  getSettingGroups: mocks.getSettingGroups,
}));
vi.mock('./SettingGroupPanel', () => ({ default: () => null }));

const emailSettings = {
  defaultFromDisplayName: '管理员',
  defaultFromAddress: 'admin@example.test',
  smtpHost: 'smtp.example.test',
  smtpPort: 587,
  smtpEnableSsl: true,
  smtpUseDefaultCredentials: false,
};

function renderPage() {
  return render(
    <AntdApp>
      <SettingsPage />
    </AntdApp>,
  );
}

describe('设置页简化', () => {
  beforeEach(() => {
    vi.resetAllMocks();
    mocks.useAccess.mockReturnValue({
      canManageSettings: true,
      canManageEmailing: true,
      canManageEmailingTest: true,
      canManageLanguages: true,
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

  it('语言统一留在语言管理，邮件和时区保留且不显示无效重置操作', async () => {
    renderPage();
    await waitFor(() => expect(mocks.getEmailSettings).toHaveBeenCalled());
    expect(screen.queryByRole('tab', { name: '语言' })).toBeNull();
    expect(screen.getByRole('tab', { name: '邮件' })).toBeTruthy();
    expect(screen.queryByRole('button', { name: /重\s*置|Reset/ })).toBeNull();
    fireEvent.click(screen.getByRole('tab', { name: '时区' }));
    await waitFor(() => expect(mocks.getTimezone).toHaveBeenCalled());
    expect(screen.getByRole('button', { name: '保存时区' })).toBeTruthy();
    expect(screen.queryByRole('button', { name: /重\s*置|Reset/ })).toBeNull();
  });

  it('测试邮件默认收起，展开后仍可按原契约提交', async () => {
    renderPage();
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
    mocks.useAccess.mockReturnValue({ canManageEmailing: true });
    renderPage();
    await waitFor(() => expect(mocks.getEmailSettings).toHaveBeenCalled());
    expect(screen.queryByRole('button', { name: '发送测试邮件' })).toBeNull();
    expect(screen.queryByLabelText('收件人')).toBeNull();
    expect(mocks.getSettingGroups).not.toHaveBeenCalled();
    expect(mocks.sendTestEmail).not.toHaveBeenCalled();
  });
});
