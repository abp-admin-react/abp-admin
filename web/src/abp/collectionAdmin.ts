/**
 * 余额域管理端 API 镜像（对应 CollectionBalanceAdminAppService，收款查看权限）：
 * 会员资金（member-*）和资金看板（dashboard）。会员目录搜索沿用 identity.getUsers。
 */
import { request } from '@umijs/max';
import { type GridQuery, postGridQuery } from './gridQuery';

const BASE = '/api/app/collection-balance-admin';
const PAYMENTS_BASE = '/api/app/collection-payment-admin';
const PAYMENT_OPERATIONS = '/api/app/collection-payments';
const REFUNDS_BASE = '/api/app/collection-refunds';

/** 管理端收款记录。金额保留服务端十进制字符串。 */
export interface CollectionPaymentAdminDto {
  id: string;
  collectionNo: string;
  businessType: string;
  businessId: string;
  userId: string;
  channel: string;
  currency: string;
  amount: string;
  status: string;
  statusNote: string | null;
  channelResultUnknown: boolean;
  expiresAt: string | null;
  channelTradeNo: string | null;
  completedAt: string | null;
  creationTime: string;
}

export function getCollectionPayments(params: {
  skipCount: number;
  maxResultCount: number;
  filter?: string;
  status?: string;
  channel?: string;
}): Promise<{ totalCount: number; items: CollectionPaymentAdminDto[] }> {
  return request(PAYMENTS_BASE, {
    params: {
      SkipCount: params.skipCount,
      MaxResultCount: params.maxResultCount,
      Filter: params.filter,
      Status: params.status,
      Channel: params.channel,
    },
  });
}

export function getCollectionPayment(
  id: string,
): Promise<CollectionPaymentAdminDto> {
  return request(`${PAYMENTS_BASE}/${id}`);
}

export interface CollectionRefundDto {
  id: string;
  collectionId: string;
  requestId: string;
  merchantRefundNo: string;
  channelRefundNo: string | null;
  channel: number;
  amount: string;
  currency: number;
  status: number | 'Pending' | 'Succeeded' | 'Failed';
  channelResultUnknown: boolean;
  statusNote: string | null;
  creationTime: string;
  succeededAt: string | null;
}

export interface RequestCollectionRefundInput {
  collectionId: string;
  requestId: string;
  amount: string;
  currency: number;
}

export function queryCollectionPayment(
  id: string,
): Promise<CollectionPaymentAdminDto> {
  return request(`${PAYMENT_OPERATIONS}/${id}/query`, { method: 'POST' });
}

export function getCollectionRefunds(
  collectionId: string,
): Promise<CollectionRefundDto[]> {
  return request(`${PAYMENT_OPERATIONS}/${collectionId}/refunds`);
}

export function requestCollectionRefund(
  input: RequestCollectionRefundInput,
): Promise<CollectionRefundDto> {
  return request(REFUNDS_BASE, {
    method: 'POST',
    data: input,
    skipErrorHandler: true,
  });
}

export function queryCollectionRefund(
  id: string,
): Promise<CollectionRefundDto> {
  return request(`${REFUNDS_BASE}/${id}/query`, { method: 'POST' });
}

/** 会员余额行（与用户侧 MyBalanceDto 同形） */
export type MemberBalanceDto = {
  currency: string;
  balance: string;
  frozen: string;
  total: string;
};

/** 已有余额行的用户（一人一行）。没有资金往来的用户不在列表中；缺的币种为 0。 */
export type MemberBalanceAccountDto = {
  userId: string;
  usdtBalance: string;
  usdtFrozen: string;
  usdcBalance: string;
  usdcFrozen: string;
  updatedAt: string;
};

/** 身份字段仅在操作者可查看会员目录时返回，邮箱沿用身份模块的脱敏权限。 */
export type MemberBalanceGridRowDto = MemberBalanceAccountDto & {
  userName: string | null;
  email: string | null;
};

export function queryMemberBalanceAccounts(
  input: GridQuery,
  signal: AbortSignal,
): Promise<{ totalCount: number; items: MemberBalanceGridRowDto[] }> {
  return postGridQuery<MemberBalanceGridRowDto>(
    `${BASE}/member-balance-accounts/query`,
    input,
    signal,
  );
}

