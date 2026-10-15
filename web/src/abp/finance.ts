/**
 * 财务 / Ledger 接口封装（定稿 §5）。
 *
 * ## 边界
 * - 页面只认本文件导出的函数、DTO、错误映射与权限相关辅助；换路径/错误码只改这里。
 * - 默认走真接口 `/api/app/fin-*`；不在此写死主机——走相对路径，由 proxy.ts / nginx 转到 Host。
 * - 时间：接口 DateTime 一律 UTC（带 Z 或无偏移当 UTC）；展示/筛选见 `@/utils/time`。
 *
 * ## 浏览器本地开关（非仓库配置；勿提交到 git）
 * | localStorage 键 | 取值 | 作用 | 改错/误开后果 |
 * | --- | --- | --- | --- |
 * | `abpadmin:finance.useMock` | `1` 开，其它关 | 内存假数据看壳，不打真 API | 联调时数据「假成功」；验收前必须关掉 |
 * | `abpadmin:finance.shellAccess` | `1` 开 | 前端菜单/按钮放行（access.ts）；**接口仍按后端权限 403** | 只能看壳，不能当授权；ApproveOwn / Scope.Agent 不受此键影响 |
 *
 * ## 不变量（与后端契约对齐）
 * - 错误码在 `error.code`，附加数据在 `error.data`（Code / ParentId / Id / Field…）。
 * - 科目 NotFound：认 `Finance:AccountNotFound` 与兼容态 `Finance:Account:NotFound`。
 * - 调账五态：Draft → Pending → Posted|Rejected；Posted → Reversed；术语与 ADJUST_STATUS_LABEL 一致。
 * - 自批禁止：真码 `Finance:Adjust:SelfApproveNotAllowed`（403）；Legacy 仅兼容旧 mock。
 */
import { request } from '@umijs/max';
import { STORAGE_KEYS } from './storageKeys';

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
  /** A3 凭证「业务单号」精确匹配（后端 BizRefId 等值） */
  bizRefId?: string;
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
  /** 余额方向；后端字段名 direction（FinNormalSide），normalizeAccount 统一成 normalSide */
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
  /** 借方归属——编辑表单回填用（live 归一为名称串，mock 种子可能是数字），缺省 Channel/default 会静默改写原归属 */
  debitOwnerType?: FinOwnerTypeName | number;
  debitOwnerId?: string | null;
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
 * ABP 约定前缀 `/api/app/fin-*`（F1 已确认）。相对路径，主机由代理决定。
 * - A1 GET fin-overview
 * - A2 CRUD fin-account；写操作需 `Finance.Account.Manage`
 * - A2b GET fin-posting-rule（只读）
 * - A3 fin-voucher / fin-manual-adjust；调账动作 POST `{id}/submit|approve|reject|reopen`；
 *   reject body `{ rejectReason }`（缺 reason → RejectReasonRequired）
 * - A4 GET fin-period/current
 * - A5 fin-reconcile；includeDemo 默认 false
 * - A6 fin-biz-doc；A7 fin-ledger/sub-account-balances
 * 改路径只改本表，页面不要拼字符串。
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

/**
 * 与后端 `FinanceErrorCodes` 对齐的错误码常量（取自响应 `error.code`）。
 * 展示文案走 `accountErrorText` / `financeErrorText`，页面不要直接把码弹给用户。
 * 后端若改码：先改这里再改映射函数；A2 NotFound 两种码都要继续认。
 */
