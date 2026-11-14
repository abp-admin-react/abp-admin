import { request } from '@umijs/max';

/**
 * 链网管理 API 镜像（对应 CollectionNetworkAdminAppService）。
 * 网络配置要求 Network.Admin 与 Host 身份，钱包读取也允许 Withdraw.Admin。
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
  id: string;
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
  /** 热钱包逐笔监控开关。 */
  hotWalletWatchEnabled: boolean;
  /** 充值手续费率（网络级 [0,1]；建单快照，改只影响新单） */
  depositFeeRate: number;
  estimatedArrivalMinutes: number | null;
  /** 超过此毛额的充值须人工放行，0 表示全部人工放行。 */
  autoCreditMaxAmount: string;
  /** 大额门持有中进款单数（Confirmed 且未挂归集任务，等放行敞口） */
  heldDepositCount: number;
  /** 持有中进款毛额合计（十进制字符串） */
  heldDepositTotal: string;
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
  /** 充值手续费（建单快照；历史单 undefined） */
  fee?: string;
  /** 入账净额（建单快照；历史单 undefined = 回退毛额） */
  actualAmount?: string;
  rawAmount: string;
  symbol: string;
  /** 网络名（ColNetwork.Name，如 BSC） */
  networkName: string;
  blockNumber: number;
  status: string;
  sweepTaskId?: string;
  manuallyReleased: boolean;
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

export interface AnkrSettingsDto {
  platformAuthKeyMasked: string | null;
  group: string | null;
  scanIntervalSeconds: number;
}

export interface AnkrBalanceDto {
  status: 'notConfigured' | 'available' | 'unavailable';
  credits: string | null;
  balanceUsd: string | null;
  checkedAt: string | null;
}

export interface TestNodeCredentialDto {
  /** 服务商检查结果。HTTP 200 也可能返回验证未通过。 */
  passed: boolean;
  message: string;
  /** 仅 Ankr Platform 验证成功时返回该次查询的余额。 */
  credits: string | null;
  balanceUsd: string | null;
}

const BASE = '/api/app/collection-network-admin';

export interface CollectionNetworkSummaryDto {
  id: string;
  name: string;
  chainId: number;
  rpcConfigured: boolean;
  hasMnemonic: boolean;
  latestChainHead: number | null;
  scanHeartbeatAt: string | null;
  depositFeeRate: number;
  autoCreditMaxAmount: string;
}

export interface CollectionWalletTokenDto {
  symbol: string;
  vaultAddress: string | null;
  vaultBalance: string | null;
  withdrawBalance: string | null;
}

export interface CollectionWalletStatusDto {
  networkId: string;
  networkName: string;
  hasMnemonic: boolean;
  fuelAddress: string | null;
  fuelBnb: string | null;
  withdrawAddress: string | null;
  hasWithdrawKey: boolean;
  isWithdrawalNetwork: boolean;
  withdrawalNetworkName: string | null;
  withdrawBnb: string | null;
  balancesAvailable: boolean;
  tokens: CollectionWalletTokenDto[];
}

export interface ConfigureNetworkInput {
  networkId: string;
  rpcUrl?: string | null;
  nodeLogResultCap: number | null;
  depositFeeRate: string;
  autoCreditMaxAmount: string;
  estimatedArrivalMinutes?: number | null;
}

export interface ChainRuntimeSettingsDto {
  chainRoundBudgetSeconds: number;
  withdrawRoundBudgetSeconds: number;
  alertRecipientUserIds: string[];
  useLegacyExplorerFallback: boolean;
  hasEncryptionKey: boolean;
}

export type ChainRuntimeSettingsInput = Omit<
  ChainRuntimeSettingsDto,
  'hasEncryptionKey'
>;

export function getChainRuntimeSettings(): Promise<ChainRuntimeSettingsDto> {
  return request(`${BASE}/runtime-settings`);
}

export function updateChainRuntimeSettings(
  data: ChainRuntimeSettingsInput,
): Promise<ChainRuntimeSettingsDto> {
  return request(`${BASE}/runtime-settings`, { method: 'PUT', data });
}

