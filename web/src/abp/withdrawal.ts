/**
 * 提现管理 API（collection 域，提现 M2/M3）。
 *
 * - 相对路径 `/api/app/...`；错误码与权限以后端 Contracts 为准（AbpAdmin.Collection.WithdrawAdmin）。
 * - 金额字段一律十进制字符串（与收款域同口径）。
 * - 页面只依赖本文件导出，勿散写 URL。
 * - 全部写端点带 { skipErrorHandler: true }：页面自己给中文错误提示（finance.adjustReq
 *   同款语义，withdrawal.adjustReq.test.ts 锁住）。
 */
import { request } from '@umijs/max';

const BASE = '/api/app/collection-withdraw-admin';

/** 写端点统一请求参数（跳过全局错误弹窗，页面自行提示） */
export const WD_REQ = { skipErrorHandler: true } as const;

/** 提现单视图（金额十进制字符串；与后端 WithdrawOrderDto 对齐） */
export type WithdrawOrderDto = {
  id: string;
  /** 提现用户 Id（管理端「用户」列） */
  userId: string;
  currency: string;
  networkName: string;
  toAddress: string;
  amount: string;
  fee: string;
  actualAmount: string;
  status: string;
  txHash?: string;
  retryCount: number;
  failReason?: string;
  auditRemark?: string;
  auditTime?: string;
  completeTime?: string;
  creationTime: string;
};

/** 提现配置快照（状态卡） */
export type WithdrawStatusDto = {
  enabled: boolean;
  halt: boolean;
  minAmount: string;
  feeRate: string;
  maxDailyAmount: string;
  maxDailyCount: string;
  autoApproveMaxAmount: string;
  pendingCount: number;
  processingCount: number;
  broadcastingCount: number;
  broadcastFailedCount: number;
};

/** 热钱包状态卡（无密钥信息） */
export type WithdrawHotWalletStatusDto = {
  configured: boolean;
  address?: string;
  usdtFloor: string;
  minBnb: string;
  bnb: string;
  breakerOpen: boolean;
  tokenBalances: Record<string, string>;
};

/** 开账回填结果 */
export type WithdrawOpeningBackfillResultDto = {
  users: number;
  posted: number;
  skipped: number;
};

/** 提现单状态（与后端 WithdrawStatus 对齐）：色板与文案是页面唯一口径 */
export const WD_STATUS_LABELS: Record<string, { color: string; text: string }> =
  {
    Pending: { color: 'processing', text: '待审核' },
    Processing: { color: 'cyan', text: '待广播' },
    Broadcasting: { color: 'orange', text: '广播中' },
    Completed: { color: 'success', text: '已完成' },
    Rejected: { color: 'default', text: '已拒绝' },
    BroadcastFailed: { color: 'error', text: '广播失败' },
    Cancelled: { color: 'default', text: '已撤单' },
  };

type PagedResult<T> = { totalCount: number; items: T[] };

// ── 查询 ───────────────────────────────────────────────────────────────────────────────

/** 提现单列表（可按状态/用户过滤，倒序分页；PascalCase 查询参数在镜像层收口） */
export async function getWithdrawals(params: {
  skipCount?: number;
  maxResultCount?: number;
  status?: string;
  userId?: string;
}): Promise<PagedResult<WithdrawOrderDto>> {
  return request(`${BASE}/withdrawals`, {
    params: {
      SkipCount: params.skipCount ?? 0,
      MaxResultCount: params.maxResultCount ?? 20,
      Status: params.status,
      UserId: params.userId,
    },
  });
}

/** 提现配置快照 */
export async function getWithdrawStatus(): Promise<WithdrawStatusDto> {
  return request(`${BASE}/status`);
}

/** 热钱包状态卡 */
export async function getHotWallet(): Promise<WithdrawHotWalletStatusDto> {
  return request(`${BASE}/hot-wallet`);
}

// ── 动作 ───────────────────────────────────────────────────────────────────────────────

/** 审核通过（审批前服务端有热钱包偿付+熔断护栏） */
export async function approveWithdraw(
  id: string,
  remark?: string,
): Promise<void> {
  await request(`${BASE}/approve`, {
    method: 'POST',
    data: { id, remark },
    ...WD_REQ,
  });
}

/** 拒绝（Pending 单解冻；原因必填） */
export async function rejectWithdraw(
  id: string,
  remark: string,
): Promise<void> {
  await request(`${BASE}/reject`, {
    method: 'POST',
    data: { id, remark },
    ...WD_REQ,
  });
}

/** 广播失败退款终态（净额+手续费退回用户可用；前提：链上确认未付出） */
export async function refundWithdraw(
  id: string,
  remark: string,
): Promise<void> {
  await request(`${BASE}/refund`, {
    method: 'POST',
    data: { id, remark },
    ...WD_REQ,
  });
}

/** 重试广播失败单（有哈希先验回执；无哈希过近期同地址付款双花门） */
// 单 Guid 参数端点为路径段形态（?id= 会 302——E2E 实测）
export async function retryWithdraw(id: string): Promise<void> {
  await request(`${BASE}/${id}/retry`, { method: 'POST', ...WD_REQ });
}

/** 人工完结（BroadcastFailed 单补录链上哈希；服务端强制回执且状态必须成功） */
// id 为路径段，txHash 是附加查询参数（?id= 形态会 302——E2E 实测）
export async function completeWithdraw(
  id: string,
  txHash: string,
): Promise<void> {
  await request(`${BASE}/${id}/complete?txHash=${encodeURIComponent(txHash)}`, {
    method: 'POST',
    ...WD_REQ,
  });
}

/** 配置提现热钱包（地址+私钥 body 传递；私钥与地址必须匹配，密文入库不回显） */
export async function setWithdrawVault(
  address: string,
  privateKey: string,
): Promise<void> {
  await request(`${BASE}/set-withdraw-vault`, {
    method: 'POST',
    data: { address, privateKey },
    ...WD_REQ,
  });
}

/** 存量余额开账回填（上线提现前执行一次；幂等可重跑） */
export async function backfillOpening(): Promise<WithdrawOpeningBackfillResultDto> {
  return request(`${BASE}/backfill-opening`, { method: 'POST', ...WD_REQ });
}
