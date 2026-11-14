import { getBalanceAdjustments } from '@/abp/balanceAdjustments';
import {
  depositStatusLabel,
  getDepositOrders,
  getSweepTasks,
} from '@/abp/chainAdmin';
import { COLLECTION_CHANNELS, COLLECTION_STATUSES } from '@/abp/collection';
import { getCollectionPayments } from '@/abp/collectionAdmin';
import { adminGetOrders, currencyLabel, getMyOrders } from '@/abp/ordering';
import {
  getWithdrawals,
  getWithdrawStatus,
  WD_STATUS_LABELS,
} from '@/abp/withdrawal';

export const recentRecordLimit = 8;
const recentPage = { skipCount: 0, maxResultCount: recentRecordLimit };
const firstMatch = { skipCount: 0, maxResultCount: 1 };

export type OperationAccess = {
  canViewOrders?: boolean;
  canViewAllOrders?: boolean;
  canViewCollectionPayments?: boolean;
  canViewWeb3Operations?: boolean;
  canViewCollections?: boolean;
  canAdminNetwork?: boolean;
  canAdminWithdrawals?: boolean;
  canViewBalanceAdjustments?: boolean;
};

export type RecentRecord = {
  id: string;
  label: string;
  detail?: string;
  amount?: string;
  status: string;
  createdAt: string;
  path: string;
};

const paths = {
  orders: '/collection/orders',
  allOrders: '/collection/orders?view=all',
  payments: '/operations/payments',
  deposits: '/web3/operations?view=deposits',
  withdrawals: '/web3/operations?view=withdrawals',
  sweeps: '/web3/operations?view=sweeps',
  adjustments: '/finance/balance-adjustments',
};

const orderStatus: Record<string, string> = {
  PendingPayment: '待支付',
  Paid: '已支付',
  Closed: '已关闭',
  Fulfilled: '已履约',
};
const adjustmentStatus = ['待审批', '已通过', '已拒绝'];
const adjustmentCurrency: Record<number, string> = { 3: 'USDT', 4: 'USDC' };

function amountLabel(amount: string | null | undefined, code?: string) {
  if (amount == null) return undefined;
  return [amount, code].filter(Boolean).join(' ');
}

