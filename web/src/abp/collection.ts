import { request } from '@umijs/max';

/**
 * 收款模块（AbpAdmin.Collection）的 API 镜像——M1 骨架 + M2 用户侧余额接口。
 * 金额字段一律十进制字符串（最多 18 位小数）。
 * 管理端收款单/余额流水/渠道设置/链上监控的端点随 M3–M5 里程碑落地时在此补齐
 *（计划第 5.7 节）。
 */

/** 收款渠道（与后端 CollectionChannel 对齐；余额只走扣款不是渠道） */
export const COLLECTION_CHANNELS = [
  { value: 'WechatNative', label: '微信 Native' },
  { value: 'AlipayPrecreate', label: '支付宝当面付' },
  { value: 'StripeCheckout', label: 'Stripe Checkout' },
] as const;

/** 币种（与后端 CollectionCurrency 对齐） */
export const COLLECTION_CURRENCIES = [
  { value: 'Cny', label: 'CNY' },
  { value: 'Usd', label: 'USD' },
  { value: 'Usdt', label: 'USDT' },
  { value: 'Usdc', label: 'USDC' },
] as const;

/** 收款单状态（与后端 CollectionStatus 对齐） */
export const COLLECTION_STATUSES = [
  { value: 'Pending', label: '等待中' },
  { value: 'Completed', label: '已完成' },
  { value: 'Closed', label: '已关闭' },
  { value: 'ChannelFailed', label: '渠道失败' },
] as const;

export type CollectionDto = {
  id: string;
  collectionNo: string;
  businessType: string;
  businessId: string;
  userId: string;
  channel: string;
  currency: string;
  amount: string;
  status: string;
  statusNote?: string;
  channelResultUnknown: boolean;
  expiresAt?: string;
  channelPayload?: string;
  channelTradeNo?: string;
  completedAt?: string;
};

// ── M2：用户侧余额接口（CollectionBalanceAppService，计划第 3.1 节） ──────────────────

/** 我的某币余额（金额十进制字符串） */
export type MyBalanceDto = {
  currency: string;
  balance: string;
};

/** 我的余额流水行（amount 带符号：充值/回冲为正，扣款为负） */
export type BalanceTxnDto = {
  id: string;
  action: string;
  currency: string;
  amount: string;
  balanceAfter: string;
  creationTime: string;
  /** 业务类型（TopUp / Order …）；无归因为 null */
  businessType?: string;
  /** 业务单 Id（充值行 = 流水自身 Id；扣款行 = 订单 Id） */
  businessId?: string;
  /** 回冲行的原扣款流水 Id */
  refTxnId?: string;
  /** 充值行的链上转账凭证（ColDepositOrders.TxHash 回填） */
  txHash?: string;
};

/** 余额动作（与后端 BalanceActions 对齐） */
export const BALANCE_ACTION_LABELS: Record<string, string> = {
  DepositCredit: '充值',
  BusinessDebit: '支付扣款',
  DebitReverse: '扣款回冲',
};

/** 我的 USDT / USDC 余额（没有余额行时后端返回 0，不缺项） */
export async function getMyBalances(): Promise<MyBalanceDto[]> {
  const result = await request<{ items: MyBalanceDto[] }>('/api/app/collection-balance/my-balances');
  return result.items ?? [];
}

/** 我的余额流水（分页、倒序） */
export async function getMyTxns(params: {
  skipCount?: number;
  maxResultCount?: number;
  fromTime?: string;
  toTime?: string;
}) {
  return request<PagedResult<BalanceTxnDto>>('/api/app/collection-balance/my-txns', {
    params: {
      SkipCount: params.skipCount ?? 0,
      MaxResultCount: params.maxResultCount ?? 20,
      FromTime: params.fromTime,
      ToTime: params.toTime,
    },
  });
}

type PagedResult<T> = { totalCount: number; items: T[] };
