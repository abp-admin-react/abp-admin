import {
  fireEvent,
  render,
  screen,
  waitFor,
  within,
} from '@testing-library/react';
import { App } from 'antd';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import NetworkConfigPage from './network';

const api = vi.hoisted(() => ({
  getNetworks: vi.fn(),
  getNetworkStatus: vi.fn(),
  getApiKeys: vi.fn(),
  configureNetwork: vi.fn(),
  configureNetworkToken: vi.fn(),
  configureAnkr: vi.fn(),
  createNetworkApiKey: vi.fn(),
  testNodeCredential: vi.fn(),
  getAnkrSettings: vi.fn(),
  getAnkrBalance: vi.fn(),
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
    api.getAnkrSettings.mockResolvedValue({
      platformAuthKeyMasked: '****1234',
      group: 'my-ankr-group',
      scanIntervalSeconds: 20,
    });
    api.getAnkrBalance.mockResolvedValue({ status: 'notConfigured' });
    api.configureAnkr.mockResolvedValue(undefined);
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
        <NetworkConfigPage />
      </App>,
    );
    fireEvent.click(await screen.findByRole('button', { name: '配置' }));
    const rate = await screen.findByLabelText('充值费率');
    expect(api.getNetworkStatus).toHaveBeenCalledWith('network-b');
    expect(api.getApiKeys).toHaveBeenCalledWith('network-b');
    fireEvent.change(rate, { target: { value: '0.02' } });
    fireEvent.click(screen.getByRole('button', { name: '保存网络配置' }));
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
        <NetworkConfigPage />
      </App>,
    );
    fireEvent.click(await screen.findByRole('button', { name: '配置' }));
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
    fireEvent.click(screen.getByRole('button', { name: '保存网络配置' }));
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

  it('保存节点密钥后保留尚未提交的充值规则', async () => {
    render(
      <App>
        <NetworkConfigPage />
      </App>,
    );
    fireEvent.click(await screen.findByRole('button', { name: '配置' }));
    fireEvent.change(await screen.findByLabelText('充值费率'), {
      target: { value: '0.17' },
    });
    fireEvent.click(screen.getByRole('tab', { name: '节点密钥' }));
    fireEvent.change(await screen.findByLabelText('新项目密钥'), {
      target: { value: 'unsaved-key' },
    });
    fireEvent.click(screen.getByRole('button', { name: '添加密钥' }));
    await waitFor(() => expect(api.getNetworkStatus).toHaveBeenCalledTimes(2));
    expect(api.createNetworkApiKey).toHaveBeenCalledWith(
      expect.objectContaining({
        networkId: 'network-b',
        provider: 'RpcBsc',
        key: 'unsaved-key',
      }),
    );
    fireEvent.click(screen.getByRole('tab', { name: '节点与充值规则' }));
    expect(await screen.findByLabelText('充值费率')).toHaveValue('0.17');
    expect(api.configureNetwork).not.toHaveBeenCalled();
  });

  it('在 RPC 保存全局充值检测间隔，在账户余额独立保存分组', async () => {
    render(
      <App>
        <NetworkConfigPage />
      </App>,
    );
    fireEvent.click(await screen.findByRole('button', { name: '配置' }));
    fireEvent.click(await screen.findByRole('tab', { name: '节点密钥' }));
    const interval = await screen.findByRole('spinbutton', {
      name: '全局充值检测间隔（秒）',
    });
    expect(interval).toHaveValue('20');
    fireEvent.change(interval, { target: { value: '120' } });
    fireEvent.click(await screen.findByRole('tab', { name: 'Ankr 账户余额' }));
    expect(
      screen.queryByRole('spinbutton', { name: '全局充值检测间隔（秒）' }),
    ).toBeNull();
    expect(await screen.findByText(/已保存 \*\*\*\*1234/)).toBeTruthy();
    expect(screen.getByLabelText('账户查询密钥')).toHaveValue('');
    fireEvent.change(screen.getByLabelText('Ankr 账户分组'), {
      target: { value: '' },
    });
    fireEvent.click(screen.getByRole('button', { name: '保存 Ankr 配置' }));
    await waitFor(() =>
      expect(api.configureAnkr).toHaveBeenNthCalledWith(1, {
        networkId: 'network-b',
        group: null,
      }),
    );
    fireEvent.click(screen.getByRole('tab', { name: 'Ankr RPC' }));
    expect(interval).toHaveValue('120');
    fireEvent.click(screen.getByRole('button', { name: '保存检测间隔' }));
    await waitFor(() =>
      expect(api.configureAnkr).toHaveBeenNthCalledWith(2, {
        networkId: 'network-b',
        scanIntervalSeconds: 120,
      }),
    );
    expect(api.createNetworkApiKey).not.toHaveBeenCalled();
  });

  it('清除平台密钥时，仅发送清除标记', async () => {
    render(
      <App>
        <NetworkConfigPage />
      </App>,
    );
    fireEvent.click(await screen.findByRole('button', { name: '配置' }));
    fireEvent.click(await screen.findByRole('tab', { name: '节点密钥' }));
    fireEvent.click(await screen.findByRole('tab', { name: 'Ankr 账户余额' }));
    fireEvent.click(
      await screen.findByRole('button', { name: '清除账户查询密钥' }),
    );
    fireEvent.click(await screen.findByRole('button', { name: '确认清除' }));
    await waitFor(() =>
      expect(api.configureAnkr).toHaveBeenCalledWith({
        networkId: 'network-b',
        clearPlatformAuthKey: true,
      }),
    );
  });

  it('按已保存的账户分组显示额度，刷新时不查询未保存的分组', async () => {
    api.getAnkrBalance
      .mockResolvedValueOnce({
        status: 'available',
        credits: '900719925474099300',
        balanceUsd: '0.00',
        checkedAt: '2026-11-14T04:00:00Z',
      })
      .mockResolvedValueOnce({ status: 'unavailable' });
    render(
      <App>
        <NetworkConfigPage />
      </App>,
    );
    fireEvent.click(await screen.findByRole('button', { name: '配置' }));
    fireEvent.click(await screen.findByRole('tab', { name: '节点密钥' }));
    expect(api.getAnkrBalance).not.toHaveBeenCalled();
    fireEvent.click(screen.getByRole('tab', { name: 'Ankr 账户余额' }));
    const card = await screen.findByRole('region', {
      name: '已保存账户的余额',
    });
    expect(await within(card).findByText('剩余 Credits')).toBeVisible();
    expect(card).toHaveTextContent('团队账户 · my-ankr-group');
    expect(card).toHaveTextContent('900,719,925,474,099,300');
    expect(card).toHaveTextContent('$0.00');
    fireEvent.change(screen.getByLabelText('Ankr 账户分组'), {
      target: { value: 'draft-group' },
    });
    expect(card).not.toHaveTextContent('draft-group');
    fireEvent.click(within(card).getByRole('button', { name: '刷新余额' }));
    expect(
      await within(card).findByText('暂时无法查询 Ankr 账户余额'),
    ).toBeVisible();
    expect(card).not.toHaveTextContent('$0.00');
    expect(api.getAnkrBalance.mock.calls).toEqual([
      ['network-b'],
      ['network-b'],
    ]);
    expect(api.configureAnkr).not.toHaveBeenCalled();
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

  it('三个服务可分别测试草稿，切换服务保留输入，测试不保存配置', async () => {
    api.testNodeCredential.mockResolvedValueOnce({
      passed: true,
      message: 'BSC 主网连接正常',
    });
    api.testNodeCredential.mockResolvedValueOnce({
      passed: true,
      message: '团队余额查询正常',
      credits: '13579',
      balanceUsd: '1.25',
    });
    api.testNodeCredential.mockResolvedValueOnce({
      passed: false,
      message: '套餐不支持 BSC',
    });
    render(
      <App>
        <NetworkConfigPage />
      </App>,
    );
    fireEvent.click(await screen.findByRole('button', { name: '配置' }));
    fireEvent.click(await screen.findByRole('tab', { name: '节点密钥' }));
    const connections = () => screen.getByRole('button', { name: '测试连接' });

    fireEvent.click(connections());
    expect(await screen.findByText('请输入密钥')).toBeTruthy();
    expect(api.testNodeCredential).not.toHaveBeenCalled();
    fireEvent.change(screen.getByLabelText('新项目密钥'), {
      target: { value: ' project-key ' },
    });
    fireEvent.click(connections());
    expect(await screen.findByText('测试通过：BSC 主网连接正常')).toBeTruthy();
    expect(api.testNodeCredential).toHaveBeenNthCalledWith(1, {
      networkId: 'network-b',
      provider: 'RpcBsc',
      key: 'project-key',
    });

    fireEvent.click(screen.getByRole('tab', { name: 'Ankr 账户余额' }));
    await screen.findByText(/已保存 \*\*\*\*1234/);
    fireEvent.change(screen.getByLabelText('账户查询密钥'), {
      target: { value: 'platform-key' },
    });
    fireEvent.change(screen.getByLabelText('Ankr 账户分组'), {
      target: { value: 'other-group' },
    });
    fireEvent.click(connections());
    expect(await screen.findByText('测试通过：团队余额查询正常')).toBeTruthy();
    const testBalance = screen.getByRole('region', { name: '本次测试余额' });
    expect(testBalance).toHaveTextContent('13,579');
    expect(testBalance).toHaveTextContent('$1.25');
    expect(
      screen.getByRole('region', { name: '已保存账户的余额' }),
    ).not.toHaveTextContent('13,579');
    expect(api.testNodeCredential).toHaveBeenNthCalledWith(2, {
      networkId: 'network-b',
      provider: 'AnkrPlatform',
      key: 'platform-key',
      group: 'other-group',
    });
    fireEvent.change(screen.getByLabelText('Ankr 账户分组'), {
      target: { value: '' },
    });
    expect(
      screen.queryByRole('region', { name: '本次测试余额' }),
    ).not.toBeInTheDocument();

    fireEvent.click(screen.getByRole('tab', { name: 'Etherscan 补查' }));
    fireEvent.change(screen.getByLabelText('新 Etherscan 密钥'), {
      target: { value: 'explorer-key' },
    });
    fireEvent.click(connections());
    expect(await screen.findByText('测试失败：套餐不支持 BSC')).toBeTruthy();
    expect(api.testNodeCredential).toHaveBeenNthCalledWith(3, {
      networkId: 'network-b',
      provider: 'EtherscanV2',
      key: 'explorer-key',
    });
    fireEvent.click(screen.getByRole('tab', { name: 'Ankr RPC' }));
    expect(screen.getByLabelText('新项目密钥')).toHaveValue(' project-key ');
    expect(api.createNetworkApiKey).not.toHaveBeenCalled();
    expect(api.configureAnkr).not.toHaveBeenCalled();
  });

  it('可测试已保存的密钥，不需要把密钥发回页面', async () => {
    api.getApiKeys.mockResolvedValue({
      rpcKeys: [{ id: 'key-row', maskedKey: '****9876', isEnabled: true }],
      etherscanKeys: [],
    });
    render(
      <App>
        <NetworkConfigPage />
      </App>,
    );
    fireEvent.click(await screen.findByRole('button', { name: '配置' }));
    fireEvent.click(await screen.findByRole('tab', { name: '节点密钥' }));
    const row = screen.getByRole('row', { name: /9876/ });
    fireEvent.click(
      within(row).getByRole('button', { name: '测试已保存密钥 ****9876' }),
    );
    expect(
      await screen.findByText('****9876 · 测试通过：可以查询 BSC'),
    ).toBeTruthy();
    expect(api.testNodeCredential).toHaveBeenNthCalledWith(1, {
      networkId: 'network-b',
      provider: 'RpcBsc',
      keyId: 'key-row',
    });

    fireEvent.click(screen.getByRole('tab', { name: 'Ankr 账户余额' }));
    await screen.findByText(/已保存 \*\*\*\*1234/);
    fireEvent.change(screen.getByLabelText('Ankr 账户分组'), {
      target: { value: '' },
    });
    fireEvent.click(screen.getByRole('button', { name: '测试连接' }));
    await waitFor(() =>
      expect(api.testNodeCredential).toHaveBeenNthCalledWith(2, {
        networkId: 'network-b',
        provider: 'AnkrPlatform',
        group: null,
      }),
    );
    expect(api.configureAnkr).not.toHaveBeenCalled();
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
    expect(screen.queryByRole('button', { name: '配置' })).toBeNull();
  });
});
