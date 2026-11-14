import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { App } from 'antd';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import ChainRuntimeSettings from './runtime-settings';

const api = vi.hoisted(() => ({
  getChainRuntimeSettings: vi.fn(),
  initializeChainCredentialEncryption: vi.fn(),
  updateChainRuntimeSettings: vi.fn(),
  getUsers: vi.fn(),
}));

vi.mock('@umijs/max', () => ({
  useAccess: () => ({ canManageUsers: false }),
  Link: ({ children, to }: { children: string; to: string }) => (
    <a href={to}>{children}</a>
  ),
}));
vi.mock('@/abp/chainAdmin', () => api);
vi.mock('@/abp/identity', () => api);

const runtimeInput = {
  chainRoundBudgetSeconds: 240,
  withdrawRoundBudgetSeconds: 120,
  alertRecipientUserIds: [],
  useLegacyExplorerFallback: true,
};
const settings = { ...runtimeInput, hasEncryptionKey: true };

function showSettings() {
  render(
    <App>
      <ChainRuntimeSettings />
    </App>,
  );
}

describe('Web3 运行设置', () => {
  beforeEach(() => {
    vi.resetAllMocks();
    api.getChainRuntimeSettings.mockResolvedValue(settings);
  });

  it('保存四项设置，空收件人有效，并显示服务端返回的配置', async () => {
    api.updateChainRuntimeSettings.mockResolvedValue({
      ...settings,
      chainRoundBudgetSeconds: 301,
    });
    showSettings();
    const budget = await screen.findByRole('spinbutton', {
      name: /充值检测、归集/,
    });
    expect(budget).toHaveValue('240');
    fireEvent.change(budget, { target: { value: '300' } });
    fireEvent.click(screen.getByRole('switch', { name: /Etherscan/ }));
    fireEvent.click(screen.getByRole('button', { name: '保存运行设置' }));
    await waitFor(() =>
      expect(api.updateChainRuntimeSettings).toHaveBeenCalledWith({
        ...runtimeInput,
        chainRoundBudgetSeconds: 300,
        useLegacyExplorerFallback: false,
      }),
    );
    expect(await screen.findByText('运行设置已保存')).toBeTruthy();
    expect(budget).toHaveValue('301');
    expect(api.getUsers).not.toHaveBeenCalled();
  });

  it('初始化后重新核实加密状态，并保留尚未保存的配置', async () => {
    api.getChainRuntimeSettings.mockResolvedValueOnce({
      ...settings,
      hasEncryptionKey: false,
    });
    api.initializeChainCredentialEncryption.mockResolvedValue(undefined);
    showSettings();
    const budget = await screen.findByRole('spinbutton', { name: /提现广播/ });
    fireEvent.change(budget, { target: { value: '130' } });
    expect(screen.getByText('未配置')).toBeVisible();
    fireEvent.click(screen.getByRole('button', { name: '初始化加密' }));
    expect(await screen.findByText('已配置')).toBeVisible();
    expect(screen.queryByRole('button', { name: '初始化加密' })).toBeNull();
    expect(api.initializeChainCredentialEncryption).toHaveBeenCalledTimes(1);
    expect(api.getChainRuntimeSettings).toHaveBeenCalledTimes(2);
    expect(api.updateChainRuntimeSettings).not.toHaveBeenCalled();
    expect(budget).toHaveValue('130');
  });

  it('初始加载失败可重试，保存失败保留输入', async () => {
    api.getChainRuntimeSettings.mockRejectedValueOnce(new Error('offline'));
    api.updateChainRuntimeSettings.mockRejectedValue(new Error('offline'));
    showSettings();
    expect(await screen.findByText('加载运行设置失败')).toBeTruthy();
    fireEvent.click(screen.getByRole('button', { name: /重\s*试/ }));
    const budget = await screen.findByRole('spinbutton', { name: /提现广播/ });
    fireEvent.change(budget, { target: { value: '130' } });
    fireEvent.click(screen.getByRole('button', { name: '保存运行设置' }));
    expect(await screen.findByText('保存失败，请重试')).toBeTruthy();
    expect(budget).toHaveValue('130');
  });

  it('允许没有用户管理权限的链网管理员录入收件人，并拦截无效 ID', async () => {
    showSettings();
    const recipients = await screen.findByRole('combobox', {
      name: '链上告警收件人',
    });
    fireEvent.change(recipients, { target: { value: 'invalid-id' } });
    fireEvent.keyDown(recipients, { key: 'Enter', code: 'Enter', keyCode: 13 });
    fireEvent.click(screen.getByRole('button', { name: '保存运行设置' }));
    expect(await screen.findByText('请输入有效的用户 ID（UUID）')).toBeTruthy();
    expect(api.getUsers).not.toHaveBeenCalled();
    expect(api.updateChainRuntimeSettings).not.toHaveBeenCalled();
  });
});