export const FINANCE_ERROR = {
  /** 自批禁止（现行真码，通常 HTTP 403） */
  SelfApproveForbidden: 'Finance:Adjust:SelfApproveNotAllowed',
  /** 旧 mock / 历史码，仅兼容，新后端不应再发 */
  SelfApproveLegacy: 'Finance:Adjust:SelfApproveForbidden',
  SubmitDenied: 'Finance:Adjust:SubmitDenied',
  RejectDenied: 'Finance:Adjust:RejectDenied',
  ReverseDenied: 'Finance:Adjust:ReverseDenied',
  NotApplicant: 'Finance:Adjust:NotApplicant',
  CreateDenied: 'Finance:Adjust:CreateDenied',
  ApproveDenied: 'Finance:Adjust:ApproveDenied',
  /** 五态流转不允许的动作（如对 Posted 再 submit） */
  InvalidStatus: 'Finance:Adjust:InvalidStatus',
  ReopenDenied: 'Finance:Adjust:ReopenDenied',
  /** 驳回未带 rejectReason */
  RejectReasonRequired: 'Finance:Adjust:RejectReasonRequired',
  /** 系统科目不可改删启停；data.Code */
  AccountSystemReadOnly: 'Finance:Account:SystemReadOnly',
  /** 有分录不可删/不可改类型方向；data.Code */
  AccountHasEntries: 'Finance:Account:HasEntries',
  /** 科目编号重复；data.Code */
  AccountCodeDuplicate: 'Finance:Account:CodeDuplicate',
  /** 有子科目不可删 */
  AccountHasChildren: 'Finance:Account:HasChildren',
  /** 真源：FinanceErrorCodes.AccountNotFound = Finance:AccountNotFound（无中间 Account 段） */
  AccountNotFound: 'Finance:AccountNotFound',
  /** 兼容态；与 AccountNotFound 同等处理 */
  AccountNotFoundAlt: 'Finance:Account:NotFound',
  /** 缺必填等；看 data.Field，页面标红，不弹通用 toast */
  InvalidEvent: 'Finance:InvalidEvent',
} as const;

export const ADJUST_STATUS_LABEL: Record<AdjustStatus, { text: string; color: string }> = {
  Draft: { text: '草稿', color: 'default' },
  Pending: { text: '待批', color: 'processing' },
  Rejected: { text: '已驳回', color: 'warning' },
  Posted: { text: '已过账', color: 'success' },
  Reversed: { text: '已红冲', color: 'error' },
};

/*
 * A2 科目写规则（与 FinAccountAppService 一致，前端只做按钮可点性，后端兜底）：
 * - 系统科目（isSystem）不能改、删、停用/启用 → SystemReadOnly
 * - 有分录（hasEntries）不能删、不能改编码/类型/方向，只能改名或停用 → HasEntries
 * - 有子科目不能删 → HasChildren
 */
export function canEditAccount(a: { isSystem?: boolean }): boolean {
  return !a.isSystem;
}
export function canDeleteAccount(a: { isSystem?: boolean; hasEntries?: boolean }): boolean {
  return !a.isSystem && !a.hasEntries;
}

/** 后端未配 JsonStringEnumConverter，枚举按数字收发（FinAccountType / FinNormalSide 的序号）。 */
const ACCOUNT_TYPES: AccountType[] = ['Asset', 'Liability', 'Equity', 'Income', 'Expense'];
const NORMAL_SIDES: FinAccountDto['normalSide'][] = ['Debit', 'Credit'];
function enumName<T extends string>(v: unknown, names: T[], fallback: T): T {
  if (typeof v === 'number') return names[v] ?? fallback;
  if (typeof v === 'string' && (names as string[]).includes(v)) return v as T;
  return fallback;
}

function normalizeAccount(raw: FinAccountDto & { direction?: unknown }): FinAccountDto {
  const enabled = raw.enabled ?? raw.isActive ?? true;
  return {
    ...raw,
    type: enumName(raw.type, ACCOUNT_TYPES, 'Asset'),
    normalSide: enumName(raw.normalSide ?? raw.direction, NORMAL_SIDES, 'Debit'),
    enabled,
    isActive: raw.isActive ?? enabled,
  };
}

