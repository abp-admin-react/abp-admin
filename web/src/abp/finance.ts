/**
 * 财务 / Ledger 接口封装（定稿 §5）。
 * 默认走真接口（/api/app/fin-*）；仅 localStorage finance.useMock=1 时走内存假数据（形状对齐 §5.3，
 * 只用于离线看壳）。页面只认这里导出的函数和错误映射，换接口只改本文件。
 * 时间：接口 DateTime 一律 UTC（带 Z）；展示/筛选转换见 @/utils/time。
 */
import { request } from '@umijs/max';

export type PagedResultDto<T> = { totalCount: number; items: T[] };

export type FinanceListQuery = {
  skipCount?: number;
  maxResultCount?: number;
  sorting?: string;
  filter?: string;
  startTime?: string;
  endTime?: string;
  /** Guid?; 会计/老板可传；代理有 Scope.Agent 时服务端覆盖 */
  agentId?: string;
  /** 页内枚举，可多值逗号分隔 */
  status?: string;
  /** A6 专用；null=全部。对账默认排除演示 */
  isDemo?: boolean | null;
  bizType?: string;
  phase?: string;
  /** A5 专用：默认 false = 排除演示单据（产品 v1.1） */
  includeDemo?: boolean;
};

export type AdjustStatus = 'Draft' | 'Pending' | 'Rejected' | 'Posted' | 'Reversed';
export type VoucherStatus = 'Posted' | 'Reversed';
export type AccountType = 'Asset' | 'Liability' | 'Equity' | 'Income' | 'Expense';

export type FinAccountDto = {
  id: string;
  code: string;
  name: string;
  type: AccountType;
  normalSide: 'Debit' | 'Credit';
  isSystem: boolean;
  enabled: boolean;
  /** list DTO alias; prefer enabled when both present */
  isActive?: boolean;
  parentId?: string | null;
  hasEntries?: boolean;
};

export type FinPostingRuleDto = {
  id: string;
  bizType: string;
  phase: string;
  version: number;
  status: string;
  debitAccountCode: string;
  creditAccountCode: string;
  summary: string;
};

export type FinEntryDto = {
  seq: number;
  accountCode: string;
  accountName: string;
  ownerType?: string;
  ownerId?: string;
  debit: number;
  credit: number;
  summary?: string;
};

export type FinVoucherDto = {
  id: string;
  voucherNo: string;
  bizType: string;
  bizRefId: string;
  phase: string;
  periodLabel: string;
  status: VoucherStatus;
  debitTotal: number;
  creditTotal: number;
  creatorName?: string;
  creationTime: string;
  summary?: string;
  isDemo?: boolean;
  agentId?: string | null;
  entries?: FinEntryDto[];
  reversalOf?: string | null;
};

export type FinManualAdjustDto = {
  id: string;
  adjustNo: string;
  status: AdjustStatus;
  amount: number;
  currency: string;
  summary: string;
  applicantId: string;
  applicantName: string;
  auditorId?: string | null;
  auditorName?: string | null;
  rejectReason?: string | null;
  voucherId?: string | null;
  creationTime: string;
  lines: { accountCode: string; debit: number; credit: number; summary?: string }[];
};

export type FinPeriodDto = {
  id: string;
  year: number;
  month: number;
  label: string;
  status: 'Open' | 'Closed';
  lastPostFailure?: string | null;
};

export type FinReconcileRunDto = {
  id: string;
  type: string;
  rangeStart: string;
  rangeEnd: string;
  diffCount: number;
  status: string;
  createdAt: string;
  /** 对账默认排除 IsDemo；结果里可注明 */
  excludedDemo: boolean;
};

export type FinBizDocDto = {
  id: string;
  bizType: string;
  bizRefId: string;
  phase: string;
  amount: number;
  currency: string;
  status: string;
  agentId?: string | null;
  isDemo: boolean;
  creationTime: string;
  voucherId?: string | null;
};

export type FinBalanceProjectionDto = {
  id: string;
  ownerType: string;
  ownerId: string;
  ownerName?: string;
  currency: string;
  available: number;
  frozen: number;
  prepayment: number;
  ledgerDerived: number;
  diff: number;
  syncedAt: string;
  agentId?: string | null;
};

export type FinOverviewDto = {
  todayIn: number;
  todayOut: number;
  balanced: boolean;
  unreconciledCount: number;
  availableTotal: number;
  frozenTotal: number;
  prepaymentTotal: number;
  recognizedIncome: number;
};

/**
 * ABP 约定前缀 /api/app/fin-*（F1 已确认）：
 * - A1 GET fin-overview；A2b GET fin-posting-rule（只读）；A4 GET fin-period/current
 * - 调账动作 POST fin-manual-adjust/{id}/submit|approve|reject|reopen；reject body {rejectReason}
 * - A5 includeDemo query bool，默认 false
 * 具体段名以 F1 路径表为准，改时只改这里。
 */
export const API = {
  overview: '/api/app/fin-overview',
  account: '/api/app/fin-account',
  postingRule: '/api/app/fin-posting-rule',
  voucher: '/api/app/fin-voucher',
  manualAdjust: '/api/app/fin-manual-adjust',
  period: '/api/app/fin-period',
  reconcileRun: '/api/app/fin-reconcile',
  bizDoc: '/api/app/fin-biz-doc',
  balanceProjection: '/api/app/fin-ledger/sub-account-balances',
} as const;

