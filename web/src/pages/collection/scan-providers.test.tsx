vi.mock('ag-grid-react', () => import('../../../tests/gridTestDouble'));

import {
  fireEvent,
  render,
  screen,
  waitFor,
  within,
} from '@testing-library/react';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import type { ScanProviderDto } from '@/abp/scanProviders';
import ScanProvidersPage from './scan-providers';

const api = vi.hoisted(() => ({
  getNetworks: vi.fn(),
  getScanProviders: vi.fn(),
  testScanProvider: vi.fn(),
  testScanProviderBalance: vi.fn(),
  getScanProviderBalance: vi.fn(),
  createScanProvider: vi.fn(),
  updateScanProvider: vi.fn(),
  reorderScanProviders: vi.fn(),
  deleteScanProvider: vi.fn(),
  claimLegacyScanProviderBalance: vi.fn(),
}));

vi.mock('@/abp/chainAdmin', () => ({ getNetworks: api.getNetworks }));
vi.mock('@/abp/scanProviders', () => api);

const bsc = { networkId: 'bsc', logResultCap: null };
const ankr: ScanProviderDto = {
  id: 'ankr-project',
  provider: 'ankr',
  label: '主用 Ankr',
  isEnabled: true,
  sortOrder: 0,
  keyMasked: '****1111',
  platformKeyMasked: '****2222',
  group: 'team',
  networks: [bsc],
};
const etherscan: ScanProviderDto = {
  id: 'etherscan-project',
  provider: 'etherscan',
  label: '备用 Etherscan',
  isEnabled: true,
  sortOrder: 1,
  keyMasked: '****3333',
  platformKeyMasked: null,
  group: null,
  networks: [bsc],
};
const availableBalance = {
  status: 'available',
  credits: '200',
  balanceUsd: '2.00',
  checkedAt: null,
};

describe('第三方扫链配置', () => {
  beforeEach(() => {
    vi.resetAllMocks();
    api.getNetworks.mockResolvedValue([
      { id: 'bsc', name: 'BSC', chainId: 56 },
    ]);
    api.getScanProviders.mockResolvedValue({
      providers: [ankr, etherscan],
      legacyBalancePending: false,
      legacyRpcActive: false,
    });
  });

  it('按行增减优先级并在保存后保持同一个按钮可获得焦点', async () => {
    api.getScanProviders
      .mockResolvedValueOnce({
        providers: [ankr, etherscan],
        legacyBalancePending: false,
        legacyRpcActive: false,
      })
      .mockResolvedValueOnce({
        providers: [
          { ...ankr, sortOrder: 1 },
          { ...etherscan, sortOrder: 0 },
        ],
        legacyBalancePending: false,
        legacyRpcActive: false,
      });
    render(<ScanProvidersPage />);
    const moveUp = await screen.findByRole('button', {
      name: '上移 备用 Etherscan',
    });
    moveUp.focus();
    fireEvent.click(moveUp);
    expect(
      await screen.findByText('备用 Etherscan 已移到第 1 位'),
    ).toBeTruthy();
    expect(api.reorderScanProviders).toHaveBeenCalledWith([
      etherscan.id,
      ankr.id,
    ]);
    expect(screen.getAllByRole('row')[1]).toHaveTextContent('备用 Etherscan');
    expect(moveUp).toHaveFocus();
    expect(moveUp).toHaveAttribute('aria-disabled', 'true');
    fireEvent.click(moveUp);
    expect(api.reorderScanProviders).toHaveBeenCalledTimes(1);
  });

  it('在同一 Ankr 编辑器测试扫链和草稿余额，提交前不会保存', async () => {
    api.testScanProvider.mockResolvedValue({
      passed: true,
      message: '已读到 BSC 日志',
    });
    api.getScanProviderBalance.mockResolvedValue(availableBalance);
    api.testScanProviderBalance.mockResolvedValue({
      ...availableBalance,
      credits: '10',
    });
    render(<ScanProvidersPage />);
    fireEvent.click(
      await screen.findByRole('button', { name: '编辑 主用 Ankr' }),
    );
    const dialog = within(
      await screen.findByRole('dialog', { name: '编辑 Ankr' }),
    );
    expect(dialog.getByLabelText('Ankr RPC 项目密钥')).toHaveValue('');
    expect(dialog.getByLabelText('Ankr Platform Auth Key')).toHaveValue('');
    fireEvent.click(dialog.getByRole('button', { name: '测试扫链' }));
    expect(await dialog.findByText(/已读到 BSC 日志/)).toBeTruthy();
    expect(api.testScanProvider).toHaveBeenCalledWith({
      id: ankr.id,
      provider: 'ankr',
      apiKey: null,
      networkId: 'bsc',
    });
    const queryBalance = dialog.getByRole('button', { name: '查询账户余额' });
    fireEvent.click(queryBalance);
    expect(await dialog.findByText('200')).toBeTruthy();
    expect(api.getScanProviderBalance).toHaveBeenCalledWith(ankr.id);
    fireEvent.change(dialog.getByLabelText('Ankr 账户分组（选填）'), {
      target: { value: 'new-team' },
    });
    fireEvent.click(queryBalance);
    expect(await dialog.findByText('10')).toBeTruthy();
    expect(api.testScanProviderBalance).toHaveBeenCalledWith({
      id: ankr.id,
      platformAuthKey: null,
      group: 'new-team',
    });
    expect(api.updateScanProvider).not.toHaveBeenCalled();
    fireEvent.click(dialog.getByRole('button', { name: '保存服务商' }));
    await waitFor(() =>
      expect(api.updateScanProvider).toHaveBeenCalledWith(ankr.id, {
        provider: 'ankr',
        label: ankr.label,
        isEnabled: true,
        apiKey: null,
        platformAuthKey: null,
        clearPlatformAuthKey: false,
        group: 'new-team',
        networks: [bsc],
      }),
    );
  });

  it('可以从下拉添加 Etherscan，表单不包含 Ankr 账户字段', async () => {
    api.getScanProviders.mockResolvedValue({
      providers: [],
      legacyBalancePending: false,
      legacyRpcActive: false,
    });
    render(<ScanProvidersPage />);
    const add = await screen.findByRole('button', { name: '添加服务商' });
    await waitFor(() => expect(add).toBeEnabled());
    fireEvent.click(add);
    const dialog = within(
      await screen.findByRole('dialog', { name: '添加扫链服务商' }),
    );
    fireEvent.mouseDown(dialog.getByRole('combobox', { name: '服务商' }));
    fireEvent.click(await screen.findByTitle('Etherscan'));
    expect(dialog.queryByLabelText('Ankr Platform Auth Key')).toBeNull();
    fireEvent.change(dialog.getByLabelText('API 密钥'), {
      target: { value: 'example-credential' },
    });
    fireEvent.click(dialog.getByRole('button', { name: '保存服务商' }));
    await waitFor(() =>
      expect(api.createScanProvider).toHaveBeenCalledWith({
        provider: 'etherscan',
        label: null,
        isEnabled: true,
        apiKey: 'example-credential',
        platformAuthKey: null,
        clearPlatformAuthKey: false,
        group: null,
        networks: [bsc],
      }),
    );
  });
});