/** 新建/修改 body：{code,name,type,direction,parentId?}，枚举转序号 */
export type FinAccountInput = {
  code: string;
  name: string;
  type: AccountType;
  normalSide: FinAccountDto['normalSide'];
  parentId?: string | null;
};
function toLiveAccountBody(input: FinAccountInput) {
  return {
    code: input.code?.trim(),
    name: input.name?.trim(),
    type: ACCOUNT_TYPES.indexOf(input.type),
    direction: NORMAL_SIDES.indexOf(input.normalSide),
    parentId: input.parentId || null,
  };
}

/** error.data 里的值（ABP 原样带 WithData 的键，大小写都兼容） */
export function financeErrorData(error: unknown, key: string): string | undefined {
  const e = error as any;
  const d = e?.response?.data?.error?.data ?? e?.info?.data ?? {};
  const v = d[key] ?? d[key.charAt(0).toLowerCase() + key.slice(1)];
  return v == null ? undefined : String(v);
}

export type AccountOp = 'create' | 'update' | 'delete' | 'toggle';

/**
 * A2 科目错误 → 中文（不露错误码）。{code} 取 error.data.Code。
 * NotFound：data.Id 是本条不存在；data.ParentId 是上级非法——等于自己时提示「不能选自己」，
 * 后端选自己/成环也走这个码（带 ParentId），所以用 selfId 比对区分。
 * InvalidEvent 返回 null，由页面按 data.Field 在输入框下标红。
 */
export function accountErrorText(error: unknown, op: AccountOp, selfId?: string): string | null {
  const code = financeErrorCode(error);
  const c = financeErrorData(error, 'Code') ?? '';
  switch (code) {
    case FINANCE_ERROR.AccountSystemReadOnly:
      return `系统科目 ${c} 不能修改或删除`;
    case FINANCE_ERROR.AccountHasEntries:
      return op === 'delete'
        ? `科目 ${c} 已有记账记录，不能删除，可以停用`
        : `科目 ${c} 已有记账记录，不能改类型或方向`;
    case FINANCE_ERROR.AccountCodeDuplicate:
      return `科目编号 ${c} 已存在`;
    case FINANCE_ERROR.AccountHasChildren:
      return '这个科目下面还有子科目，请先删除或移走子科目';
    case FINANCE_ERROR.AccountNotFound:
    case FINANCE_ERROR.AccountNotFoundAlt: {
      const parentId = financeErrorData(error, 'ParentId');
      if (parentId) {
        return selfId && parentId.toLowerCase() === selfId.toLowerCase()
          ? '不能选自己做上级科目'
          : '上级科目不存在，请刷新后重选';
      }
      return '科目不存在，请刷新后重试';
    }
    case FINANCE_ERROR.InvalidEvent:
      return null;
    default:
      if (isFinanceForbidden(error)) return '没有科目管理权限';
      return '操作失败，请稍后重试';
  }
}

/**
 * 缺必填 → 表单字段名。两条来源：
 * - 业务校验 InvalidEvent + data.Field（code / name / type/direction）
 * - 模型绑定先拦（Code/Name 非空 string 为隐式 Required）：error.validationErrors[].members
 * direction 对应表单的 normalSide。
 */
export function accountInvalidFields(error: unknown): string[] {
  const toForm = (f: string) => {
    const k = f.trim().toLowerCase();
    if (k === 'type/direction') return ['type', 'normalSide'];
    if (k === 'direction') return ['normalSide'];
    return ['code', 'name', 'type', 'parentid'].includes(k) ? [k === 'parentid' ? 'parentId' : k] : [];
  };
  if (financeErrorCode(error) === FINANCE_ERROR.InvalidEvent) {
    return toForm(financeErrorData(error, 'Field') || '');
  }
  const errObj = (error as any)?.response?.data?.error;
  const members: string[] = Array.isArray(errObj?.validationErrors)
    ? errObj.validationErrors.flatMap((v: any) => (Array.isArray(v?.members) ? v.members.map(String) : []))
    : [];
  return Array.from(new Set(members.flatMap(toForm)));
}
export function useMock(): boolean {
  // 默认走真接口；仅 localStorage finance.useMock=1 时用假数据
  if (typeof localStorage !== 'undefined') {
    return localStorage.getItem(STORAGE_KEYS.financeUseMock) === '1';
  }
  return false;
}