export const FINANCE_ERROR = {
  SelfApproveForbidden: 'Finance:Adjust:SelfApproveNotAllowed',
  SelfApproveLegacy: 'Finance:Adjust:SelfApproveForbidden',
  SubmitDenied: 'Finance:Adjust:SubmitDenied',
  RejectDenied: 'Finance:Adjust:RejectDenied',
  ReverseDenied: 'Finance:Adjust:ReverseDenied',
  NotApplicant: 'Finance:Adjust:NotApplicant',
  CreateDenied: 'Finance:Adjust:CreateDenied',
  ApproveDenied: 'Finance:Adjust:ApproveDenied',
  InvalidStatus: 'Finance:Adjust:InvalidStatus',
  ReopenDenied: 'Finance:Adjust:ReopenDenied',
  RejectReasonRequired: 'Finance:Adjust:RejectReasonRequired',
  AccountSystemReadOnly: 'Finance:Account:SystemReadOnly',
  AccountHasEntries: 'Finance:Account:HasEntries',
  AccountDisableOnly: 'Finance:Account:DisableOnly',
} as const;

export const ADJUST_STATUS_LABEL: Record<AdjustStatus, { text: string; color: string }> = {
  Draft: { text: '草稿', color: 'default' },
  Pending: { text: '待批', color: 'processing' },
  Rejected: { text: '已驳回', color: 'warning' },
  Posted: { text: '已过账', color: 'success' },
  Reversed: { text: '已红冲', color: 'error' },
};

/**
 * A2 accounts write switch. v1 = read-only (GET /api/app/fin-account only).
 * Future: system accounts read-only; accounts with entries can only be disabled, not deleted.
 */
export const FIN_ACCOUNT_WRITE_ENABLED = false;

/** Future rule stub: whether an account may be deleted (else disable only). */
export function canDeleteAccount(a: { isSystem?: boolean; hasEntries?: boolean }): boolean {
  return FIN_ACCOUNT_WRITE_ENABLED && !a.isSystem && !a.hasEntries;
}


function normalizeAccount(a: FinAccountDto): FinAccountDto {
  const enabled = a.enabled ?? a.isActive ?? true;
  return { ...a, enabled, isActive: a.isActive ?? enabled };
}
function useMock(): boolean {
  // 默认走真接口；仅 localStorage finance.useMock=1 时用假数据
  if (typeof localStorage !== 'undefined') {
    return localStorage.getItem('finance.useMock') === '1';
  }
  return false;
}

/** 页面/测试可读：默认 live；仅 localStorage finance.useMock=1 时为 mock */
export function isFinanceMock(): boolean {
  return useMock();
}

/**
 * mock 种子制单人 id（与 mockAdjusts.applicantId 对齐）。
 * live 禁止回落成 user-boss，须用 ABP currentUser.id。
 */
export const MOCK_FINANCE_USER_ID = 'user-accountant';

/**
 * 解析当前用户 id，供 applicantId 比较（制单人 reopen、自审拦截）。
 * - 优先用 initialState / 调用方传入的 id（app.tsx 已从 application-configuration 映射为 userid）
 * - mock 且无传入时回落 MOCK_FINANCE_USER_ID
 * - live 且无传入时返回空串（不硬编码 user-boss）
 */
export function resolveFinanceUserId(fromInitial?: {
  userid?: string;
  id?: string;
} | null): string {
  const id = fromInitial?.userid || fromInitial?.id;
  if (id) return id;
  if (useMock()) return MOCK_FINANCE_USER_ID;
  return '';
}

/**
 * live：GET /api/abp/application-configuration → currentUser.id
 * mock：返回 MOCK_FINANCE_USER_ID
 */
export async function getFinanceCurrentUserId(): Promise<string> {
  if (useMock()) return MOCK_FINANCE_USER_ID;
  const config = await request<{
    currentUser?: { id?: string | null; isAuthenticated?: boolean };
  }>('/api/abp/application-configuration', {
    method: 'GET',
    params: { includeLocalizationResources: false },
  });
  const id = config?.currentUser?.id || '';
  if (!id) {
    abpError('Finance:Auth:Unauthenticated', '未登录或无法获取当前用户', 401);
  }
  return id;
}

function uid(prefix: string) {
  return prefix + '-' + Math.random().toString(36).slice(2, 10);
}

const mockAccounts: FinAccountDto[] = [
  { id: 'a1', code: '1001', name: '银行存款', type: 'Asset', normalSide: 'Debit', isSystem: true, enabled: true, hasEntries: true },
  { id: 'a2', code: '1122', name: '应收账款', type: 'Asset', normalSide: 'Debit', isSystem: true, enabled: true, hasEntries: false },
  { id: 'a3', code: '2203', name: '预收账款', type: 'Liability', normalSide: 'Credit', isSystem: true, enabled: true, hasEntries: true },
  { id: 'a4', code: '6001', name: '主营业务收入', type: 'Income', normalSide: 'Credit', isSystem: true, enabled: true, hasEntries: true },
  { id: 'a5', code: '6603', name: '手续费', type: 'Expense', normalSide: 'Debit', isSystem: false, enabled: true, hasEntries: false },
  { id: 'a6', code: '6901', name: '调账过渡', type: 'Equity', normalSide: 'Debit', isSystem: true, enabled: true, hasEntries: false },
];

const mockRules: FinPostingRuleDto[] = [
  { id: 'r1', bizType: 'demo.rent', phase: 'collect', version: 1, status: 'Active', debitAccountCode: '1001', creditAccountCode: '2203', summary: '云机租金收款→预收' },
  { id: 'r2', bizType: 'demo.rent', phase: 'recognize', version: 1, status: 'Active', debitAccountCode: '2203', creditAccountCode: '6001', summary: '预收确认收入' },
  { id: 'r3', bizType: 'demo.topup', phase: 'collect', version: 1, status: 'Active', debitAccountCode: '1001', creditAccountCode: '2203', summary: 'AI 充值→预收' },
  { id: 'r4', bizType: 'demo.topup', phase: 'recognize', version: 1, status: 'Active', debitAccountCode: '2203', creditAccountCode: '6001', summary: '用量确认收入' },
  { id: 'r5', bizType: 'demo.commission', phase: 'settle', version: 1, status: 'Active', debitAccountCode: '6603', creditAccountCode: '1122', summary: '代理佣金' },
];

