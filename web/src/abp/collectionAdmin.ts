/**
 * 余额域管理端 API 镜像（对应 CollectionBalanceAdminAppService，收款查看权限）：
 * 会员资金（member-*）+ 资金看板（dashboard）。用户名 → userId 解析走 identity 的
 * getUsers（模块不做身份耦合）。余额 DTO/动作色板在此单源，用户侧 collection.ts 复用。
 */
import { request } from '@umijs/max';

const BASE = '/api/app/collection-balance-admin';

/** 会员余额行（与用户侧 MyBalanceDto 同形） */
export type MemberBalanceDto = {
  currency: string;
  balance: string;
  frozen: string;
  total: string;
};

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
};

// 单 Guid 参数端点 ABP 生成路径段（?userId= 形态不匹配路由→SPA 兜底 HTML，E2E 实测同
// approveLargeDeposit 教训；api-definition 确认：GET api/app/collection-balance-admin/member-balances/{userId}）
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
}): Promise<{ totalCount: number; items: BalanceTxnDto[] }> {
  return request(`${BASE}/member-txns/${params.userId}`, {
    params: { skipCount: params.skipCount, maxResultCount: params.maxResultCount },
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
  WithdrawFreeze: { color: 'orange', text: '提现冻结' },
  WithdrawUnfreeze: { color: 'cyan', text: '提现解冻' },
  WithdrawSettle: { color: 'purple', text: '提现出账' },
  WithdrawRefund: { color: 'default', text: '提现退款' },
};

/** 状态桶（计数 + 金额合计） */
export interface StatusBucketDto {
  status: string;
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
  /** 提现手续费累计（Completed） */
  withdrawFeeTotal: string;
  /** 在途提现净额（Pending/Processing/Broadcasting） */
  withdrawInFlightAmount: string;
  userBalances: UserBalanceBucketDto[];
  vaultChecks: VaultCheckLatestDto[];
}

/** 资金看板聚合（收款查看权限位；今日 = UTC 日界，与当日笔数限额同口径） */
export async function getTreasuryDashboard(): Promise<TreasuryDashboardDto> {
  return request(`${BASE}/dashboard`);
}
