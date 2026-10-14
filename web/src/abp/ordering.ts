/**
 * 订单域 API 封装。
 *
 * - 相对路径与分页口径与其它 abp/* 一致（SkipCount/MaxResultCount）。
 * - 支付结果展示以后端状态机为准，前端不做乐观「已支付」终态。
 */
import { request } from '@umijs/max';

/**
 * 订单模块（AbpAdmin.Ordering）的 API 镜像。
 * 端点由模块 ConventionalControllers 自动暴露（/api/app/order、/api/app/order-admin），
 * 生成器不可信（见 docs/framework-contracts.md §3），按 src/abp 手写镜像约定书写。
 * 金额字段一律十进制字符串（最多 18 位小数）——业务契约，不解析成 JS number。
 */

export type OrderDto = {
  id: string;
  orderNo: string;
  userId: string;
  /** TopUp | Business */
  orderType: string;
  subject: string;
  /** 业务单标价（USD，两位小数字符串）；充值订单为 null */
  amount?: string;
  currency?: string;
  /** PendingPayment | Paid | Closed | Fulfilled */
  status: string;
  /** WechatNative | AlipayPrecreate | StripeCheckout | BalanceUsdt | BalanceUsdc */
  payChannel?: string;
  payAmount?: string;
  payCurrency?: string;
  paidAt?: string;
  /** 致付收款的渠道单号（微信/支付宝交易号、Stripe 单号）；余额支付为 null */
  channelTradeNo?: string;
  /** 支付网络（Web3 渠道如 "BSC"；法币渠道为 null） */
  payNetwork?: string;
  /** 履约时间 */
  fulfilledAt?: string;
  /** 实际关闭时间（用户取消 / 过期关闭；ExpiresAt 只是付款截止） */
  closedAt?: string;
  /** 人工退款累计（标价币种字符串）；无退款为 null */
  refundedAmount?: string;
  quoteId?: string;
  collectionId?: string;
  balanceTxnId?: string;
  expiresAt?: string;
  /** UserCancel | Expired */
  closeReason?: string;
  paymentAnomaly: boolean;
  creationTime: string;
  anomalyLines?: OrderAnomalyLineDto[];
};

export type OrderAnomalyLineDto = {
  id: string;
  amount?: string;
  currency?: string;
  collectionId?: string;
  channelTradeNo?: string;
  note: string;
  creationTime: string;
};

export type CreateOrderInput = {
  subject: string;
  /** USD 标价，两位小数字符串 */
  amount: string;
};

export type OrderListFilterDto = {
  orderType?: string;
  status?: string;
  hasAnomaly?: boolean;
  userId?: string;
};

export type QuoteDto = {
  id: string;
  orderId: string;
  payMethod: string;
  payCurrency: string;
  payAmount: string;
  usdCnyRate: string;
  status: string;
  expiresAt: string;
};

export type StartPayResultDto = {
  /** ChannelCreated | BalancePaid | AlreadyCompleted | Closed */
  outcome: string;
  order: OrderDto;
  /** 渠道凭据：微信 code_url、支付宝 qr_code、Stripe Checkout 跳转地址 */
  channelPayload?: string;
  collectionId?: string;
};

export type PagedResult<T> = {
  items: T[];
  totalCount: number;
};

/** 渠道大类：法币 / Web3 链上 / 账户余额（账本划扣，非外部渠道） */
export type PayChannelCategory = 'Fiat' | 'Web3' | 'Balance';

/** 渠道元数据（与后端 OrderingPayChannel 对齐）——全站渠道显示的唯一出处 */
export const PAY_CHANNEL_META: Record<
  string,
  { category: PayChannelCategory; label: string; color: string }
> = {
  WechatNative: { category: 'Fiat', label: '微信 Native', color: 'green' },
  AlipayPrecreate: { category: 'Fiat', label: '支付宝当面付', color: 'blue' },
  StripeCheckout: { category: 'Fiat', label: 'Stripe Checkout', color: 'purple' },
  BalanceUsdt: { category: 'Balance', label: '余额 USDT', color: 'default' },
  BalanceUsdc: { category: 'Balance', label: '余额 USDC', color: 'default' },
};

/** 当前唯一支付网络（与后端 ColNetwork 种子一致）；多链上线后此兜底移除、以 payNetwork 为准 */
const DEFAULT_NETWORK = 'BSC';

export const currencyLabel = (c?: string) =>
  c === 'Usdt' ? 'USDT' : c === 'Usdc' ? 'USDC' : c === 'Cny' ? 'CNY' : c === 'Usd' ? 'USD' : c ?? '';

/**
 * 订单支付方式的组合显示标签：
 * - 法币渠道：渠道名（+币种，如 微信 Native·CNY）
 * - Web3/余额：网络·币种（如 BSC·USDT / 余额·USDT）
 * 网络读 order.payNetwork，缺失按单链兜底 BSC（余额单网络含义是「余额来源链」）。
 */
export function payChannelLabel(order: Pick<OrderDto, 'payChannel' | 'payCurrency' | 'payNetwork'>): string | null {
  if (!order.payChannel) return null;
  const meta = PAY_CHANNEL_META[order.payChannel];
  if (!meta) return order.payChannel;
  if (meta.category === 'Balance') {
    return `余额·${currencyLabel(order.payCurrency)}`;
  }
  if (meta.category === 'Web3') {
    return `${order.payNetwork ?? DEFAULT_NETWORK}·${currencyLabel(order.payCurrency)}`;
  }
  return order.payCurrency ? `${meta.label}·${currencyLabel(order.payCurrency)}` : meta.label;
}