let mockVouchers: FinVoucherDto[] = [
  {
    id: 'v1', voucherNo: 'V-202610-0001', bizType: 'demo.rent', bizRefId: 'ord-1001', phase: 'collect',
    periodLabel: '2026-10', status: 'Posted', debitTotal: 100, creditTotal: 100,
    creatorName: 'system', creationTime: '2026-10-10T08:00:00+08:00', summary: '租金收款',
    isDemo: true, agentId: 'ag-1',
    entries: [
      { seq: 1, accountCode: '1001', accountName: '银行存款', debit: 100, credit: 0 },
      { seq: 2, accountCode: '2203', accountName: '预收账款', debit: 0, credit: 100 },
    ],
  },
  {
    id: 'v2', voucherNo: 'V-202610-0002', bizType: 'demo.rent', bizRefId: 'ord-1001', phase: 'recognize',
    periodLabel: '2026-10', status: 'Posted', debitTotal: 30, creditTotal: 30,
    creatorName: 'system', creationTime: '2026-10-12T09:00:00+08:00', summary: '租金确认收入',
    isDemo: true, agentId: 'ag-1',
    entries: [
      { seq: 1, accountCode: '2203', accountName: '预收账款', debit: 30, credit: 0 },
      { seq: 2, accountCode: '6001', accountName: '主营业务收入', debit: 0, credit: 30 },
    ],
  },
];

let mockAdjusts: FinManualAdjustDto[] = [
  {
    id: 'adj-1', adjustNo: 'ADJ-001', status: 'Draft', amount: 50, currency: 'CNY',
    summary: '手续费补记', applicantId: 'user-accountant', applicantName: '会计甲',
    creationTime: '2026-10-13T10:00:00+08:00',
    lines: [
      { accountCode: '6603', debit: 50, credit: 0, summary: '手续费' },
      { accountCode: '1001', debit: 0, credit: 50, summary: '银行存款' },
    ],
  },
  {
    id: 'adj-2', adjustNo: 'ADJ-002', status: 'Pending', amount: 20, currency: 'CNY',
    summary: '预收纠偏', applicantId: 'user-accountant', applicantName: '会计甲',
    creationTime: '2026-10-13T11:00:00+08:00',
    lines: [
      { accountCode: '2203', debit: 20, credit: 0 },
      { accountCode: '6001', debit: 0, credit: 20 },
    ],
  },
  {
    id: 'adj-3', adjustNo: 'ADJ-003', status: 'Pending', amount: 10, currency: 'CNY',
    summary: '老板自测单（用于验自批拦截）', applicantId: 'user-boss', applicantName: '老板',
    creationTime: '2026-10-13T12:00:00+08:00',
    lines: [
      { accountCode: '6901', debit: 10, credit: 0 },
      { accountCode: '1001', debit: 0, credit: 10 },
    ],
  },  {
    id: 'adj-4', adjustNo: 'ADJ-004', status: 'Rejected', amount: 15, currency: 'CNY',
    summary: '渠道手续费冲回（被驳回，制单人=当前演示用户）', applicantId: 'user-boss', applicantName: '老板',
    auditorId: 'user-accountant2', auditorName: '会计乙',
    rejectReason: '缺少渠道对账单附件，补齐后重新提交',
    creationTime: '2026-10-13T13:00:00+08:00',
    lines: [
      { accountCode: '1001', debit: 15, credit: 0 },
      { accountCode: '6603', debit: 0, credit: 15 },
    ],
  },
  {
    id: 'adj-5', adjustNo: 'ADJ-005', status: 'Rejected', amount: 8, currency: 'CNY',
    summary: '他人被驳回单（当前用户不可重新编辑）', applicantId: 'user-accountant', applicantName: '会计甲',
    auditorId: 'user-boss', auditorName: '老板',
    rejectReason: '金额与流水不符',
    creationTime: '2026-10-13T14:00:00+08:00',
    lines: [
      { accountCode: '6603', debit: 8, credit: 0 },
      { accountCode: '1001', debit: 0, credit: 8 },
    ],
  },
];

const mockPeriod: FinPeriodDto = {
  id: 'p-202610', year: 2026, month: 10, label: '2026-10', status: 'Open',
  lastPostFailure: null,
};

let mockReconciles: FinReconcileRunDto[] = [
  {
    id: 'rc-1', type: 'ledger-internal', rangeStart: '2026-10-01', rangeEnd: '2026-10-14',
    diffCount: 0, status: 'Done', createdAt: '2026-10-14T07:00:00+08:00', excludedDemo: true,
  },
  {
    id: 'rc-0', type: 'ledger-internal', rangeStart: '2026-10-01', rangeEnd: '2026-10-13',
    diffCount: 0, status: 'Done', createdAt: '2026-10-13T07:00:00+08:00', excludedDemo: false,
  },
];

const mockBizDocs: FinBizDocDto[] = [
  { id: 'bd-1', bizType: 'demo.rent', bizRefId: 'ord-1001', phase: 'collect', amount: 100, currency: 'CNY', status: 'Settled', agentId: 'ag-1', isDemo: true, creationTime: '2026-10-10T08:00:00+08:00', voucherId: 'v1' },
  { id: 'bd-2', bizType: 'order.pay', bizRefId: 'ord-2002', phase: 'paid', amount: 88, currency: 'CNY', status: 'Paid', agentId: 'ag-2', isDemo: false, creationTime: '2026-10-11T14:00:00+08:00', voucherId: null },
];

