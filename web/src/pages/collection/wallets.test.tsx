import {
  act,
  fireEvent,
  render,
  screen,
  waitFor,
} from '@testing-library/react';
import { App } from 'antd';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import CollectionWalletsPage from './wallets';

const api = vi.hoisted(() => ({
  getWalletNetworks: vi.fn(),
  getWalletStatus: vi.fn(),
  setVaultAddress: vi.fn(),
  generateMnemonic: vi.fn(),
  setWithdrawVault: vi.fn(),
  useAccess: vi.fn(),
}));
vi.mock('@umijs/max', () => ({ useAccess: api.useAccess }));
vi.mock('@/abp/chainAdmin', () => api);
vi.mock('@/abp/withdrawal', () => ({ setWithdrawVault: api.setWithdrawVault }));

describe('收款钱包', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    api.useAccess.mockReturnValue({
      canAdminNetwork: true,
      canAdminWithdrawals: true,
      canManageCollectionWallets: true,
    });
    api.getWalletNetworks.mockResolvedValue([
      { id: 'selected-network', name: 'BSC' },
    ]);
    api.getWalletStatus.mockResolvedValue({
      networkId: 'selected-network',
      networkName: 'BSC',
      hasMnemonic: true,
      hasWithdrawKey: false,
      isWithdrawalNetwork: true,
      withdrawalNetworkName: 'BSC',
      fuelAddress: '0xfuel',
      withdrawAddress: null,
      tokens: [
        {
          symbol: 'USDT',
          vaultAddress: '0xvault',
          vaultBalance: null,
          withdrawBalance: null,
        },
      ],
    });
    api.setWithdrawVault.mockResolvedValue(undefined);
    api.setVaultAddress.mockResolvedValue(undefined);
  });

  it('钱包总览与配置按网络加载，提现私钥通过带网络 ID 的接口提交', async () => {
    render(
      <App>
        <CollectionWalletsPage />
      </App>,
    );
    await screen.findByText('归集加油钱包');
    expect(api.getWalletStatus).toHaveBeenCalledWith('selected-network', false);
    const titles = screen.getAllByText('提现打款钱包');
    fireEvent.click(titles[titles.length - 1]);
    fireEvent.change(await screen.findByLabelText('打款地址'), {
      target: { value: `0x${'1'.repeat(40)}` },
    });
    fireEvent.change(screen.getByLabelText('私钥'), {
      target: { value: 'private-key' },
    });
    fireEvent.click(screen.getByRole('button', { name: '保存提现钱包' }));
    await waitFor(() =>
      expect(api.setWithdrawVault).toHaveBeenCalledWith(
        `0x${'1'.repeat(40)}`,
        'private-key',
        'selected-network',
      ),
    );
  });

  it('仅提现权限时保留提现配置，隐藏网络密钥和平台地址编辑', async () => {
    api.useAccess.mockReturnValue({
      canAdminNetwork: false,
      canAdminWithdrawals: true,
      canManageCollectionWallets: true,
    });
    render(
      <App>
        <CollectionWalletsPage />
      </App>,
    );
    await screen.findByText('归集加油钱包');
    expect(screen.queryByText('充值总密钥')).toBeNull();
    expect(screen.queryByText('平台收款地址')).toBeNull();
    const titles = screen.getAllByText('提现打款钱包');
    fireEvent.click(titles[titles.length - 1]);
    expect(await screen.findByLabelText('私钥')).toBeTruthy();
  });

  it('保存平台地址后保留尚未提交的提现私钥', async () => {
    render(
      <App>
        <CollectionWalletsPage />
      </App>,
    );
    await screen.findByText('归集加油钱包');
    const withdrawTitles = screen.getAllByText('提现打款钱包');
    fireEvent.click(withdrawTitles[withdrawTitles.length - 1]);
    fireEvent.change(await screen.findByLabelText('私钥'), {
      target: { value: 'pending-private-key' },
    });
    fireEvent.click(screen.getByText('平台收款地址', { exact: true }));
    fireEvent.change(await screen.findByLabelText('收款地址'), {
      target: { value: `0x${'2'.repeat(40)}` },
    });
    fireEvent.click(screen.getByRole('button', { name: '保存收款地址' }));
    await waitFor(() => expect(api.getWalletStatus).toHaveBeenCalledTimes(2));
    expect(screen.getByLabelText('私钥')).toHaveValue('pending-private-key');
    expect(api.setWithdrawVault).not.toHaveBeenCalled();
  });

  it('非提现运行网络不显示无效的钱包保存入口', async () => {
    const current = await api.getWalletStatus();
    api.getWalletStatus.mockResolvedValue({
      ...current,
      isWithdrawalNetwork: false,
      withdrawalNetworkName: 'BSC',
    });
    render(
      <App>
        <CollectionWalletsPage />
      </App>,
    );
    await screen.findByText('当前提现使用 BSC 网络');
    expect(screen.queryByRole('button', { name: '保存提现钱包' })).toBeNull();
  });

  it('默认只显示钱包配置，余额按需加载', async () => {
    const current = await api.getWalletStatus();
    api.getWalletStatus.mockClear();
    api.getWalletStatus.mockImplementation(
      async (_networkId, includeBalances) =>
        includeBalances
          ? { ...current, balancesAvailable: true, fuelBnb: '12' }
          : current,
    );
    render(
      <App>
        <CollectionWalletsPage />
      </App>,
    );
    await screen.findByText('归集加油钱包');

    expect(api.getWalletStatus.mock.calls).toEqual([
      ['selected-network', false],
    ]);
    expect(screen.queryByText(/^BNB /)).toBeNull();
    fireEvent.click(screen.getByRole('button', { name: '查询钱包余额' }));
    expect(await screen.findByText('BNB 12')).toBeTruthy();
    expect(api.getWalletStatus).toHaveBeenCalledWith('selected-network', true);
  });

  it('没有钱包管理权限时不查询链网络或钱包地址', () => {
    api.useAccess.mockReturnValue({
      canAdminNetwork: true,
      canManageCollectionWallets: false,
    });
    render(
      <App>
        <CollectionWalletsPage />
      </App>,
    );
    expect(screen.getByText('暂无链上钱包管理权限')).toBeTruthy();
    expect(api.getWalletNetworks).not.toHaveBeenCalled();
    expect(api.getWalletStatus).not.toHaveBeenCalled();
  });

  it('切换栏目时关闭钱包助记词弹窗', async () => {
    api.generateMnemonic.mockResolvedValue('test-only phrase');
    const page = ({ active }: { active: boolean }) => (
      <App>{active && <CollectionWalletsPage />}</App>
    );
    const view = render(page({ active: true }));
    fireEvent.click(await screen.findByText('充值总密钥', { exact: true }));
    fireEvent.click(screen.getByRole('button', { name: '生成充值总密钥' }));
    fireEvent.click(await screen.findByRole('button', { name: /^生\s*成$/ }));
    expect(
      await screen.findAllByDisplayValue('test-only phrase'),
    ).not.toHaveLength(0);

    view.rerender(page({ active: false }));
    await waitFor(() =>
      expect(screen.queryAllByDisplayValue('test-only phrase')).toHaveLength(0),
    );
  });

  it('迟到的余额响应不会覆盖刚保存的钱包配置', async () => {
    const original = await api.getWalletStatus();
    let resolveBalance!: (value: typeof original) => void;
    const pendingBalance = new Promise((resolve) => {
      resolveBalance = resolve;
    });
    const updatedAddress = `0x${'2'.repeat(40)}`;
    api.getWalletStatus.mockImplementation((_id, includeBalances) =>
      includeBalances ? pendingBalance : Promise.resolve(original),
    );
    render(
      <App>
        <CollectionWalletsPage />
      </App>,
    );
    await screen.findByText('归集加油钱包');
    fireEvent.click(screen.getByRole('button', { name: '查询钱包余额' }));
    fireEvent.click(screen.getByText('平台收款地址', { exact: true }));
    fireEvent.change(await screen.findByLabelText('收款地址'), {
      target: { value: updatedAddress },
    });
    api.getWalletStatus.mockImplementation((_id, includeBalances) =>
      includeBalances
        ? pendingBalance
        : Promise.resolve({
            ...original,
            tokens: [{ ...original.tokens[0], vaultAddress: updatedAddress }],
          }),
    );
    fireEvent.click(screen.getByRole('button', { name: '保存收款地址' }));
    await waitFor(() =>
      expect(screen.getByLabelText('收款地址')).toHaveValue(updatedAddress),
    );
    await waitFor(() => expect(screen.getByText(updatedAddress)).toBeTruthy());
    await act(async () =>
      resolveBalance({ ...original, balancesAvailable: true, fuelBnb: '999' }),
    );
    expect(screen.getByText(updatedAddress)).toBeTruthy();
    expect(screen.queryByText('BNB 999')).toBeNull();
  });

  it('其他管理员换地址后，余额和展示地址保持对应并保留编辑草稿', async () => {
    const original = await api.getWalletStatus();
    render(
      <App>
        <CollectionWalletsPage />
      </App>,
    );
    await screen.findByText('归集加油钱包');
    fireEvent.click(screen.getByText('平台收款地址', { exact: true }));
    const draft = `0x${'8'.repeat(40)}`;
    fireEvent.change(await screen.findByLabelText('收款地址'), {
      target: { value: draft },
    });
    const changedAddress = `0x${'9'.repeat(40)}`;
    api.getWalletStatus.mockResolvedValue({
      ...original,
      balancesAvailable: true,
      tokens: [
        {
          ...original.tokens[0],
          vaultAddress: changedAddress,
          vaultBalance: '123',
        },
      ],
    });
    fireEvent.click(screen.getByRole('button', { name: '查询钱包余额' }));
    expect(await screen.findByText(changedAddress)).toBeTruthy();
    expect(screen.getByText('余额 123')).toBeTruthy();
    expect(screen.getByLabelText('收款地址')).toHaveValue(draft);
  });
});