export function initializeChainCredentialEncryption(): Promise<void> {
  return request(`${BASE}/initialize-encryption`, { method: 'POST' });
}

export function getNetworks(): Promise<CollectionNetworkSummaryDto[]> {
  return request(`${BASE}/networks`);
}

export function getWalletNetworks(): Promise<CollectionNetworkSummaryDto[]> {
  return request('/api/app/collection-wallet-admin/networks');
}

export function getWalletStatus(
  networkId: string,
  includeBalances = false,
): Promise<CollectionWalletStatusDto> {
  return request(`/api/app/collection-wallet-admin/${networkId}`, {
    params: { IncludeBalances: includeBalances },
  });
}

export function configureNetwork(input: ConfigureNetworkInput): Promise<void> {
  return request(`${BASE}/configure-network`, { method: 'POST', data: input });
}

export function configureNetworkToken(input: {
  networkId: string;
  tokenId: string;
  minDeposit: string;
  isEnabled: boolean;
}): Promise<void> {
  return request(`${BASE}/configure-token`, { method: 'POST', data: input });
}

export function createNetworkApiKey(input: {
  networkId: string;
  provider: string;
  key: string;
  label?: string;
}): Promise<ChainApiKeyDto> {
  return request(`${BASE}/add-api-key`, { method: 'POST', data: input });
}

export async function getNetworkStatus(
  networkId?: string,
): Promise<NetworkStatusDto> {
  return request(`${BASE}/status`, { params: { NetworkId: networkId } });
}

export function getAnkrSettings(networkId: string): Promise<AnkrSettingsDto> {
  return request(`${BASE}/ankr-settings`, { params: { NetworkId: networkId } });
}

export function configureAnkr(input: {
  networkId: string;
  platformAuthKey?: string;
  clearPlatformAuthKey?: boolean;
  group?: string | null;
  scanIntervalSeconds?: number;
}): Promise<void> {
  return request(`${BASE}/configure-ankr`, { method: 'POST', data: input });
}

export function getAnkrBalance(networkId?: string): Promise<AnkrBalanceDto> {
  return request(`${BASE}/ankr-balance`, {
    params: { NetworkId: networkId },
    skipErrorHandler: true,
  });
}

export function testNodeCredential(input: {
  networkId: string;
  provider: 'RpcBsc' | 'AnkrPlatform' | 'EtherscanV2';
  key?: string | null;
  keyId?: string | null;
  group?: string | null;
}): Promise<TestNodeCredentialDto> {
  return request(`${BASE}/test-node-credential`, {
    method: 'POST',
    data: input,
    skipErrorHandler: true,
  });
}

export async function getDepositOrders(params: {
  skipCount?: number;
  maxResultCount?: number;
  status?: string;
  symbol?: string;
  depositId?: string;
}): Promise<{ totalCount: number; items: DepositOrderListDto[] }> {
  return request(`${BASE}/deposit-orders`, {
    params: {
      SkipCount: params.skipCount,
      MaxResultCount: params.maxResultCount,
      Status: params.status,
      Symbol: params.symbol,
      DepositId: params.depositId,
    },
  });
}

export async function getSweepTasks(params: {
  skipCount?: number;
  maxResultCount?: number;
  status?: string;
}): Promise<{ totalCount: number; items: SweepTaskListDto[] }> {
  return request(`${BASE}/sweep-tasks`, { params });
}

/** 保存人工放行资格并加入归集队列。已有任务时重复请求成功。 */
export async function approveLargeDeposit(id: string): Promise<void> {
  await request(`${BASE}/${id}/approve-large-deposit`, { method: 'POST' });
}

/** 手动将失败任务重新入队，包括需要人工处置的任务。 */
export async function retrySweepTask(id: string): Promise<void> {
  await request(`${BASE}/${id}/retry-sweep-task`, { method: 'POST' });
}

/** 设置充值政策：费率 [0,1]（建单快照，改只影响新单）+ 大额放行门（≥0，0 = 全部等放行）。 */
export async function setDepositPolicy(
  feeRate: string,
  autoCreditMaxAmount: string,
): Promise<void> {
  await request(`${BASE}/set-deposit-policy`, {
    method: 'POST',
    params: { feeRate, autoCreditMaxAmount },
  });
}