const mockBalances: FinBalanceProjectionDto[] = [
  { id: 'bal-1', ownerType: 'User', ownerId: 'u-1', ownerName: '用户甲', currency: 'CNY', available: 120, frozen: 30, prepayment: 70, ledgerDerived: 150, diff: 0, syncedAt: '2026-10-14T07:30:00+08:00', agentId: 'ag-1' },
  { id: 'bal-2', ownerType: 'User', ownerId: 'u-2', ownerName: '用户乙', currency: 'CNY', available: 40, frozen: 0, prepayment: 10, ledgerDerived: 55, diff: -5, syncedAt: '2026-10-14T07:30:00+08:00', agentId: 'ag-2' },
];

function pageOf<T>(all: T[], q: FinanceListQuery): PagedResultDto<T> {
  const skip = q.skipCount ?? 0;
  const take = Math.min(q.maxResultCount ?? 20, 100);
  return { totalCount: all.length, items: all.slice(skip, skip + take) };
}

function abpError(code: string, message: string, status = 400): never {
  const err: any = new Error(message);
  err.name = 'BizError';
  err.info = { code, message };
  err.response = { status, data: { error: { code, message } } };
  throw err;
}

/**
 * 错误码 → 中文提示。码以后端 FinanceErrorCodes 为准（error.code）；
 * 自批是 Finance:Adjust:SelfApproveNotAllowed（403），Legacy 码仅兼容旧 mock。
 */
function mapFinanceErrorMessage(code?: string, fallback?: string) {
  switch (code) {
    case FINANCE_ERROR.SelfApproveLegacy:
    case FINANCE_ERROR.SelfApproveForbidden:
      return '不能审批自己发起的调账';
    case FINANCE_ERROR.CreateDenied:
      return '没有调账创建权限';
    case FINANCE_ERROR.ApproveDenied:
      return '没有调账审批权限';
    case FINANCE_ERROR.InvalidStatus:
      return '单据状态已变化，请刷新';
    case FINANCE_ERROR.ReopenDenied:
      return '只有制单人可以重新编辑';
    case FINANCE_ERROR.RejectReasonRequired:
      return '请填写驳回意见';
    case FINANCE_ERROR.SubmitDenied:
      return '没有调账提交权限';
    case FINANCE_ERROR.RejectDenied:
      return '没有调账驳回权限';
    case FINANCE_ERROR.ReverseDenied:
      return '没有调账冲销权限';
    case FINANCE_ERROR.NotApplicant:
      return '只有申请人本人可操作';
    case FINANCE_ERROR.AccountSystemReadOnly:
      return '系统科目只读，不可修改或删除';
    case FINANCE_ERROR.AccountHasEntries:
      return '科目已有分录，只能停用不能删除';
    case FINANCE_ERROR.AccountDisableOnly:
      return '有分录的科目只能停用';
    default:
      return fallback || '操作失败';
  }
}

/**
 * InvalidInput（BusinessException）字段：后端放在 error.data.fields（逗号分隔，如 "amount,currency"）。
 * 兼容 info.data / response.data.error.data / 数组形状 / validationErrors[].members。
 */
export function financeErrorFields(error: unknown): string[] {
  const e = error as any;
  const errObj = e?.response?.data?.error ?? e?.data?.error ?? e?.error;
  const code: string = financeErrorCode(error) || '';
  const candidates = [
    errObj?.data?.fields,
    e?.info?.data?.fields,
    e?.data?.fields,
    errObj?.data?.Fields,
  ];
  const out: string[] = [];
  for (const c of candidates) {
    if (typeof c === 'string') out.push(...c.split(/[,;\s]+/));
    else if (Array.isArray(c)) out.push(...c.map(String));
  }
  if (Array.isArray(errObj?.validationErrors)) {
    for (const v of errObj.validationErrors) {
      if (Array.isArray(v?.members)) out.push(...v.members.map(String));
    }
  }
  const fields = Array.from(new Set(out.map((s) => s.trim()).filter(Boolean)));
  if (!fields.length && !/InvalidInput/i.test(code)) return [];
  return fields.map((s) => s.charAt(0).toLowerCase() + s.slice(1));
}

export function isFinanceInvalidInput(error: unknown): boolean {
  return /InvalidInput/i.test(financeErrorCode(error) || '') || financeErrorFields(error).length > 0;
}

export function financeErrorCode(error: unknown): string | undefined {
  const e = error as {
    info?: { code?: string };
    response?: { data?: { error?: { code?: string } } };
  };
  return e?.info?.code || e?.response?.data?.error?.code;
}

/** HTTP 403（无权限）判定，用于页面「没有权限查看此页面」 */
export function isFinanceForbidden(error: unknown): boolean {
  const e = error as { response?: { status?: number } };
  return e?.response?.status === 403;
}

export function financeErrorText(error: unknown): string {
  const e = error as {
    info?: { code?: string; message?: string };
    response?: { data?: { error?: { code?: string; message?: string } } };
  };
  const code = e?.info?.code || e?.response?.data?.error?.code;
  const msg = e?.info?.message || e?.response?.data?.error?.message;
  return mapFinanceErrorMessage(code, msg);
}

async function liveGet<T>(url: string, params?: Record<string, unknown>) {
  return request<T>(url, { method: 'GET', params });
}
async function livePost<T>(url: string, data?: unknown) {
  return request<T>(url, { method: 'POST', data });
}
async function livePut<T>(url: string, data?: unknown) {
  return request<T>(url, { method: 'PUT', data });
}
async function liveDelete<T>(url: string) {
  return request<T>(url, { method: 'DELETE' });
}

export async function getFinanceOverview(): Promise<FinOverviewDto> {
  if (!useMock()) return liveGet(API.overview);
  return {
    todayIn: 188, todayOut: 20, balanced: true, unreconciledCount: 1,
    availableTotal: 160, frozenTotal: 30, prepaymentTotal: 80, recognizedIncome: 30,
  };
}