export async function getMemberBalanceAccounts(params: {
  skipCount?: number;
  maxResultCount?: number;
}): Promise<{ totalCount: number; items: MemberBalanceAccountDto[] }> {
  return request(`${BASE}/member-balance-accounts`, {
    params: {
      SkipCount: params.skipCount,
      MaxResultCount: params.maxResultCount,
    },
  });
}

/** 会员流水行（与用户侧 BalanceTxnDto 同形，充值行带链上凭证） */
export type BalanceTxnDto = {
  id: string;
  action: string;
  currency: string;
  amount: string;
  balanceAfter: string;
  creationTime: string;
  businessType?: string;
  businessId?: string;
  refTxnId?: string;
  txHash?: string;
  depositOrderId: string | null;
};

// ABP 把 userId 绑定为路径参数。
export async function getMemberBalances(
  userId: string,
): Promise<{ items: MemberBalanceDto[] }> {
  return request(`${BASE}/member-balances/${userId}`);
}

/** 会员流水：userId 路径段 + 分页 query */
export async function getMemberTxns(params: {
  userId: string;
  skipCount?: number;
  maxResultCount?: number;
  txnId?: string;
  withdrawalId?: string;
}): Promise<{ totalCount: number; items: BalanceTxnDto[] }> {
  return request(`${BASE}/member-txns/${params.userId}`, {
    params: {
      SkipCount: params.skipCount,
      MaxResultCount: params.maxResultCount,
      ...(params.txnId ? { TxnId: params.txnId } : {}),
      ...(params.withdrawalId ? { WithdrawalId: params.withdrawalId } : {}),
    },
  });
}

/** 余额动作（与后端 BalanceActions 对齐）：色板与文案是页面唯一口径 */
export const BALANCE_ACTION_LABELS: Record<
  string,
  { color: string; text: string }
> = {
  DepositCredit: { color: 'success', text: '充值入账' },
  BusinessDebit: { color: 'warning', text: '业务扣款' },
  DebitReverse: { color: 'processing', text: '扣款回冲' },
  ManualAdjust: { color: 'blue', text: '人工调账' },
  WithdrawFreeze: { color: 'orange', text: '提现冻结' },
  WithdrawUnfreeze: { color: 'cyan', text: '提现解冻' },
  WithdrawSettle: { color: 'purple', text: '提现出账' },
  WithdrawRefund: { color: 'default', text: '提现退款' },
};

/** 状态桶（计数 + 金额合计） */
export interface StatusBucketDto {
  status: string;
  currency?: string;
  count: number;
  total: string;
}

export interface UserBalanceBucketDto {
  currency: string;
  balance: string;
  frozen: string;
}

export interface VaultCheckLatestDto {
  symbol: string;
  vaultBalance: string;
  userTotal: string;
  /** VaultBalance - UserTotal；负 = 金库 < 用户余额（缺口） */
  difference: string;
  checkedAt: string;
}

/** 资金看板（纯 DB 聚合；热钱包余额/熔断在热钱包监控页） */
export interface TreasuryDashboardDto {
  generatedAt: string;
  depositByStatus: StatusBucketDto[];
  withdrawByStatus: StatusBucketDto[];
  heldDepositCount: number;
  heldDepositTotal: string;
  depositTodayCount: number;
  depositTodayAmount: string;
  withdrawTodayCount: number;
  withdrawTodayAmount: string;
  /** 充值手续费累计（Swept） */
  depositFeeTotal: string;
  /** 一种币时的提现手续费。多种币时为 0，看 withdrawFees */
  withdrawFeeTotal: string;
  withdrawFees?: { currency: string; amount: string }[];
  /** 审核后未清偿净额，含广播失败。多种币时为 0，看 withdrawInFlight */
  withdrawInFlightAmount: string;
  withdrawInFlight?: { currency: string; amount: string }[];
  userBalances: UserBalanceBucketDto[];
  vaultChecks: VaultCheckLatestDto[];
}

/** 资金看板聚合（收款查看权限位；今日 = UTC 日界，与当日笔数限额同口径） */
export async function getTreasuryDashboard(): Promise<TreasuryDashboardDto> {
  return request(`${BASE}/dashboard`);
}
