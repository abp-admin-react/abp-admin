import { request } from '@umijs/max';

/**
 * 链网管理 API 镜像（对应 CollectionNetworkAdminAppService）。
 * 全部需要 AbpAdmin.Collection.Network.Admin 权限 + Host 身份。
 */

export interface TokenStatusDto {
  id: string;
  symbol: string;
  contractAddress: string;
  minDeposit: string;
  vaultAddress?: string;
  lastScannedBlock?: number;
  lastScanAt?: string;
  isEnabled: boolean;
  blocksBehind?: number;
}

export interface NetworkStatusDto {
  network: string;
  chainId: number;
  hasMnemonic: boolean;
  rpcUrlMasked?: string;
  nodeLogResultCap?: number;
  latestChainHead?: number;
  scanHeartbeatAt?: string;
  sweepHeartbeatAt?: string;
  scanBehindAlertedAt?: string;
  sweepStuckAlertedAt?: string;
  gasLowAlertedAt?: string;
  scanEnabled: boolean;
  sweepEnabled: boolean;
  tokens: TokenStatusDto[];
}

export interface DepositOrderListDto {
  id: string;
  txHash: string;
  logIndex: number;
  userId: string;
  toAddress: string;
  fromAddress: string;
  amount?: string;
  rawAmount: string;
  symbol: string;
  /** 网络名（ColNetwork.Name，如 BSC） */
  networkName: string;
  blockNumber: number;
  status: string;
  sweepTaskId?: string;
  creationTime: string;
}

export interface SweepTaskListDto {
  id: string;
  symbol: string;
  userId: string;
  address: string;
  addressIndex: number;
  amount: string;
  status: string;
  retryable: boolean;
  failReason?: string;
  txHash?: string;
  nonceUsed?: number;
  gasTxHash?: string;
  statusChangedAt: string;
  creationTime: string;
}

export interface VaultCheckListDto {
  id: string;
  symbol: string;
  vaultBalance: string;
  userTotal: string;
  difference: string;
  checkedAt: string;
}

export interface ChainApiKeyDto {
  id: string;
  /** RpcBsc / EtherscanV2 */
  provider: string;
  label?: string;
  /** 掩码（**** + 尾 4 位），明文不出库 */
  maskedKey: string;
  isEnabled: boolean;
  lastUsedAt?: string;
  consecutiveFailures: number;
  creationTime: string;
}

export interface ChainApiKeysDto {
  rpcKeys: ChainApiKeyDto[];
  etherscanKeys: ChainApiKeyDto[];
  /** RPC 池为空时实际使用的回退来源（网络行 RpcCipher 掩码） */
  rpcFallbackMasked?: string;
  /** Etherscan 池为空时是否配置了宿主回退端点 */
  etherscanFallbackPresent: boolean;
}

const BASE = '/api/app/collection-network-admin';

export async function getNetworkStatus(): Promise<NetworkStatusDto> {
  return request(`${BASE}/status`);
}

export async function getDepositOrders(params: {
  skipCount?: number;
  maxResultCount?: number;
  status?: string;
  symbol?: string;
}): Promise<{ totalCount: number; items: DepositOrderListDto[] }> {
  return request(`${BASE}/deposit-orders`, { params });
}

export async function getSweepTasks(params: {
  skipCount?: number;
  maxResultCount?: number;
  status?: string;
}): Promise<{ totalCount: number; items: SweepTaskListDto[] }> {
  return request(`${BASE}/sweep-tasks`, { params });
}

export async function getVaultChecks(params: {
  skipCount?: number;
  maxResultCount?: number;
}): Promise<{ totalCount: number; items: VaultCheckListDto[] }> {
  return request(`${BASE}/vault-checks`, { params });
}

export async function setRpcUrl(rpcUrl: string): Promise<void> {
  await request(`${BASE}/set-rpc-url`, { method: 'POST', params: { rpcUrl } });
}

export async function setNodeLogResultCap(cap: number): Promise<void> {
  await request(`${BASE}/set-node-log-result-cap`, { method: 'POST', params: { cap } });
}

export async function setVaultAddress(symbol: string, vaultAddress: string): Promise<void> {
  await request(`${BASE}/set-vault-address`, { method: 'POST', params: { symbol, vaultAddress } });
}

export async function setMinDeposit(symbol: string, minDeposit: string): Promise<void> {
  await request(`${BASE}/set-min-deposit`, { method: 'POST', params: { symbol, minDeposit } });
}

export async function setTokenEnabled(symbol: string, isEnabled: boolean): Promise<void> {
  await request(`${BASE}/set-token-enabled`, { method: 'POST', params: { symbol, isEnabled } });
}

export async function generateMnemonic(): Promise<string> {
  return request(`${BASE}/generate-mnemonic`, { method: 'POST' });
}

/** 扫链 KEY 池全量（掩码回显 + 回退来源可见度） */
export async function getApiKeys(): Promise<ChainApiKeysDto> {
  return request(`${BASE}/api-keys`);
}

/** 添加一把扫链 KEY（明文只传不存；provider: RpcBsc / EtherscanV2）。ABP 约定路由：Add 前缀动词省略 → POST /api-key */
export async function addApiKey(provider: string, key: string, label?: string): Promise<ChainApiKeyDto> {
  return request(`${BASE}/api-key`, { method: 'POST', params: { provider, key, label } });
}

export async function removeApiKey(id: string): Promise<void> {
  await request(`${BASE}/${id}/api-key`, { method: 'DELETE' });
}

export async function setApiKeyEnabled(id: string, isEnabled: boolean): Promise<void> {
  await request(`${BASE}/set-api-key-enabled`, { method: 'POST', params: { id, isEnabled } });
}

/** 状态标签色 */
export const DEPOSIT_STATUS_LABELS: Record<string, { color: string; text: string }> = {
  PendingConfirm: { color: 'processing', text: '待确认' },
  Confirmed: { color: 'cyan', text: '已确认/待归集' },
  SkippedBelowMinDeposit: { color: 'default', text: '低于最低额' },
  PrecisionOverflow: { color: 'warning', text: '超精度' },
  Sweeping: { color: 'orange', text: '归集中' },
  Swept: { color: 'success', text: '已入账' },
};

export const SWEEP_STATUS_LABELS: Record<string, { color: string; text: string }> = {
  Queued: { color: 'default', text: '排队中' },
  Fueling: { color: 'orange', text: '打油中' },
  Sweeping: { color: 'processing', text: '归集中' },
  Swept: { color: 'success', text: '已完成' },
  Failed: { color: 'error', text: '失败' },
};