export async function getAccounts(q: FinanceListQuery = {}): Promise<PagedResultDto<FinAccountDto>> {
  if (!useMock()) {
    const page = await liveGet<PagedResultDto<FinAccountDto>>(API.account, q as Record<string, unknown>);
    return { ...page, items: (page.items || []).map(normalizeAccount) };
  }
  let list = [...mockAccounts];
  if (q.filter) {
    const f = q.filter.toLowerCase();
    list = list.filter((x) => x.code.includes(f) || x.name.includes(f));
  }
  return pageOf(list, q);
}

export async function createAccount(input: Partial<FinAccountDto>): Promise<FinAccountDto> {
  if (!useMock()) return livePost(API.account, input);
  const row: FinAccountDto = {
    id: uid('a'), code: input.code || '', name: input.name || '',
    type: (input.type as AccountType) || 'Asset', normalSide: input.normalSide || 'Debit',
    isSystem: false, enabled: true, hasEntries: false,
  };
  mockAccounts.push(row);
  return row;
}

export async function updateAccount(id: string, input: Partial<FinAccountDto>): Promise<FinAccountDto> {
  if (!useMock()) return livePut(API.account + '/' + id, input);
  const i = mockAccounts.findIndex((x) => x.id === id);
  if (i < 0) abpError('Finance:Account:NotFound', '科目不存在', 404);
  mockAccounts[i] = { ...mockAccounts[i], ...input, id };
  return mockAccounts[i];
}

export async function deleteAccount(id: string): Promise<void> {
  if (!useMock()) return liveDelete(API.account + '/' + id);
  const row = mockAccounts.find((x) => x.id === id);
  if (!row) abpError('Finance:Account:NotFound', '科目不存在', 404);
  if (row.isSystem) abpError(FINANCE_ERROR.AccountSystemReadOnly, '系统科目不可删');
  if (row.hasEntries) abpError('Finance:Account:HasEntries', '有分录的科目不可删，只能停用');
  const idx = mockAccounts.findIndex((x) => x.id === id);
  mockAccounts.splice(idx, 1);
}

export async function getPostingRules(q: FinanceListQuery = {}): Promise<PagedResultDto<FinPostingRuleDto>> {
  if (!useMock()) return liveGet(API.postingRule, q as Record<string, unknown>);
  return pageOf(mockRules, q);
}

export async function getVouchers(q: FinanceListQuery = {}): Promise<PagedResultDto<FinVoucherDto>> {
  if (!useMock()) return liveGet(API.voucher, q as Record<string, unknown>);
  let list = [...mockVouchers];
  if (q.filter) {
    const f = q.filter.toLowerCase();
    list = list.filter((x) => x.voucherNo.toLowerCase().includes(f) || (x.summary || '').toLowerCase().includes(f));
  }
  if (q.bizType) list = list.filter((x) => x.bizType === q.bizType);
  if (q.agentId) list = list.filter((x) => x.agentId === q.agentId);
  if (q.isDemo === true) list = list.filter((x) => x.isDemo);
  if (q.isDemo === false) list = list.filter((x) => !x.isDemo);
  return pageOf(list, q);
}

export async function getVoucher(id: string): Promise<FinVoucherDto> {
  if (!useMock()) return liveGet(API.voucher + '/' + id);
  const row = mockVouchers.find((x) => x.id === id);
  if (!row) abpError('Finance:Voucher:NotFound', '凭证不存在', 404);
  return row;
}

export async function reverseVoucher(id: string): Promise<FinVoucherDto> {
  if (!useMock()) return livePost(API.voucher + '/' + id + '/reverse');
  const row = mockVouchers.find((x) => x.id === id);
  if (!row) abpError('Finance:Voucher:NotFound', '凭证不存在', 404);
  if (row.status !== 'Posted') abpError('Finance:Voucher:InvalidStatus', '仅已过账凭证可红冲');
  row.status = 'Reversed';
  const rev: FinVoucherDto = {
    ...row,
    id: uid('v'),
    voucherNo: row.voucherNo + '-R',
    status: 'Posted',
    reversalOf: row.id,
    creationTime: new Date().toISOString(),
    summary: '红冲 ' + row.voucherNo,
    entries: row.entries?.map((e) => ({ ...e, debit: e.credit, credit: e.debit })),
  };
  mockVouchers = [rev, ...mockVouchers];
  return rev;
}

export async function getAdjusts(q: FinanceListQuery = {}): Promise<PagedResultDto<FinManualAdjustDto>> {
  if (!useMock()) {
    const page = await liveGet<{ totalCount: number; items: Record<string, unknown>[] }>(API.manualAdjust, q as Record<string, unknown>);
    return { totalCount: page.totalCount, items: (page.items || []).map(fromLiveAdjust) };
  }
  let list = [...mockAdjusts];
  if (q.status) {
    const set = new Set(q.status.split(',').map((s) => s.trim()));
    list = list.filter((x) => set.has(x.status));
  }
  if (q.filter) {
    const f = q.filter.toLowerCase();
    list = list.filter((x) => x.adjustNo.toLowerCase().includes(f) || x.summary.toLowerCase().includes(f));
  }
  return pageOf(list, q);
}


const ADJUST_STATUS_FROM_API: AdjustStatus[] = ['Draft', 'Pending', 'Rejected', 'Posted', 'Reversed'];

function mapAdjustStatus(v: unknown): AdjustStatus {
  if (typeof v === 'number' && v >= 0 && v < ADJUST_STATUS_FROM_API.length) return ADJUST_STATUS_FROM_API[v];
  if (typeof v === 'string' && (ADJUST_STATUS_FROM_API as string[]).includes(v)) return v as AdjustStatus;
  return 'Draft';
}

