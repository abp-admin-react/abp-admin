import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { App } from 'antd';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { getWeb3ConfigurationSections } from './config';
import { Web3RuleSection } from './rules';

const mocks = vi.hoisted(() => ({
  useAccess: vi.fn(),
  useLocation: vi.fn(),
  push: vi.fn(),
  getNetworkStatus: vi.fn(),
  setChainSwitches: vi.fn(),
  getSettingGroups: vi.fn(),
  setSettingValues: vi.fn(),
  resetSettingValues: vi.fn(),
}));

vi.mock('@umijs/max', () => ({
  useAccess: mocks.useAccess,
  useLocation: mocks.useLocation,
  history: { push: mocks.push },
}));
vi.mock('@/abp/chainAdmin', () => mocks);
vi.mock('./network', () => ({ default: () => <div>网络配置内容</div> }));
vi.mock('./wallets', () => ({ default: () => <div>钱包配置内容</div> }));
vi.mock('./withdrawal-controls', () => ({
  default: () => <div>提现启停与开账</div>,
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

const scanInterval = 'Collection.Chain.ScanIntervalSeconds';
const confirmationSetting = 'Collection.Chain.Confirmations';
const walletWatchInterval = 'Collection.Chain.HotWalletWatchIntervalSeconds';
const minWithdrawAmount = 'Collection.Withdraw.MinAmount';
const alipayLimit = 'Collection.MaxAmount.AlipayPrecreate.Cny';
const operationalSettings = [
  ['Collection.Chain.ScanEnabled', 'Chain'],
  ['Collection.Chain.SweepEnabled', 'Chain'],
  ['Collection.Chain.HotWalletWatchEnabled', 'Chain'],
  ['Collection.Withdraw.Enabled', 'Withdraw'],
  ['Collection.Withdraw.Halt', 'Withdraw'],
];
const setting = (name: string, section: string): API.SettingInfo => ({
  name,
  displayName: name,
  value: '1',
  properties: { Group2: section },
});

function renderRules(section: 'Chain' | 'Withdraw' = 'Chain') {
  const page = (
    next: 'Chain' | 'Withdraw',
    scope: 'deposit' | 'operations' = 'operations',
  ) => (
    <App>
      <Web3RuleSection key={`${next}:${scope}`} section={next} scope={scope} />
    </App>
  );
  const view = render(page(section));
  return (next: 'Chain' | 'Withdraw', scope?: 'deposit' | 'operations') =>
    view.rerender(page(next, scope));
}

describe('Web3 配置', () => {
  beforeEach(() => {
    vi.resetAllMocks();
    mocks.useAccess.mockReturnValue({
      canConfigureCollectionNetwork: true,
      canAdminNetwork: true,
      canAdminWithdrawals: true,
      canManageCollectionWallets: true,
      canManageSettings: true,
      canUpdateSettings: true,
    });
    mocks.getNetworkStatus.mockResolvedValue({
      scanEnabled: true,
      sweepEnabled: false,
      hotWalletWatchEnabled: false,
    });
    mocks.setChainSwitches.mockResolvedValue(undefined);
    mocks.getSettingGroups.mockResolvedValue([
      {
        groupName: 'Web3',
        settingInfos: [
          setting(scanInterval, 'Chain'),
          setting(confirmationSetting, 'Chain'),
          setting(walletWatchInterval, 'Chain'),
          setting(minWithdrawAmount, 'Withdraw'),
          ...operationalSettings.map(([name, section]) =>
            setting(name, section),
          ),
        ],
      },
      {
        groupName: 'Alipay',
        settingInfos: [setting(alipayLimit, 'Chain')],
      },
    ]);
    mocks.setSettingValues.mockResolvedValue(undefined);
    mocks.resetSettingValues.mockResolvedValue(undefined);
  });

  it('充值与提现分别显示对应参数，只有指定 API 写运行开关', async () => {
    const select = renderRules();
    expect(await screen.findByLabelText(scanInterval)).toBeTruthy();
    expect(screen.getByText('充值检测')).toBeTruthy();
    expect(screen.queryByLabelText(alipayLimit)).toBeNull();
    expect(screen.queryByLabelText(walletWatchInterval)).toBeNull();
    expect(screen.queryByLabelText(confirmationSetting)).toBeNull();
    expect(
      await screen.findByRole('switch', { name: '自动检测充值' }),
    ).toBeChecked();
    fireEvent.click(screen.getByRole('switch', { name: '自动归集' }));
    await waitFor(() =>
      expect(mocks.setChainSwitches).toHaveBeenCalledWith({
        sweepEnabled: true,
      }),
    );
    select('Withdraw');
    expect(await screen.findByLabelText(minWithdrawAmount)).toBeTruthy();
    expect(screen.getByLabelText(walletWatchInterval)).toBeTruthy();
    expect(screen.getByText('费用与限额')).toBeTruthy();
    expect(screen.getByText('提现启停与开账')).toBeTruthy();
    expect(screen.queryByLabelText(scanInterval)).toBeNull();
    fireEvent.click(
      await screen.findByRole('switch', { name: '提现钱包监控' }),
    );
    await waitFor(() =>
      expect(mocks.setChainSwitches).toHaveBeenCalledWith({
        hotWalletWatchEnabled: true,
      }),
    );
    expect(mocks.getNetworkStatus).toHaveBeenCalledTimes(2);
    for (const [name] of operationalSettings) {
      expect(screen.queryByLabelText(name)).toBeNull();
    }
    expect(mocks.setSettingValues).not.toHaveBeenCalled();
  });

  it('设置专员只能访问规则，保存与重置只作用于当前栏目', async () => {
    mocks.useAccess.mockReturnValue({
      canConfigureCollectionNetwork: true,
      canAdminNetwork: false,
      canManageSettings: true,
      canUpdateSettings: true,
    });
    const select = renderRules();
    expect(
      getWeb3ConfigurationSections(mocks.useAccess()).map(({ key }) => key),
    ).toEqual(['chain-rules']);
    fireEvent.change(await screen.findByLabelText(scanInterval), {
      target: { value: '15' },
    });
    select('Withdraw');
    fireEvent.change(await screen.findByLabelText(minWithdrawAmount), {
      target: { value: '20' },
    });
    fireEvent.click(screen.getByRole('button', { name: /^保\s*存$/ }));
    await waitFor(() =>
      expect(mocks.setSettingValues).toHaveBeenCalledWith({
        [minWithdrawAmount]: '20',
        [walletWatchInterval]: '1',
      }),
    );
    fireEvent.click(
      await screen.findByRole('button', { name: '重置为默认值' }),
    );
    fireEvent.click(await screen.findByRole('button', { name: /^重\s*置$/ }));
    await waitFor(() =>
      expect(mocks.resetSettingValues).toHaveBeenCalledWith(
        expect.arrayContaining([minWithdrawAmount, walletWatchInterval]),
      ),
    );
    expect(mocks.resetSettingValues.mock.calls[0][0]).toHaveLength(2);
    select('Chain', 'deposit');
    expect(await screen.findByLabelText(confirmationSetting)).toHaveValue('1');
    expect(screen.queryByLabelText(scanInterval)).toBeNull();
    expect(mocks.getNetworkStatus).not.toHaveBeenCalled();
    expect(screen.queryByRole('switch')).toBeNull();
    expect(screen.queryByText('提现启停与开账')).toBeNull();
  });

  it('链网络运维可从链接直达提现监控，且不加载通用设置', async () => {
    mocks.useAccess.mockReturnValue({
      canConfigureCollectionNetwork: true,
      canAdminNetwork: true,
    });
    renderRules('Withdraw');
    expect(
      await screen.findByRole('switch', { name: '提现钱包监控' }),
    ).toBeTruthy();
    expect(screen.queryByRole('switch', { name: '自动归集' })).toBeNull();
    expect(mocks.getSettingGroups).not.toHaveBeenCalled();
  });

  it('提现专员在提现页和钱包区域分别看到各自的配置', () => {
    mocks.useAccess.mockReturnValue({
      canAdminWithdrawals: true,
      canManageCollectionWallets: true,
    });
    renderRules('Withdraw');
    expect(
      getWeb3ConfigurationSections(mocks.useAccess()).map(({ key }) => key),
    ).toEqual(['wallets']);
    expect(screen.getByText('提现启停与开账')).toBeTruthy();
    expect(screen.queryByText('网络配置内容')).toBeNull();
    expect(mocks.getSettingGroups).not.toHaveBeenCalled();
    expect(mocks.getNetworkStatus).not.toHaveBeenCalled();
  });

  it('没有宿主规则权限时，不请求任何配置', () => {
    mocks.useAccess.mockReturnValue({ canManageSettings: true });
    renderRules();
    expect(screen.getByText('暂无规则管理权限')).toBeTruthy();
    expect(mocks.getSettingGroups).not.toHaveBeenCalled();
    expect(mocks.getNetworkStatus).not.toHaveBeenCalled();
  });
});
