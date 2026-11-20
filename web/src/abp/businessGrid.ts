import type { BalanceAdjustmentDto } from './balanceAdjustments';
import type {
  DepositOrderListDto,
  SweepTaskListDto,
  VaultCheckListDto,
} from './chainAdmin';
import type {
  BalanceTxnDto,
  CollectionPaymentAdminDto,
} from './collectionAdmin';
import type { FinBalanceProjectionDto, FinBizDocDto } from './finance';
import { type GridQuery, postGridQuery } from './gridQuery';
import type { PagedResult } from './identity';
import type { OrderDto } from './ordering';
import type { HotWalletTxnDto, WithdrawOrderDto } from './withdrawal';

export function gridScope(
  params: Record<string, string | boolean | number | null | undefined>,
) {
  const search = new URLSearchParams();
  for (const [key, value] of Object.entries(params)) {
    if (value != null && value !== '') search.set(key, String(value));
  }
  return search.size ? `?${search.toString()}` : '';
}

function resource<T>(path: string) {
  return (
    input: GridQuery,
    signal: AbortSignal,
    scope = '',
  ): Promise<PagedResult<T>> =>
    postGridQuery<T>(`/api/app/${path}/query${scope}`, input, signal);
}

export type LedgerBalanceGridRow = Pick<
  FinBalanceProjectionDto,
  'id' | 'ownerName' | 'ownerId' | 'currency'
> & {
  available: string;
  frozen: string;
  prepayment: string;
  ledgerDerived: string;
};

function ledgerResource<T>(path: string, demo: 'balances' | 'bizDocs') {
  const read = resource<T>(path);
  return async (input: GridQuery, signal: AbortSignal, scope = '') => {
    const { isMock, demoLedgerLists } = await import('./finance');
    if (isMock()) return demoLedgerLists[demo](input, scope) as PagedResult<T>;
    return read(input, signal, scope);
  };
}

export const businessGridQueries = {
  deposits: resource<DepositOrderListDto>(
    'collection-network-admin/deposit-orders',
  ),
  sweeps: resource<SweepTaskListDto>('collection-network-admin/sweep-tasks'),
  vault: resource<VaultCheckListDto>('collection-network-admin/vault-checks'),
  withdrawals: resource<WithdrawOrderDto>(
    'collection-withdraw-admin/withdrawals',
  ),
  hotWallet: resource<HotWalletTxnDto>(
    'collection-withdraw-admin/hot-wallet-txns',
  ),
  payments: resource<CollectionPaymentAdminDto>('collection-payment-admin'),
  balanceAdjustments: resource<BalanceAdjustmentDto>(
    'collection-balance-adjustment',
  ),
  memberTxns: resource<BalanceTxnDto>('collection-balance-admin/member-txns'),
  myTxns: resource<BalanceTxnDto>('collection-balance/my-txns'),
  orders: resource<OrderDto>('order-admin'),
  myOrders: resource<OrderDto>('order'),
  balances: ledgerResource<LedgerBalanceGridRow>(
    'fin-ledger/balance-projections',
    'balances',
  ),
  bizDocs: ledgerResource<FinBizDocDto>('fin-biz-doc', 'bizDocs'),
};
