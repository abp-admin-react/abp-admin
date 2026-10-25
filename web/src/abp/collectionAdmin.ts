/**
 * 会员资金查询 API 镜像（对应 CollectionBalanceAdminAppService，收款查看权限）。
 * 用户名 → userId 的解析走 identity 的 getUsers（模块不做身份耦合）。
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