/** 页面/测试可读：默认 live；仅 localStorage finance.useMock=1 时为 mock。
 *  （OCR refactor：isFinanceMock/getFinanceCurrentUserId 原为无调用方的死导出，已删；
 *  mock 判定统一走 useMock()，live 用户 id 统一走 resolveFinanceUserId(app.tsx 映射)。） */

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

function uid(prefix: string) {
  return prefix + '-' + Math.random().toString(36).slice(2, 10);
}

const mockAccounts: FinAccountDto[] = [
  { id: 'a1', code: '1002', name: '渠道资金（现金/银行）', type: 'Asset', normalSide: 'Debit', isSystem: true, enabled: true, hasEntries: true },
  { id: 'a2', code: '1122', name: '应收账款', type: 'Asset', normalSide: 'Debit', isSystem: true, enabled: true, hasEntries: false },
  { id: 'a3', code: '2205', name: '预收账款', type: 'Liability', normalSide: 'Credit', isSystem: true, enabled: true, hasEntries: true },
  { id: 'a4', code: '6002', name: '主营业务收入', type: 'Income', normalSide: 'Credit', isSystem: true, enabled: true, hasEntries: true },
  { id: 'a5', code: '6602', name: '管理费用', type: 'Expense', normalSide: 'Debit', isSystem: false, enabled: true, hasEntries: false },
  { id: 'a6', code: '3101', name: '人工调整', type: 'Equity', normalSide: 'Credit', isSystem: true, enabled: true, hasEntries: false },
];

const mockRules: FinPostingRuleDto[] = [
  { id: 'r1', bizType: 'demo.credit', phase: 'pay', version: 1, status: 'Active', debitAccountCode: '1002', creditAccountCode: '2205', summary: '收款：借渠道资金 / 贷预收账款' },
  { id: 'r2', bizType: 'demo.credit', phase: 'recognize', version: 1, status: 'Active', debitAccountCode: '2205', creditAccountCode: '6002', summary: '确认收入：借预收账款 / 贷主营业务收入' },
  { id: 'r3', bizType: 'manual-adjust', phase: 'post', version: 2, status: 'Active', debitAccountCode: '*', creditAccountCode: '*', summary: '人工调账过账' },
];

let mockVouchers: FinVoucherDto[] = [
  {
    id: 'v1', voucherNo: 'V-202610-0001', bizType: 'demo.credit', bizRefId: 'ord-1001', phase: 'pay',
    periodLabel: '2026-10', status: 'Posted', debitTotal: 100, creditTotal: 100,
    creatorName: 'system', creationTime: '2026-10-10T08:00:00+08:00', summary: '收款',
    isDemo: true, agentId: 'ag-1',
    entries: [
      { seq: 1, accountCode: '1002', accountName: '渠道资金（现金/银行）', debit: 100, credit: 0 },
      { seq: 2, accountCode: '2205', accountName: '预收账款', debit: 0, credit: 100 },
    ],
  },
  {
    id: 'v2', voucherNo: 'V-202610-0002', bizType: 'demo.credit', bizRefId: 'ord-1001', phase: 'recognize',
    periodLabel: '2026-10', status: 'Posted', debitTotal: 30, creditTotal: 30,
    creatorName: 'system', creationTime: '2026-10-12T09:00:00+08:00', summary: '确认收入',
    isDemo: true, agentId: 'ag-1',
    entries: [
      { seq: 1, accountCode: '2205', accountName: '预收账款', debit: 30, credit: 0 },
      { seq: 2, accountCode: '6002', accountName: '主营业务收入', debit: 0, credit: 30 },
    ],
  },
];