export function recentSources(access: OperationAccess) {
  const showAllOrders = access.canViewAllOrders;
  const orderPath = showAllOrders ? paths.allOrders : paths.orders;
  return [
    {
      key: showAllOrders ? 'all-orders' : 'my-orders',
      label: showAllOrders ? '订单' : '我的订单',
      path: orderPath,
      allowed: showAllOrders || access.canViewOrders,
      load: async (): Promise<RecentRecord[]> => {
        const { items } = await (showAllOrders
          ? adminGetOrders(recentPage)
          : getMyOrders(recentPage));
        return items.map((row) => ({
          id: row.id,
          label: row.orderNo,
          detail: row.subject,
          amount:
            amountLabel(row.amount, currencyLabel(row.currency)) ??
            amountLabel(row.payAmount, currencyLabel(row.payCurrency)),
          status: orderStatus[row.status] ?? row.status,
          createdAt: row.creationTime,
          path: `${orderPath}${showAllOrders ? '&' : '?'}orderId=${encodeURIComponent(row.id)}`,
        }));
      },
    },
    {
      key: 'payments',
      label: '收款',
      path: paths.payments,
      allowed: access.canViewCollectionPayments,
      load: async (): Promise<RecentRecord[]> => {
        const { items } = await getCollectionPayments(recentPage);
        return items.map((row) => ({
          id: row.id,
          label: row.collectionNo,
          detail:
            COLLECTION_CHANNELS.find(({ value }) => value === row.channel)
              ?.label ?? row.channel,
          amount: amountLabel(row.amount, currencyLabel(row.currency)),
          status: row.channelResultUnknown
            ? '渠道结果待核实'
            : (COLLECTION_STATUSES.find(({ value }) => value === row.status)
                ?.label ?? row.status),
          createdAt: row.creationTime,
          path: `${paths.payments}?paymentId=${encodeURIComponent(row.id)}`,
        }));
      },
    },
    {
      key: 'deposits',
      label: '链上充值',
      path: paths.deposits,
      allowed:
        access.canViewWeb3Operations &&
        (access.canViewCollections || access.canAdminNetwork),
      load: async (): Promise<RecentRecord[]> => {
        const { items } = await getDepositOrders(recentPage);
        return items.map((row) => ({
          id: row.id,
          label: `${row.networkName} ${row.symbol} 充值`,
          detail: row.txHash,
          amount: amountLabel(row.amount, row.symbol),
          status: depositStatusLabel(row).text,
          createdAt: row.creationTime,
          path: `${paths.deposits}&depositId=${encodeURIComponent(row.id)}`,
        }));
      },
    },
    {
      key: 'withdrawals',
      label: '链上提现',
      path: `${paths.withdrawals}&status=all`,
      allowed: access.canViewWeb3Operations && access.canAdminWithdrawals,
      load: async (): Promise<RecentRecord[]> => {
        const { items } = await getWithdrawals(recentPage);
        return items.map((row) => ({
          id: row.id,
          label: `${row.networkName} ${currencyLabel(row.currency)} 提现`,
          detail: row.id,
          amount: amountLabel(row.amount, currencyLabel(row.currency)),
          status: WD_STATUS_LABELS[row.status]?.text ?? row.status,
          createdAt: row.creationTime,
          path: `${paths.withdrawals}&status=all&withdrawalId=${encodeURIComponent(row.id)}`,
        }));
      },
    },
    {
      key: 'adjustments',
      label: '余额调账',
      path: `${paths.adjustments}?status=all`,
      allowed: access.canViewBalanceAdjustments,
      load: async (): Promise<RecentRecord[]> => {
        const { items } = await getBalanceAdjustments(recentPage);
        return items.map((row) => ({
          id: row.id,
          label: '余额调账申请',
          detail: row.reason,
          amount: amountLabel(row.amount, adjustmentCurrency[row.currency]),
          status: adjustmentStatus[row.status] ?? String(row.status),
          createdAt: row.creationTime,
          path: `${paths.adjustments}?status=all&adjustmentId=${encodeURIComponent(row.id)}`,
        }));
      },
    },
  ].filter(({ allowed }) => allowed);
}

export function taskSources(access: OperationAccess) {
  return [
    {
      key: 'withdrawals',
      label: '提现',
      allowed: access.canViewWeb3Operations && access.canAdminWithdrawals,
      alerts: [
        { label: '待审核提现', path: `${paths.withdrawals}&status=Pending` },
        {
          label: '打款失败',
          path: `${paths.withdrawals}&status=BroadcastFailed`,
        },
      ],
      load: async () => {
        const status = await getWithdrawStatus();
        return [status.pendingCount, status.broadcastFailedCount];
      },
    },
    {
      key: 'adjustments',
      label: '余额调账',
      allowed: access.canViewBalanceAdjustments,
      alerts: [
        { label: '待审批调账', path: `${paths.adjustments}?status=Pending` },
      ],
      load: async () => [
        (await getBalanceAdjustments({ ...firstMatch, status: 0 })).totalCount,
      ],
    },
    {
      key: 'sweeps',
      label: '归集',
      allowed:
        access.canViewWeb3Operations &&
        (access.canViewCollections || access.canAdminNetwork),
      alerts: [{ label: '归集失败', path: `${paths.sweeps}&status=Failed` }],
      load: async () => [
        (await getSweepTasks({ ...firstMatch, status: 'Failed' })).totalCount,
      ],
    },
    {
      key: 'orders',
      label: '订单',
      allowed: access.canViewAllOrders,
      alerts: [
        { label: '有异常的订单', path: `${paths.allOrders}&hasAnomaly=true` },
      ],
      load: async () => [
        (await adminGetOrders({ ...firstMatch, hasAnomaly: true })).totalCount,
      ],
    },
  ].filter(({ allowed }) => allowed);
}
