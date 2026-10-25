/**
 * 收款/归集业务 API（collection 域）。
 *
 * - 相对路径 `/api/app/...`；错误码与权限以后端 Contracts 为准。
 * - 页面只依赖本文件导出，勿散写 URL。
 */
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

// 余额 DTO 与动作色板单源在 collectionAdmin.ts（my-txns 会返回全部 7 种动作，
// 管理端镜像已全量对齐后端 BalanceActions；此处复用导出，勿再复制第二份）
import type { BalanceTxnDto, MemberBalanceDto } from './collectionAdmin';

type MyBalanceDto = MemberBalanceDto; // 本文件内的历史名（消费方从本文件导入）
export type { MemberBalanceDto as MyBalanceDto, BalanceTxnDto };
export { BALANCE_ACTION_LABELS } from './collectionAdmin';

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
