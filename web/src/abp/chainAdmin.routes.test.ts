import { beforeEach, describe, expect, it, vi } from 'vitest';

const request = vi.fn();
vi.mock('@umijs/max', () => ({ request: (...a: unknown[]) => request(...a) }));

import {
  approveLargeDeposit,
  configureNetwork,
  configureNetworkToken,
  createNetworkApiKey,
  getWalletStatus,
  removeApiKey,
  retrySweepTask,
  setApiKeyEnabled,
  setChainSwitches,
  setDepositPolicy,
} from './chainAdmin';

// 链网运维写端点的 URL 形状锁（E2E 实测：单 Guid 参数端点 ?id= 形态 302 假成功，必须路径段）
describe('chainAdmin write endpoint URL shapes', () => {
  beforeEach(() => request.mockReset().mockResolvedValue(undefined));

  it('approveLargeDeposit 使用路径段', async () => {
    await approveLargeDeposit('d1');
    expect(request.mock.calls[0][0]).toBe(
      '/api/app/collection-network-admin/d1/approve-large-deposit',
    );
    expect(request.mock.calls[0][1]).toMatchObject({ method: 'POST' });
  });

  it('retrySweepTask 路径段且段名与 api-definition 一致（/retry-sweep-task；E2E 实测 /retry 是 SPA 兜底 HTML）', async () => {
    await retrySweepTask('t1');
    expect(request.mock.calls[0][0]).toBe(
      '/api/app/collection-network-admin/t1/retry-sweep-task',
    );
    expect(request.mock.calls[0][1]).toMatchObject({ method: 'POST' });
  });

  it('setDepositPolicy 走 query 参数（ABP 原始参数绑定，同 set-min-deposit 形态）', async () => {
    await setDepositPolicy('0.001', '5000');
    expect(request.mock.calls[0][0]).toBe(
      '/api/app/collection-network-admin/set-deposit-policy',
    );
    expect(request.mock.calls[0][1]).toMatchObject({
      method: 'POST',
      params: { feeRate: '0.001', autoCreditMaxAmount: '5000' },
    });
  });

  it('setApiKeyEnabled 使用路径段（E2E 二轮实锤：query 形态假成功）', async () => {
    await setApiKeyEnabled('k1', true);
    expect(request.mock.calls[0][0]).toBe(
      '/api/app/collection-network-admin/k1/set-api-key-enabled',
    );
    expect(request.mock.calls[0][1]).toMatchObject({
      method: 'POST',
      params: { isEnabled: true },
    });
  });

  it('删除 KEY 使用路径段', async () => {
    await removeApiKey('k1');
    expect(request.mock.calls[0][0]).toBe(
      '/api/app/collection-network-admin/k1/api-key',
    );
    expect(request.mock.calls[0][1]).toMatchObject({ method: 'DELETE' });
  });

  it('setChainSwitches 只传提供的参数', async () => {
    await setChainSwitches({ scanEnabled: true });
    expect(request.mock.calls[0][0]).toBe(
      '/api/app/collection-network-admin/set-chain-switches',
    );
    expect(request.mock.calls[0][1]).toMatchObject({
      method: 'POST',
      params: { scanEnabled: true },
    });
  });

  it('集中配置把网络 ID 和敏感字段放在请求体中', async () => {
    await configureNetwork({
      networkId: 'network-2',
      rpcUrl: 'https://rpc.example/key',
      nodeLogResultCap: 1000,
      depositFeeRate: '0.01',
      autoCreditMaxAmount: '50',
    });
    expect(request).toHaveBeenLastCalledWith(
      '/api/app/collection-network-admin/configure-network',
      {
        method: 'POST',
        data: {
          networkId: 'network-2',
          rpcUrl: 'https://rpc.example/key',
          nodeLogResultCap: 1000,
          depositFeeRate: '0.01',
          autoCreditMaxAmount: '50',
        },
      },
    );
    await createNetworkApiKey({
      networkId: 'network-2',
      provider: 'RpcBsc',
      key: 'sensitive',
    });
    expect(request).toHaveBeenLastCalledWith(
      '/api/app/collection-network-admin/add-api-key',
      {
        method: 'POST',
        data: { networkId: 'network-2', provider: 'RpcBsc', key: 'sensitive' },
      },
    );
    await configureNetworkToken({
      networkId: 'network-2',
      tokenId: 'token-2',
      minDeposit: '5',
      isEnabled: true,
    });
    expect(request).toHaveBeenLastCalledWith(
      '/api/app/collection-network-admin/configure-token',
      {
        method: 'POST',
        data: {
          networkId: 'network-2',
          tokenId: 'token-2',
          minDeposit: '5',
          isEnabled: true,
        },
      },
    );
    await getWalletStatus('network-2', true);
    expect(request).toHaveBeenLastCalledWith(
      '/api/app/collection-wallet-admin/network-2',
      { params: { IncludeBalances: true } },
    );
  });
});