let mockAdjusts: FinManualAdjustDto[] = [
  {
    id: 'adj-1', adjustNo: 'ADJ-001', status: 'Draft', amount: 50, currency: 'CNY',
    summary: '手续费补记', applicantId: 'user-accountant', applicantName: '会计甲',
    creationTime: '2026-10-13T10:00:00+08:00',
    lines: [
      { accountCode: '6602', debit: 50, credit: 0, summary: '手续费' },
      { accountCode: '1002', debit: 0, credit: 50, summary: '银行存款' },
    ],
  },
  {
    id: 'adj-2', adjustNo: 'ADJ-002', status: 'Pending', amount: 20, currency: 'CNY',
    summary: '预收纠偏', applicantId: 'user-accountant', applicantName: '会计甲',
    creationTime: '2026-10-13T11:00:00+08:00',
    lines: [
      { accountCode: '2205', debit: 20, credit: 0 },
      { accountCode: '6002', debit: 0, credit: 20 },
    ],
  },
  {
    id: 'adj-3', adjustNo: 'ADJ-003', status: 'Pending', amount: 10, currency: 'CNY',
    summary: '老板自测单（用于验自批拦截）', applicantId: 'user-boss', applicantName: '老板',
    creationTime: '2026-10-13T12:00:00+08:00',
    lines: [
      { accountCode: '3101', debit: 10, credit: 0 },
      { accountCode: '1002', debit: 0, credit: 10 },
    ],
  },  {
    id: 'adj-4', adjustNo: 'ADJ-004', status: 'Rejected', amount: 15, currency: 'CNY',
    summary: '渠道手续费冲回（被驳回，制单人=当前演示用户）', applicantId: 'user-boss', applicantName: '老板',
    auditorId: 'user-accountant2', auditorName: '会计乙',
    rejectReason: '缺少渠道对账单附件，补齐后重新提交',
    creationTime: '2026-10-13T13:00:00+08:00',
    lines: [
      { accountCode: '1002', debit: 15, credit: 0 },
      { accountCode: '6602', debit: 0, credit: 15 },
    ],
  },
  {
    id: 'adj-5', adjustNo: 'ADJ-005', status: 'Rejected', amount: 8, currency: 'CNY',
    summary: '他人被驳回单（当前用户不可重新编辑）', applicantId: 'user-accountant', applicantName: '会计甲',
    auditorId: 'user-boss', auditorName: '老板',
    rejectReason: '金额与流水不符',
    creationTime: '2026-10-13T14:00:00+08:00',
    lines: [
      { accountCode: '6602', debit: 8, credit: 0 },
      { accountCode: '1002', debit: 0, credit: 8 },
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
  { id: 'bd-1', bizType: 'demo.credit', bizRefId: 'ord-1001', phase: 'pay', amount: 100, currency: 'CNY', status: 'Settled', agentId: 'ag-1', isDemo: true, creationTime: '2026-10-10T08:00:00+08:00', voucherId: 'v1' },
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
      return '系统科目不能修改或删除';
    case FINANCE_ERROR.AccountHasEntries:
      return '科目已有记账记录，不能删除，可以停用';
    // AccountDisableOnly 死映射已删（OCR R2 修复）：后端 FinanceErrorCodes 从不发出该码，
    // 有分录的口径统一走 AccountHasEntries——保留「看起来有兜底其实永远不会走」的分支只会误导
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
async function livePost<T>(url: string, data?: unknown, opts?: Record<string, unknown>) {
  return request<T>(url, { method: 'POST', data, ...opts });
}
async function livePut<T>(url: string, data?: unknown, opts?: Record<string, unknown>) {
  return request<T>(url, { method: 'PUT', data, ...opts });
}
async function liveDelete<T>(url: string, opts?: Record<string, unknown>) {
  return request<T>(url, { method: 'DELETE', ...opts });
}
/**
 * A2 科目写接口专用：skipErrorHandler 关掉全局「HTTP 400 + 后端原文」通知，
 * 只留页面 accountErrorText 的中文提示（产品口径：不露错误码、不出两条提示）。
 */
const ACCOUNT_REQ = { skipErrorHandler: true } as const;
/**
 * 调账写接口专用：页面 onActionError 已按错误码弹中文（如「没有调账创建权限」），
 * skipErrorHandler 关掉全局 403/400 提示，避免同一次失败弹两条。
 */
export const ADJUST_REQ = { skipErrorHandler: true } as const;

export async function getFinanceOverview(): Promise<FinOverviewDto> {
  if (!useMock()) {
    // live 字段归一（OCR E2E 修复：此前原样透传，live 返回 todayIncome/isBalanced/...，
    // UI DTO 期望 todayIn/balanced/... → 页面全 0 且永远显示「借贷不平衡」告警）
    const raw = await liveGet<Record<string, unknown>>(API.overview);
    const num = (v: unknown) => Number(v || 0);
    return {
      todayIn: num(raw.todayIncome),
      todayOut: num(raw.todayExpense),
      balanced: raw.isBalanced !== false,
      unreconciledCount: num(raw.unreconciledCount),
      availableTotal: num(raw.availableTotal),
      frozenTotal: num(raw.frozenTotal),
      prepaymentTotal: num(raw.unearnedTotal),
      recognizedIncome: num(raw.recognizedIncomeTotal),
    };
  }
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

export async function getAccount(id: string): Promise<FinAccountDto> {
  if (!useMock()) return normalizeAccount(await liveGet(API.account + '/' + id));
  const row = mockAccounts.find((x) => x.id === id);
  if (!row) abpError(FINANCE_ERROR.AccountNotFound, '科目不存在', 404);
  return row;
}

/** POST /api/app/fin-account（Finance.Account.Manage） */
export async function createAccount(input: FinAccountInput): Promise<FinAccountDto> {
  if (!useMock()) {
    return normalizeAccount(await request(API.account, { method: 'POST', data: toLiveAccountBody(input), ...ACCOUNT_REQ }));
  }
  const row: FinAccountDto = {
    id: uid('a'), code: input.code, name: input.name, type: input.type, normalSide: input.normalSide,
    parentId: input.parentId ?? null, isSystem: false, enabled: true, hasEntries: false,
  };
  mockAccounts.push(row);
  return row;
}

/** PUT /api/app/fin-account/{id}：整份 body，同 create */
export async function updateAccount(id: string, input: FinAccountInput): Promise<FinAccountDto> {
  if (!useMock()) {
    return normalizeAccount(
      await request(API.account + '/' + id, { method: 'PUT', data: toLiveAccountBody(input), ...ACCOUNT_REQ }),
    );
  }
  const i = mockAccounts.findIndex((x) => x.id === id);
  if (i < 0) abpError(FINANCE_ERROR.AccountNotFound, '科目不存在', 404);
  mockAccounts[i] = { ...mockAccounts[i], ...input, id };
  return mockAccounts[i];
}

/** POST /{id}/disable | /{id}/enable；系统科目后端拒 SystemReadOnly */
export async function setAccountActive(id: string, active: boolean): Promise<FinAccountDto> {
  if (!useMock()) {
    return normalizeAccount(
      await request(API.account + '/' + id + (active ? '/enable' : '/disable'), { method: 'POST', ...ACCOUNT_REQ }),
    );
  }
  const row = mockAccounts.find((x) => x.id === id);
  if (!row) abpError(FINANCE_ERROR.AccountNotFound, '科目不存在', 404);
  row.enabled = active;
  row.isActive = active;
  return row;
}

/** DELETE /{id}：有分录或被调账引用 → HasEntries；有子科目 → HasChildren */
export async function deleteAccount(id: string): Promise<void> {
  if (!useMock()) {
    await request(API.account + '/' + id, { method: 'DELETE', ...ACCOUNT_REQ });
    return;
  }
  const row = mockAccounts.find((x) => x.id === id);
  if (!row) abpError(FINANCE_ERROR.AccountNotFound, '科目不存在', 404);
  if (row.isSystem) abpError(FINANCE_ERROR.AccountSystemReadOnly, '系统科目不可删');
  if (row.hasEntries) abpError(FINANCE_ERROR.AccountHasEntries, '有分录的科目不可删，只能停用');
  const idx = mockAccounts.findIndex((x) => x.id === id);
  mockAccounts.splice(idx, 1);
}

export async function getPostingRules(q: FinanceListQuery = {}): Promise<PagedResultDto<FinPostingRuleDto>> {
  if (!useMock()) return liveGet(API.postingRule, q as Record<string, unknown>);
  return pageOf(mockRules, q);
}

/** 后端 FinVoucherStatus（0=Posted,1=Reversed，数字或同名字符串）→ UI 字符串。
 *  未知值回落 'Posted'：方向是「可操作态」——红冲入口可见，对不可红冲凭证后端会拒绝兜底；
 *  后端枚举扩展时同步这里。 */
export function mapVoucherStatus(v: unknown): VoucherStatus {
  const n = typeof v === 'number' ? v : Number(v);
  if (n === 1 || v === 'Reversed') return 'Reversed';
  return 'Posted';
}

/** Live FinVoucherDto → UI shape（OCR E2E 修复：与 getFinanceOverview 同根因——原样透传导致
 *  状态恒显「已冲销」（后端枚举是数字 0/1，UI 判 !== 'Posted'）且借贷合计列空（后端字段名
 *  totalDebit/totalCredit，UI 是 debitTotal/creditTotal）。凭证号派生单一出处=后端 Map，
 *  这里只透传不再重复实现。 */
function fromLiveVoucher(raw: Record<string, unknown>): FinVoucherDto {
  const id = String(raw.id || '');
  return {
    id,
    voucherNo: (raw.voucherNo as string) ?? '',
    bizType: String(raw.bizType || ''),
    bizRefId: String(raw.bizRefId || ''),
    phase: String(raw.phase || ''),
    periodLabel: (raw.periodLabel as string) ?? '',
    status: mapVoucherStatus(raw.status),
    debitTotal: Number(raw.totalDebit ?? raw.debitTotal ?? 0),
    creditTotal: Number(raw.totalCredit ?? raw.creditTotal ?? 0),
    creationTime: String(raw.creationTime || ''),
    summary: (raw.summary as string) ?? undefined,
    isDemo: Boolean(raw.isDemo),
    agentId: (raw.agentId as string | null) ?? null,
    reversalOf: (raw.reversalOf as string | null) ?? null,
    entries: Array.isArray(raw.entries)
      ? (raw.entries as Record<string, unknown>[]).map((en) => ({
          seq: Number(en.seq ?? 0),
          accountCode: String(en.accountCode || ''),
          accountName: String(en.accountName || ''),
          ownerType: en.ownerType != null ? finOwnerTypeName(en.ownerType) : undefined,
          ownerId: en.ownerId != null ? String(en.ownerId) : undefined,
          debit: Number(en.debit ?? 0),
          credit: Number(en.credit ?? 0),
        }))
      : undefined,
  };
}

export async function getVouchers(q: FinanceListQuery = {}): Promise<PagedResultDto<FinVoucherDto>> {
  if (!useMock()) {
    const page = await liveGet<{ totalCount: number; items: Record<string, unknown>[] }>(API.voucher, q as Record<string, unknown>);
    return { totalCount: page.totalCount, items: (page.items || []).map(fromLiveVoucher) };
  }
  let list = [...mockVouchers];
  if (q.filter) {
    const f = q.filter.toLowerCase();
    list = list.filter((x) => x.voucherNo.toLowerCase().includes(f) || (x.summary || '').toLowerCase().includes(f));
  }
  if (q.bizType) list = list.filter((x) => x.bizType === q.bizType);
  if (q.bizRefId) list = list.filter((x) => x.bizRefId === q.bizRefId);
  if (q.agentId) list = list.filter((x) => x.agentId === q.agentId);
  if (q.isDemo === true) list = list.filter((x) => x.isDemo);
  if (q.isDemo === false) list = list.filter((x) => !x.isDemo);
  return pageOf(list, q);
}

export async function getVoucher(id: string): Promise<FinVoucherDto> {
  if (!useMock()) return fromLiveVoucher(await liveGet<Record<string, unknown>>(API.voucher + '/' + id));
  const row = mockVouchers.find((x) => x.id === id);
  if (!row) abpError('Finance:Voucher:NotFound', '凭证不存在', 404);
  return row;
}

export async function reverseVoucher(id: string): Promise<FinVoucherDto> {
  if (!useMock()) return fromLiveVoucher(await livePost<Record<string, unknown>>(API.voucher + '/' + id + '/reverse'));
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
/** 后端数字枚举 → 名称串（fromLiveAdjust 回填用，与 FIN_OWNER_TYPE 一一对应）。
 *  单一事实源=FIN_OWNER_TYPE 的键序，另抄一份字面量数组会漂移。 */
const FIN_OWNER_NAME_BY_CODE: FinOwnerTypeName[] = Object.keys(FIN_OWNER_TYPE) as FinOwnerTypeName[];

/** 后端 OwnerType（数字枚举或名称串）→ 表单 options 的名称串；未知回落 Channel（与历史默认一致）。 */
export function finOwnerTypeName(v: unknown): FinOwnerTypeName {
  if (typeof v === 'number' && v >= 0 && v < FIN_OWNER_NAME_BY_CODE.length) return FIN_OWNER_NAME_BY_CODE[v];
  if (typeof v === 'string' && Object.hasOwn(FIN_OWNER_TYPE, v)) return v as FinOwnerTypeName;
  return 'Channel';
}

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
/** Live FinManualAdjustDto → UI shape (lines/summary/adjustNo/status string + owner fields). */
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
    debitOwnerType: finOwnerTypeName(raw.debitOwnerType),
    debitOwnerId: raw.debitOwnerId != null ? String(raw.debitOwnerId) : 'default',
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
    const raw = await livePost<Record<string, unknown>>(API.manualAdjust, toLiveAdjustBody(input), ADJUST_REQ);
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
      ADJUST_REQ,
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
  if (!useMock()) return liveDelete(API.manualAdjust + '/' + id, ADJUST_REQ);
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
    const raw = await livePost<Record<string, unknown>>(API.manualAdjust + '/' + id + '/submit', undefined, ADJUST_REQ);
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
    const raw = await livePost<Record<string, unknown>>(API.manualAdjust + '/' + id + '/approve', undefined, ADJUST_REQ);
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
    const raw = await livePost<Record<string, unknown>>(API.manualAdjust + '/' + id + '/reject', { rejectReason }, ADJUST_REQ);
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
    const raw = await livePost<Record<string, unknown>>(API.manualAdjust + '/' + id + '/reopen', undefined, ADJUST_REQ);
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
    const raw = await livePost<Record<string, unknown>>(API.manualAdjust + '/' + id + '/reverse', undefined, ADJUST_REQ);
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
    // 页面自处理（catch 里 financeErrorText），skipErrorHandler 防同一次失败弹两条
    // ——与 ADJUST_REQ/ACCOUNT_REQ 同口径（OCR 修复：此处此前漏了）
    const raw = await livePost<Record<string, unknown>>(API.reconcileRun + '/run', { ...input, includeDemo: !!input.includeDemo }, ADJUST_REQ);
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