/** 支付方式三组（发起支付下拉用）；label 一律回指 PAY_CHANNEL_META（单一出处），
 * 法币渠道 M3/M4 实装前不可真实支付（开发态 Fake 网关，下拉里标注）。 */
export const PAY_METHOD_GROUPS: { groupLabel: string; methods: { value: string; label: string; note?: string }[] }[] = [
  {
    groupLabel: '余额支付（账本划扣）',
    methods: [
      { value: 'BalanceUsdt', label: PAY_CHANNEL_META.BalanceUsdt.label },
      { value: 'BalanceUsdc', label: PAY_CHANNEL_META.BalanceUsdc.label },
    ],
  },
  {
    groupLabel: '法币渠道',
    methods: [
      { value: 'WechatNative', label: PAY_CHANNEL_META.WechatNative.label, note: 'M3' },
      { value: 'AlipayPrecreate', label: PAY_CHANNEL_META.AlipayPrecreate.label, note: 'M3' },
      { value: 'StripeCheckout', label: 'Stripe Checkout (USD)', note: 'M4' },
    ],
  },
];

/** 创建业务单 */
export async function createOrder(data: CreateOrderInput) {
  return request<OrderDto>('/api/app/order', { method: 'POST', data });
}

/** 我的订单列表（服务端分页：SkipCount/MaxResultCount；页大小后端钳到 100） */
export async function getMyOrders(
  params: OrderListFilterDto & { skipCount?: number; maxResultCount?: number },
) {
  return request<PagedResult<OrderDto>>('/api/app/order', {
    method: 'GET',
    params: {
      OrderType: params.orderType,
      Status: params.status,
      HasAnomaly: params.hasAnomaly,
      UserId: params.userId,
      SkipCount: params.skipCount ?? 0,
      MaxResultCount: params.maxResultCount ?? 10,
    },
  });
}

/** 订单详情（含异常明细） */
export async function getOrder(id: string) {
  return request<OrderDto>(`/api/app/order/${id}`, { method: 'GET' });
}

/** 获取（必要时创建）某支付方式的有效报价（orderId 走路径段——ABP 自动路由如此绑定，swagger 钉住） */
export async function getOrderQuote(orderId: string, payMethod: string) {
  return request<QuoteDto>(`/api/app/order/quote/${orderId}`, {
    method: 'GET',
    params: { payMethod },
  });
}

/** 发起支付（换渠道/余额；后端先取消旧收款，返回已完成则不建新单不扣款） */
export async function startPay(orderId: string, payMethod: string) {
  return request<StartPayResultDto>(`/api/app/order/start-pay/${orderId}`, {
    method: 'POST',
    params: { payMethod },
  });
}

/** 用户取消（CloseReason = UserCancel，只对待支付订单） */
export async function cancelOrder(id: string) {
  return request<OrderDto>(`/api/app/order/${id}/cancel`, { method: 'POST' });
}

/** 余额流水 → 订单 的关联（流水页跳转用） */
export type OrderRefDto = {
  balanceTxnId: string;
  orderId: string;
  orderNo: string;
};

/** 按余额流水 Id 反查自己的订单号（只返回当前用户名下的订单）。
 * List<Guid> 绑定按「重复的查询键」（balanceTxnIds=a&balanceTxnIds=b）——CSV 串和
 * umi 默认的 ids[]=x 括号风格都绑不上（实测 400），这里手工拼查询串 */
export async function getOrderRefsByBalanceTxn(balanceTxnIds: string[]) {
  const qs = balanceTxnIds.map((id) => `balanceTxnIds=${encodeURIComponent(id)}`).join('&');
  return request<OrderRefDto[]>(`/api/app/order/order-refs-by-balance-txn?${qs}`, {
    method: 'GET',
  });
}

// ── 管理端（Host；全部订单、标履约、人工退款记录）────────────────────────────────────

export async function adminGetOrders(params: OrderListFilterDto & { maxResultCount?: number }) {
  return request<PagedResult<OrderDto>>('/api/app/order-admin', {
    method: 'GET',
    params: {
      OrderType: params.orderType,
      Status: params.status,
      HasAnomaly: params.hasAnomaly,
      UserId: params.userId,
      MaxResultCount: params.maxResultCount ?? 200,
    },
  });
}

export async function adminGetOrder(id: string) {
  return request<OrderDto>(`/api/app/order-admin/${id}`, { method: 'GET' });
}

/** 把业务单从 Paid 标成 Fulfilled（不可回冲；退款只做人工记录） */
export async function markOrderFulfilled(id: string) {
  return request<OrderDto>(`/api/app/order-admin/${id}/mark-fulfilled`, { method: 'POST' });
}

/** 人工退款记录：追加异常明细，不改订单状态 */
export async function recordManualRefund(
  id: string,
  data: { amount?: string; currency?: string; note: string },
) {
  return request<OrderDto>(`/api/app/order-admin/${id}/record-manual-refund`, {
    method: 'POST',
    data,
  });
}