export async function getVaultChecks(params: {
  skipCount?: number;
  maxResultCount?: number;
}): Promise<{ totalCount: number; items: VaultCheckListDto[] }> {
  return request(`${BASE}/vault-checks`, { params });
}

/** 仅写入显式传入的开关，后台任务每轮读取。 */
export async function setChainSwitches(params: {
  scanEnabled?: boolean;
  sweepEnabled?: boolean;
  hotWalletWatchEnabled?: boolean;
}): Promise<void> {
  await request(`${BASE}/set-chain-switches`, { method: 'POST', params });
}

export async function setNodeLogResultCap(cap: number): Promise<void> {
  await request(`${BASE}/set-node-log-result-cap`, {
    method: 'POST',
    params: { cap },
  });
}

export async function setVaultAddress(
  symbol: string,
  vaultAddress: string,
  networkId?: string,
): Promise<void> {
  await request(`${BASE}/set-vault-address`, {
    method: 'POST',
    params: { symbol, vaultAddress, NetworkId: networkId },
  });
}

export async function setMinDeposit(
  symbol: string,
  minDeposit: string,
): Promise<void> {
  await request(`${BASE}/set-min-deposit`, {
    method: 'POST',
    params: { symbol, minDeposit },
  });
}

export async function setTokenEnabled(
  symbol: string,
  isEnabled: boolean,
): Promise<void> {
  await request(`${BASE}/set-token-enabled`, {
    method: 'POST',
    params: { symbol, isEnabled },
  });
}

export async function generateMnemonic(networkId?: string): Promise<string> {
  return request(`${BASE}/generate-mnemonic`, {
    method: 'POST',
    params: { NetworkId: networkId },
  });
}

/** 扫链 KEY 池全量（掩码回显 + 回退来源可见度） */
export async function getApiKeys(networkId?: string): Promise<ChainApiKeysDto> {
  return request(`${BASE}/api-keys`, { params: { NetworkId: networkId } });
}

export async function removeApiKey(
  id: string,
  networkId?: string,
): Promise<void> {
  await request(`${BASE}/${id}/api-key`, {
    method: 'DELETE',
    params: { NetworkId: networkId },
  });
}

export async function setApiKeyEnabled(
  id: string,
  isEnabled: boolean,
  networkId?: string,
): Promise<void> {
  // 单 Guid 参数端点为路径段（?id= 形态不命中路由→SPA 兜底 200 HTML→开关“假成功”——
  // api-definition 实测：POST /{id}/set-api-key-enabled；E2E 二轮实锤）
  await request(`${BASE}/${id}/set-api-key-enabled`, {
    method: 'POST',
    params: { isEnabled, NetworkId: networkId },
  });
}

/** 状态标签色 */
export const DEPOSIT_STATUS_LABELS: Record<
  string,
  { color: string; text: string }
> = {
  PendingConfirm: { color: 'processing', text: '待确认' },
  Confirmed: { color: 'cyan', text: '已确认/待归集' },
  SkippedBelowMinDeposit: { color: 'default', text: '低于最低额' },
  PrecisionOverflow: { color: 'warning', text: '超精度' },
  Sweeping: { color: 'orange', text: '归集中' },
  Swept: { color: 'success', text: '已入账' },
};

export function depositStatusLabel(row: DepositOrderListDto) {
  if (row.status === 'Confirmed' && !row.sweepTaskId && !row.manuallyReleased) {
    return { color: 'purple', text: '大额待放行' };
  }
  return (
    DEPOSIT_STATUS_LABELS[row.status] ?? { color: 'default', text: row.status }
  );
}

export const SWEEP_STATUS_LABELS: Record<
  string,
  { color: string; text: string }
> = {
  Queued: { color: 'default', text: '排队中' },
  Fueling: { color: 'orange', text: '打油中' },
  Sweeping: { color: 'processing', text: '归集中' },
  Swept: { color: 'success', text: '已完成' },
  Failed: { color: 'error', text: '失败' },
};
