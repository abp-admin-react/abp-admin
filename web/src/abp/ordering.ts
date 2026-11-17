import { request } from '@umijs/max';

/**
 * 订单 API 镜像。金额用十进制字符串保留精度，支付状态以后端返回为准。
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
  /** WechatNative | AlipayPrecreate | StripeCheckout | BalanceUsdt | BalanceUsdc | ChainDeposit */
  payChannel?: string;
  payAmount?: string;
  payCurrency?: string;
  paidAt?: string;
  /** 外部收款渠道单号，余额支付为 null。 */
  channelTradeNo?: string;
  /** 充值入账的网络。余额支付沿用历史结算网络字段。 */
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
  balanceTxnId?: string;
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
export type PayChannelCategory = 'Fiat' | 'Balance' | 'Chain';

/** 渠道元数据（与后端 OrderingPayChannel 对齐）——全站渠道显示的唯一出处 */
export const PAY_CHANNEL_META: Record<
  string,
  { category: PayChannelCategory; label: string; color: string }
> = {
  WechatNative: { category: 'Fiat', label: '微信 Native', color: 'green' },
  AlipayPrecreate: { category: 'Fiat', label: '支付宝当面付', color: 'blue' },
  StripeCheckout: {
    category: 'Fiat',
    label: 'Stripe Checkout',
    color: 'purple',
  },
  BalanceUsdt: { category: 'Balance', label: '余额 USDT', color: 'default' },
  BalanceUsdc: { category: 'Balance', label: '余额 USDC', color: 'default' },
  ChainDeposit: { category: 'Chain', label: '链上充值', color: 'gold' },
};

export const currencyLabel = (c?: string) =>
  c === 'Usdt'
    ? 'USDT'
    : c === 'Usdc'
      ? 'USDC'
      : c === 'Cny'
        ? 'CNY'
        : c === 'Usd'
          ? 'USD'
          : (c ?? '');

export function payChannelLabel(
  order: Pick<OrderDto, 'orderType' | 'payChannel' | 'payCurrency' | 'payNetwork'>,
): string | null {
  if (order.orderType === 'TopUp') {
    return ['链上充值', order.payNetwork, currencyLabel(order.payCurrency)]
      .filter(Boolean)
      .join(' · ');
  }
  if (!order.payChannel) return null;
  const meta = PAY_CHANNEL_META[order.payChannel];
  if (!meta) return order.payChannel;
  if (meta.category === 'Balance') {
    return `余额·${currencyLabel(order.payCurrency)}`;
  }
  return order.payCurrency
    ? `${meta.label}·${currencyLabel(order.payCurrency)}`
    : meta.label;
}

/** 订单可选的支付方式。渠道实际可用性由服务端决定。 */
export const PAY_METHOD_GROUPS = [
  {
    groupLabel: '余额支付',
    methods: [
      { value: 'BalanceUsdt', label: PAY_CHANNEL_META.BalanceUsdt.label },
      { value: 'BalanceUsdc', label: PAY_CHANNEL_META.BalanceUsdc.label },
    ],
  },
  {
    groupLabel: '第三方支付',
    methods: [
      { value: 'WechatNative', label: '微信支付（扫码）' },
      { value: 'AlipayPrecreate', label: '支付宝（扫码）' },
      { value: 'StripeCheckout', label: 'Stripe（USD）' },
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
      ...(params.balanceTxnId ? { BalanceTxnId: params.balanceTxnId } : {}),
      SkipCount: params.skipCount ?? 0,
      MaxResultCount: params.maxResultCount ?? 10,
    },
  });
}

/** 订单详情（含异常明细） */
export async function getOrder(id: string) {
  return request<OrderDto>(`/api/app/order/${id}`, { method: 'GET' });
}

/** 获取指定支付方式的有效报价，缺少报价时由服务端创建。 */
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

/** 反查当前用户的订单号。ABP 按重复的 balanceTxnIds 查询键绑定列表。 */
export async function getOrderRefsByBalanceTxn(balanceTxnIds: string[]) {
  const qs = balanceTxnIds
    .map((id) => `balanceTxnIds=${encodeURIComponent(id)}`)
    .join('&');
  return request<OrderRefDto[]>(
    `/api/app/order/order-refs-by-balance-txn?${qs}`,
    {
      method: 'GET',
    },
  );
}

// ── 管理端（Host；全部订单、标履约、人工退款记录）────────────────────────────────────

export async function adminGetOrders(
  params: OrderListFilterDto & { skipCount?: number; maxResultCount?: number },
) {
  return request<PagedResult<OrderDto>>('/api/app/order-admin', {
    method: 'GET',
    params: {
      OrderType: params.orderType,
      Status: params.status,
      HasAnomaly: params.hasAnomaly,
      UserId: params.userId,
      ...(params.balanceTxnId ? { BalanceTxnId: params.balanceTxnId } : {}),
      SkipCount: params.skipCount ?? 0,
      MaxResultCount: params.maxResultCount ?? 200,
    },
  });
}

export async function adminGetOrder(id: string) {
  return request<OrderDto>(`/api/app/order-admin/${id}`, { method: 'GET' });
}

/** 把业务单从 Paid 标成 Fulfilled（不可回冲；退款只做人工记录） */
export async function markOrderFulfilled(id: string) {
  return request<OrderDto>(`/api/app/order-admin/${id}/mark-fulfilled`, {
    method: 'POST',
  });
}

/** 人工退款记录：追加异常明细，不改订单状态 */
export async function recordManualRefund(
  id: string,
  data: { amount?: string; currency?: string; note: string; linkedRequestId?: string },
) {
  return request<OrderDto>(`/api/app/order-admin/${id}/record-manual-refund`, {
    method: 'POST',
    data,
  });
}
