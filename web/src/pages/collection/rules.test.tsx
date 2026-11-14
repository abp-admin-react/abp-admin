import {
  fireEvent,
  render,
  screen,
  waitFor,
  within,
} from '@testing-library/react';
import { App } from 'antd';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import Web3ConfigPage from './config';

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

function renderConfig(search = '') {
  mocks.useLocation.mockReturnValue({ pathname: '/web3/config', search });
  const page = () => (
    <App>
      <Web3ConfigPage />
    </App>
  );
  const view = render(page());
  return (label: string, key: string) => {
    fireEvent.click(screen.getByRole('tab', { name: label }));
    const route = { pathname: '/web3/config', search: `?view=${key}` };
    expect(mocks.push).toHaveBeenLastCalledWith(route);
    mocks.useLocation.mockReturnValue(route);
    view.rerender(page());
  };
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
    const select = renderConfig();
    expect(screen.getByTitle('Web3 配置')).toBeTruthy();
    expect(screen.getAllByRole('tab').map((tab) => tab.textContent)).toEqual([
      '充值与归集规则',
      '提现规则',
      '链网络',
      '链上钱包',
      '运行设置',
    ]);
    expect(await screen.findByLabelText(scanInterval)).toBeTruthy();
    expect(screen.getByText('充值检测')).toBeTruthy();
    expect(screen.queryByLabelText(alipayLimit)).toBeNull();
    expect(screen.queryByLabelText(walletWatchInterval)).toBeNull();
    expect(
      await screen.findByRole('switch', { name: '自动检测充值' }),
    ).toBeChecked();
    fireEvent.click(screen.getByRole('switch', { name: '自动归集' }));
    await waitFor(() =>
      expect(mocks.setChainSwitches).toHaveBeenCalledWith({
        sweepEnabled: true,
      }),
    );
    select('提现规则', 'withdraw');
    const panel = screen.getByRole('tabpanel');
    expect(await within(panel).findByLabelText(minWithdrawAmount)).toBeTruthy();
    expect(within(panel).getByLabelText(walletWatchInterval)).toBeTruthy();
    expect(within(panel).getByText('费用与限额')).toBeTruthy();
    expect(within(panel).getByText('提现启停与开账')).toBeTruthy();
    expect(within(panel).queryByLabelText(scanInterval)).toBeNull();
    fireEvent.click(
      await within(panel).findByRole('switch', { name: '提现钱包监控' }),
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
    const select = renderConfig('?view=wallets');
    expect(screen.getByRole('tab', { name: '充值与归集规则' })).toHaveAttribute(
      'aria-selected',
      'true',
    );
    expect(screen.queryByRole('tab', { name: '链上钱包' })).toBeNull();
    expect(screen.queryByRole('tab', { name: '链网络' })).toBeNull();
    fireEvent.change(await screen.findByLabelText(scanInterval), {
      target: { value: '15' },
    });
    select('提现规则', 'withdraw');
    const withdrawPanel = within(screen.getByRole('tabpanel'));
    fireEvent.change(await withdrawPanel.findByLabelText(minWithdrawAmount), {
      target: { value: '20' },
    });
    fireEvent.click(withdrawPanel.getByRole('button', { name: /^保\s*存$/ }));
    await waitFor(() =>
      expect(mocks.setSettingValues).toHaveBeenCalledWith({
        [minWithdrawAmount]: '20',
        [walletWatchInterval]: '1',
      }),
    );
    fireEvent.click(
      await withdrawPanel.findByRole('button', { name: '重置为默认值' }),
    );
    fireEvent.click(await screen.findByRole('button', { name: /^重\s*置$/ }));
    await waitFor(() =>
      expect(mocks.resetSettingValues).toHaveBeenCalledWith(
        expect.arrayContaining([minWithdrawAmount, walletWatchInterval]),
      ),
    );
    expect(mocks.resetSettingValues.mock.calls[0][0]).toHaveLength(2);
    select('充值与归集规则', 'deposit');
    expect(
      await within(screen.getByRole('tabpanel')).findByLabelText(scanInterval),
    ).toHaveValue('1');
    expect(mocks.getNetworkStatus).not.toHaveBeenCalled();
    expect(screen.queryByRole('switch')).toBeNull();
    expect(screen.queryByText('提现启停与开账')).toBeNull();
  });

  it('链网络运维可从链接直达提现监控，且不加载通用设置', async () => {
    mocks.useAccess.mockReturnValue({
      canConfigureCollectionNetwork: true,
      canAdminNetwork: true,
    });
    renderConfig('?view=withdraw');
    expect(screen.getByRole('tab', { name: '提现规则' })).toHaveAttribute(
      'aria-selected',
      'true',
    );
    expect(
      await screen.findByRole('switch', { name: '提现钱包监控' }),
    ).toBeTruthy();
    expect(screen.queryByRole('switch', { name: '自动归集' })).toBeNull();
    expect(mocks.getSettingGroups).not.toHaveBeenCalled();
  });

  it('提现专员只看到提现规则与链上钱包，直接打开未授权栏目时落到提现规则', () => {
    mocks.useAccess.mockReturnValue({
      canAdminWithdrawals: true,
      canManageCollectionWallets: true,
    });
    const select = renderConfig('?view=network');
    expect(screen.getAllByRole('tab').map((tab) => tab.textContent)).toEqual([
      '提现规则',
      '链上钱包',
    ]);
    expect(screen.getByText('提现启停与开账')).toBeTruthy();
    expect(screen.queryByText('网络配置内容')).toBeNull();
    select('链上钱包', 'wallets');
    expect(screen.getByText('钱包配置内容')).toBeTruthy();
    expect(screen.queryByText('提现启停与开账')).toBeNull();
    expect(mocks.getSettingGroups).not.toHaveBeenCalled();
    expect(mocks.getNetworkStatus).not.toHaveBeenCalled();
  });

  it('没有宿主规则权限时，不请求任何配置', () => {
    mocks.useAccess.mockReturnValue({ canManageSettings: true });
    renderConfig();
    expect(screen.getByText('当前账号没有可访问的栏目')).toBeTruthy();
    expect(mocks.getSettingGroups).not.toHaveBeenCalled();
    expect(mocks.getNetworkStatus).not.toHaveBeenCalled();
  });
});