/** Live CreateUpdateFinManualAdjustDto — never send summary/lines/applicantId. */
export type FinOwnerTypeName = 'Platform' | 'User' | 'Agent' | 'Channel';
export const FIN_OWNER_TYPE: Record<FinOwnerTypeName, number> = {
  Platform: 0,
  User: 1,
  Agent: 2,
  Channel: 3,
};

export type CreateUpdateFinManualAdjustInput = {
  amount: number;
  currency: string;
  debitAccountCode: string;
  debitOwnerType?: FinOwnerTypeName | number;
  debitOwnerId?: string | null;
  creditAccountCode?: string | null;
  creditOwnerType?: FinOwnerTypeName | number;
  creditOwnerId?: string | null;
  reason?: string | null;
  /** UI-only aliases mapped away before POST */
  summary?: string;
  lines?: FinManualAdjustDto['lines'];
};

function toLiveAdjustBody(input: CreateUpdateFinManualAdjustInput) {
  const lines = input.lines || [];
  const debitLine = lines.find((l) => Number(l.debit || 0) > 0) || lines[0];
  const creditLine = lines.find((l) => Number(l.credit || 0) > 0) || lines[1];
  const amount =
    Number(input.amount) ||
    lines.reduce((sum, l) => sum + Number(l.debit || 0), 0) ||
    lines.reduce((sum, l) => sum + Number(l.credit || 0), 0);
  const coerceOwner = (v: FinOwnerTypeName | number | undefined, fallback: number) => {
    if (typeof v === 'number') return v;
    if (typeof v === 'string' && v in FIN_OWNER_TYPE) return FIN_OWNER_TYPE[v as FinOwnerTypeName];
    return fallback;
  };
  const body: Record<string, unknown> = {
    amount,
    currency: input.currency || 'CNY',
    debitAccountCode: input.debitAccountCode || debitLine?.accountCode || '',
    debitOwnerType: coerceOwner(input.debitOwnerType, FIN_OWNER_TYPE.Channel),
    debitOwnerId: input.debitOwnerId ?? 'default',
    reason: input.reason ?? input.summary ?? undefined,
  };
  const creditCode = input.creditAccountCode || creditLine?.accountCode;
  if (creditCode) body.creditAccountCode = creditCode;
  if (input.creditOwnerType !== undefined && input.creditOwnerType !== null) {
    body.creditOwnerType = coerceOwner(input.creditOwnerType as FinOwnerTypeName | number | undefined, FIN_OWNER_TYPE.Platform);
  }
  if (input.creditOwnerId) body.creditOwnerId = input.creditOwnerId;
  return body;
}
/** Normalize live FinManualAdjustDto → UI shape (lines/summary/adjustNo/status string). */
function fromLiveAdjust(raw: Record<string, unknown>): FinManualAdjustDto {
  const debit = String(raw.debitAccountCode || '');
  const credit = String(raw.creditAccountCode || '');
  const amount = Number(raw.amount || 0);
  const reason = String(raw.reason || raw.summary || '');
  const id = String(raw.id || '');
  return {
    id,
    adjustNo: String(raw.adjustNo || (id ? 'ADJ-' + id.slice(0, 8) : '')),
    status: mapAdjustStatus(raw.status),
    amount,
    currency: String(raw.currency || 'CNY'),
    summary: reason,
    applicantId: String(raw.applicantId || ''),
    applicantName: String(raw.applicantName || ''),
    auditorId: (raw.auditorId as string) ?? null,
    auditorName: (raw.auditorName as string) ?? null,
    rejectReason: (raw.rejectReason as string) ?? null,
    voucherId: (raw.voucherId as string) ?? null,
    creationTime: String(raw.creationTime || raw.submittedAt || new Date().toISOString()),
    lines: [
      { accountCode: debit, debit: amount, credit: 0, summary: reason },
      { accountCode: credit || debit, debit: 0, credit: amount, summary: reason },
    ],
  };
}

export async function createAdjust(input: CreateUpdateFinManualAdjustInput & {
  summary?: string;
  lines?: FinManualAdjustDto['lines'];
  applicantId?: string;
  applicantName?: string;
}): Promise<FinManualAdjustDto> {
  if (!useMock()) {
    const raw = await livePost<Record<string, unknown>>(API.manualAdjust, toLiveAdjustBody(input));
    return fromLiveAdjust(raw);
  }
  const debitCode = input.debitAccountCode || input.lines?.[0]?.accountCode || '1002';
  const creditCode = input.creditAccountCode || input.lines?.[1]?.accountCode || '3101';
  const reason = input.reason ?? input.summary ?? '';
  const row: FinManualAdjustDto = {
    id: uid('adj'),
    adjustNo: 'ADJ-' + String(mockAdjusts.length + 1).padStart(3, '0'),
    status: 'Draft',
    amount: input.amount,
    currency: input.currency || 'CNY',
    summary: reason,
    applicantId: input.applicantId || MOCK_FINANCE_USER_ID,
    applicantName: input.applicantName || 'accountant',
    creationTime: new Date().toISOString(),
    lines: input.lines || [
      { accountCode: debitCode, debit: input.amount, credit: 0, summary: reason },
      { accountCode: creditCode, debit: 0, credit: input.amount, summary: reason },
    ],
  };
  mockAdjusts = [row, ...mockAdjusts];
  return row;
}

function findAdjust(id: string) {
  const i = mockAdjusts.findIndex((x) => x.id === id);
  if (i < 0) abpError('Finance:Adjust:NotFound', '调账不存在', 404);
  return i;
}

function requireDraft(cur: FinManualAdjustDto) {
  // 产品 v1.1：编辑/删除/提交非 Draft → InvalidStatus
  if (cur.status !== 'Draft') {
    abpError(FINANCE_ERROR.InvalidStatus, mapFinanceErrorMessage(FINANCE_ERROR.InvalidStatus));
  }
}

