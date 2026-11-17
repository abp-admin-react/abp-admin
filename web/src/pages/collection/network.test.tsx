import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { App } from 'antd';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import NetworkConfigPage from './network';

const api = vi.hoisted(() => ({
  getNetworks: vi.fn(),
  getNetworkStatus: vi.fn(),
  getApiKeys: vi.fn(),
  configureNetwork: vi.fn(),
  configureNetworkToken: vi.fn(),
  configureTransactionRpc: vi.fn(),
  getTransactionRpc: vi.fn(),
  createNetworkApiKey: vi.fn(),
  testNodeCredential: vi.fn(),
  removeApiKey: vi.fn(),
  setApiKeyEnabled: vi.fn(),
  useAccess: vi.fn(),
  getSettingGroups: vi.fn(),
}));
vi.mock('@umijs/max', () => ({ useAccess: api.useAccess }));
vi.mock('@/abp/chainAdmin', () => api);
vi.mock('@/abp/settingUi', () => ({ getSettingGroups: api.getSettingGroups }));
vi.mock('@/requestErrorConfig', () => ({ isNotFound: vi.fn() }));

describe('链网络配置', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    api.useAccess.mockReturnValue({
      canAdminNetwork: true,
      canConfigureCollectionNetwork: true,
    });
    api.getNetworks.mockResolvedValue([
      {
        id: 'network-b',
        name: 'BSC',
        chainId: 56,
        rpcConfigured: true,
        depositFeeRate: 0.05,
        autoCreditMaxAmount: '1000',
      },
    ]);
    api.getNetworkStatus.mockResolvedValue({
      id: 'network-b',
      network: 'BSC',
      chainId: 56,
      depositFeeRate: 0.05,
      autoCreditMaxAmount: '1000',
      estimatedArrivalMinutes: 60,
      nodeLogResultCap: 10000,
      tokens: [],
    });
    api.getApiKeys.mockResolvedValue({ rpcKeys: [], etherscanKeys: [] });
    api.getTransactionRpc.mockResolvedValue({
      rpcUrlMasked: 'https://rpc.example',
    });
    api.configureTransactionRpc.mockResolvedValue({
      rpcUrlMasked: 'https://rpc.new.example',
    });
    api.configureNetwork.mockResolvedValue(undefined);
    api.configureNetworkToken.mockResolvedValue(undefined);
    api.createNetworkApiKey.mockResolvedValue(undefined);
    api.testNodeCredential.mockResolvedValue({
      passed: true,
      message: '可以查询 BSC',
      credits: null,
      balanceUsd: null,
    });
  });

  it('从网络行打开集中配置，保存包含所选网络 ID', async () => {
    render(
      <App>
        <NetworkConfigPage mode="deposit" />
      </App>,
    );
    fireEvent.click(await screen.findByRole('button', { name: '配置充值' }));
    const rate = await screen.findByLabelText('充值费率');
    expect(api.getNetworkStatus).toHaveBeenCalledWith('network-b');
    expect(api.getApiKeys).not.toHaveBeenCalled();
    fireEvent.change(rate, { target: { value: '0.02' } });
    fireEvent.click(screen.getByRole('button', { name: '保存充值规则' }));
    await waitFor(() =>
      expect(api.configureNetwork).toHaveBeenCalledWith(
        expect.objectContaining({
          networkId: 'network-b',
          depositFeeRate: '0.02',
        }),
      ),
    );
    expect(screen.queryByText('生成充值总密钥')).toBeNull();
    expect(screen.queryByText('提现打款钱包')).toBeNull();
  });

  it('在同一充值规则区展示代币最低充值和到账时间，并分别保存', async () => {
    api.getNetworkStatus.mockImplementation(async () => ({
      id: 'network-b',
      network: 'BSC',
      chainId: 56,
      depositFeeRate: 0.05,
      autoCreditMaxAmount: '1000',
      estimatedArrivalMinutes: 60,
      tokens: [
        { id: 'token-u', symbol: 'USDT', minDeposit: '10', isEnabled: true },
      ],
    }));
    render(
      <App>
        <NetworkConfigPage mode="deposit" />
      </App>,
    );
    fireEvent.click(await screen.findByRole('button', { name: '配置充值' }));
    expect(await screen.findByText('BSC 充值规则')).toBeTruthy();
    expect(
      await screen.findByRole('columnheader', { name: '最低充值' }),
    ).toBeTruthy();
    expect(screen.getByText('10')).toBeTruthy();
    const arrival = screen.getByLabelText('预计到账（分钟）');
    expect(arrival).toHaveValue('60');
    expect(
      screen.getByText('仅用于展示，实际到账以区块确认为准。'),
    ).toBeTruthy();
    fireEvent.change(arrival, { target: { value: '45' } });
    fireEvent.click(screen.getByRole('button', { name: '配置 USDT 充值规则' }));
    fireEvent.change(await screen.findByLabelText('最低充值'), {
      target: { value: '12' },
    });
    fireEvent.click(screen.getByRole('switch', { name: '启用代币' }));
    fireEvent.click(screen.getByRole('button', { name: /OK|确\s*定/ }));
    await waitFor(() =>
      expect(api.configureNetworkToken).toHaveBeenCalledWith({
        networkId: 'network-b',
        tokenId: 'token-u',
        minDeposit: '12',
        isEnabled: false,
      }),
    );
    expect(api.configureNetwork).not.toHaveBeenCalled();
    await waitFor(() => expect(arrival).toHaveValue('45'));
    fireEvent.click(screen.getByRole('button', { name: '保存充值规则' }));
    await waitFor(() =>
      expect(api.configureNetwork).toHaveBeenCalledWith(
        expect.objectContaining({
          networkId: 'network-b',
          estimatedArrivalMinutes: 45,
        }),
      ),
    );
  });

  it('加载失败显示重试入口', async () => {
    api.getNetworks.mockRejectedValue(new Error('offline'));
    render(
      <App>
        <NetworkConfigPage />
      </App>,
    );
    expect(await screen.findByText('加载网络列表失败')).toBeTruthy();
    fireEvent.click(screen.getByRole('button', { name: /重\s*试/ }));
    await waitFor(() => expect(api.getNetworks).toHaveBeenCalledTimes(2));
  });

  it('从链网络配置交易 RPC 不加载充值设置或写入充值规则', async () => {
    render(
      <App>
        <NetworkConfigPage />
      </App>,
    );
    fireEvent.click(
      await screen.findByRole('button', { name: '配置交易 RPC' }),
    );
    expect(
      await screen.findByText(/已保存 https:\/\/rpc.example/),
    ).toBeTruthy();
    expect(api.getTransactionRpc).toHaveBeenCalledWith('network-b');
    fireEvent.change(screen.getByLabelText('RPC 地址'), {
      target: { value: ' https://rpc.new.example/node ' },
    });
    fireEvent.click(screen.getByRole('button', { name: '保存交易 RPC' }));
    await waitFor(() =>
      expect(api.configureTransactionRpc).toHaveBeenCalledWith({
        networkId: 'network-b',
        rpcUrl: 'https://rpc.new.example/node',
      }),
    );
    expect(screen.queryByLabelText('充值费率')).toBeNull();
    expect(api.getNetworkStatus).not.toHaveBeenCalled();
    expect(api.configureNetwork).not.toHaveBeenCalled();
  });

  it('网络栏目只显示单链配置，加载列表时不请求详细状态', async () => {
    render(
      <App>
        <NetworkConfigPage />
      </App>,
    );
    expect(await screen.findByText('BSC')).toBeTruthy();
    expect(screen.getByText('已配置')).toBeTruthy();
    expect(screen.queryByRole('columnheader', { name: '最新区块' })).toBeNull();
    expect(screen.queryByRole('columnheader', { name: '最近检查' })).toBeNull();
    expect(api.getSettingGroups).not.toHaveBeenCalled();
    expect(api.getNetworkStatus).not.toHaveBeenCalled();
  });

  it('设置专员没有链网络管理权限，不请求网络详情', () => {
    api.useAccess.mockReturnValue({
      canAdminNetwork: false,
      canConfigureCollectionNetwork: true,
    });
    render(
      <App>
        <NetworkConfigPage />
      </App>,
    );
    expect(screen.getByText('暂无链网络管理权限')).toBeTruthy();
    expect(api.getNetworks).not.toHaveBeenCalled();
    expect(api.getNetworkStatus).not.toHaveBeenCalled();
    expect(api.getApiKeys).not.toHaveBeenCalled();
    expect(api.getSettingGroups).not.toHaveBeenCalled();
    expect(screen.queryByText('自动检测充值')).toBeNull();
    expect(screen.queryByRole('button', { name: '配置交易 RPC' })).toBeNull();
  });
});