export async function updateAdjust(
  id: string,
  input: CreateUpdateFinManualAdjustInput | Partial<Pick<FinManualAdjustDto, 'summary' | 'amount' | 'lines' | 'currency'>>,
): Promise<FinManualAdjustDto> {
  if (!useMock()) {
    const raw = await livePut<Record<string, unknown>>(
      API.manualAdjust + '/' + id,
      toLiveAdjustBody(input as CreateUpdateFinManualAdjustInput),
    );
    return fromLiveAdjust(raw);
  }
  const i = findAdjust(id);
  const cur = mockAdjusts[i];
  requireDraft(cur);
  mockAdjusts[i] = { ...cur, ...input, id, status: cur.status };
  return mockAdjusts[i];
}

export async function deleteAdjust(id: string): Promise<void> {
  if (!useMock()) return liveDelete(API.manualAdjust + '/' + id);
  const i = findAdjust(id);
  requireDraft(mockAdjusts[i]);
  mockAdjusts.splice(i, 1);
}

export async function getAdjust(id: string): Promise<FinManualAdjustDto> {
  if (!useMock()) {
    const raw = await liveGet<Record<string, unknown>>(API.manualAdjust + '/' + id);
    return fromLiveAdjust(raw);
  }
  return mockAdjusts[findAdjust(id)];
}

export async function submitAdjust(id: string): Promise<FinManualAdjustDto> {
  if (!useMock()) {
    const raw = await livePost<Record<string, unknown>>(API.manualAdjust + '/' + id + '/submit');
    return fromLiveAdjust(raw);
  }
  const i = findAdjust(id);
  requireDraft(mockAdjusts[i]);
  // rejectReason 一直保留（产品 v1）
  mockAdjusts[i] = { ...mockAdjusts[i], status: 'Pending' };
  return mockAdjusts[i];
}

export async function approveAdjust(
  id: string,
  auditor: { id: string; name: string },
): Promise<FinManualAdjustDto> {
  if (!useMock()) {
    const raw = await livePost<Record<string, unknown>>(API.manualAdjust + '/' + id + '/approve');
    return fromLiveAdjust(raw);
  }
  const i = findAdjust(id);
  const cur = mockAdjusts[i];
  if (cur.status !== 'Pending') {
    abpError(FINANCE_ERROR.InvalidStatus, mapFinanceErrorMessage(FINANCE_ERROR.InvalidStatus));
  }
  if (cur.applicantId === auditor.id) {
    abpError(
      FINANCE_ERROR.SelfApproveForbidden,
      mapFinanceErrorMessage(FINANCE_ERROR.SelfApproveForbidden),
      403,
    );
  }
  const voucher: FinVoucherDto = {
    id: uid('v'),
    voucherNo: 'V-ADJ-' + cur.adjustNo,
    bizType: 'manual-adjust',
    bizRefId: cur.id,
    phase: 'post',
    periodLabel: '2026-10',
    status: 'Posted',
    debitTotal: cur.amount,
    creditTotal: cur.amount,
    creatorName: auditor.name,
    creationTime: new Date().toISOString(),
    summary: cur.summary,
    isDemo: false,
    entries: cur.lines.map((l, idx) => ({
      seq: idx + 1,
      accountCode: l.accountCode,
      accountName: l.accountCode,
      debit: l.debit,
      credit: l.credit,
      summary: l.summary,
    })),
  };
  mockVouchers = [voucher, ...mockVouchers];
  mockAdjusts[i] = {
    ...cur,
    status: 'Posted',
    auditorId: auditor.id,
    auditorName: auditor.name,
    voucherId: voucher.id,
  };
  return mockAdjusts[i];
}

export async function rejectAdjust(
  id: string,
  auditor: { id: string; name: string },
  rejectReason: string,
): Promise<FinManualAdjustDto> {
  if (!useMock()) {
    const raw = await livePost<Record<string, unknown>>(API.manualAdjust + '/' + id + '/reject', { rejectReason });
    return fromLiveAdjust(raw);
  }
  if (!rejectReason || !rejectReason.trim()) {
    abpError(FINANCE_ERROR.RejectReasonRequired, mapFinanceErrorMessage(FINANCE_ERROR.RejectReasonRequired));
  }
  const i = findAdjust(id);
  const cur = mockAdjusts[i];
  if (cur.status !== 'Pending') {
    abpError(FINANCE_ERROR.InvalidStatus, mapFinanceErrorMessage(FINANCE_ERROR.InvalidStatus));
  }
  if (cur.applicantId === auditor.id) {
    abpError(
      FINANCE_ERROR.SelfApproveForbidden,
      mapFinanceErrorMessage(FINANCE_ERROR.SelfApproveForbidden),
      403,
    );
  }
  mockAdjusts[i] = {
    ...cur,
    status: 'Rejected',
    auditorId: auditor.id,
    auditorName: auditor.name,
    rejectReason: rejectReason.trim(),
  };
  return mockAdjusts[i];
}

/**
 * Rejected → Draft，仅制单人 + Finance.Adjust.Create。
 * mock 侧 currentUserId / canCreate 由调用方传入（live 由服务端按 token 判定）。
 */
export async function reopenAdjust(
  id: string,
  me: { id: string; canCreate: boolean },
): Promise<FinManualAdjustDto> {
  if (!useMock()) {
    const raw = await livePost<Record<string, unknown>>(API.manualAdjust + '/' + id + '/reopen');
    return fromLiveAdjust(raw);
  }
  const i = findAdjust(id);
  const cur = mockAdjusts[i];
  if (!me.canCreate) {
    abpError(FINANCE_ERROR.CreateDenied, mapFinanceErrorMessage(FINANCE_ERROR.CreateDenied), 403);
  }
  if (cur.applicantId !== me.id) {
    abpError(FINANCE_ERROR.ReopenDenied, mapFinanceErrorMessage(FINANCE_ERROR.ReopenDenied), 403);
  }
  if (cur.status !== 'Rejected') {
    abpError(FINANCE_ERROR.InvalidStatus, mapFinanceErrorMessage(FINANCE_ERROR.InvalidStatus));
  }
  mockAdjusts[i] = { ...cur, status: 'Draft' };
  return mockAdjusts[i];
}

/** Posted -> Reversed（F1：POST /api/app/fin-manual-adjust/{id}/reverse） */
export async function reverseAdjust(id: string): Promise<FinManualAdjustDto> {
  if (!useMock()) {
    const raw = await livePost<Record<string, unknown>>(API.manualAdjust + '/' + id + '/reverse');
    return fromLiveAdjust(raw);
  }
  const i = findAdjust(id);
  const cur = mockAdjusts[i];
  if (cur.status !== 'Posted') {
    abpError(FINANCE_ERROR.InvalidStatus, mapFinanceErrorMessage(FINANCE_ERROR.InvalidStatus));
  }
  mockAdjusts[i] = { ...cur, status: 'Reversed' as FinManualAdjustDto['status'] };
  return mockAdjusts[i];
}

export async function getCurrentPeriod(): Promise<FinPeriodDto> {
  if (!useMock()) return liveGet(API.period + '/current');
  return mockPeriod;
}

function parseJsonObj(v: unknown): Record<string, any> {
  if (v && typeof v === 'object') return v as Record<string, any>;
  if (typeof v !== 'string' || !v) return {};
  try { return JSON.parse(v) ?? {}; } catch { return {}; }
}
/** Live FinReconcileRunDto (runType/hasDiff/diffJson/scopeJson/creationTime) -> UI shape. */
function fromLiveReconcile(raw: Record<string, unknown>): FinReconcileRunDto {
  const scope = parseJsonObj(raw.scopeJson);
  const diff = parseJsonObj(raw.diffJson);
  const projDiffs = Array.isArray(diff.projectionDiffs) ? diff.projectionDiffs.length : 0;
  const diffCount = typeof raw.diffCount === 'number'
    ? raw.diffCount
    : projDiffs + (diff.unbalanced ? 1 : 0) || (raw.hasDiff ? 1 : 0);
  const includeDemo = (raw.includeDemo ?? scope.includeDemo) === true;
  return {
    id: String(raw.id || ''),
    type: String(raw.runType || raw.type || ''),
    rangeStart: String(raw.rangeStart || scope.rangeStart || ''),
    rangeEnd: String(raw.rangeEnd || scope.rangeEnd || ''),
    diffCount,
    status: String(raw.status || (diffCount > 0 ? 'HasDiff' : 'Done')),
    createdAt: String(raw.createdAt || raw.creationTime || ''),
    excludedDemo: !includeDemo,
  };
}

export async function getReconcileRuns(q: FinanceListQuery = {}): Promise<PagedResultDto<FinReconcileRunDto>> {
  if (!useMock()) {
    const res = await liveGet<PagedResultDto<Record<string, unknown>>>(API.reconcileRun, { ...q, includeDemo: !!q.includeDemo } as Record<string, unknown>);
    return { totalCount: res?.totalCount ?? 0, items: (res?.items ?? []).map(fromLiveReconcile) };
  }
  const list = q.includeDemo ? mockReconciles : mockReconciles.filter((x) => x.excludedDemo);
  return pageOf(list, q);
}

export async function runReconcile(input: {
  rangeStart: string;
  rangeEnd: string;
  /** 产品 v1.1：默认 false = 排除演示单据 */
  includeDemo?: boolean;
}): Promise<FinReconcileRunDto> {
  if (!useMock()) {
    const raw = await livePost<Record<string, unknown>>(API.reconcileRun + '/run', { ...input, includeDemo: !!input.includeDemo });
    return fromLiveReconcile(raw);
  }
  const row: FinReconcileRunDto = {
    id: uid('rc'),
    type: 'ledger-internal',
    rangeStart: input.rangeStart,
    rangeEnd: input.rangeEnd,
    diffCount: 0,
    status: 'Done',
    createdAt: new Date().toISOString(),
    excludedDemo: !input.includeDemo,
  };
  mockReconciles = [row, ...mockReconciles];
  return row;
}

export async function getBizDocs(q: FinanceListQuery = {}): Promise<PagedResultDto<FinBizDocDto>> {
  if (!useMock()) return liveGet(API.bizDoc, q as Record<string, unknown>);
  let list = [...mockBizDocs];
  if (q.agentId) list = list.filter((x) => x.agentId === q.agentId);
  if (q.isDemo === true) list = list.filter((x) => x.isDemo);
  if (q.isDemo === false) list = list.filter((x) => !x.isDemo);
  if (q.filter) {
    const f = q.filter.toLowerCase();
    list = list.filter((x) => x.bizRefId.toLowerCase().includes(f) || x.bizType.includes(f));
  }
  return pageOf(list, q);
}

export async function getBalanceProjections(
  q: FinanceListQuery = {},
): Promise<PagedResultDto<FinBalanceProjectionDto>> {
  if (!useMock()) return liveGet(API.balanceProjection, q as Record<string, unknown>);
  let list = [...mockBalances];
  if (q.agentId) list = list.filter((x) => x.agentId === q.agentId);
  if (q.filter) {
    const f = q.filter.toLowerCase();
    list = list.filter((x) => (x.ownerName || '').toLowerCase().includes(f) || x.ownerId.includes(f));
  }
  return pageOf(list, q);
}
