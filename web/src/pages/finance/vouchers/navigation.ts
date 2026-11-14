export const voucherBusinessTypes: Record<string, string> = {
  payment: '订单收款',
  deposit: '链上充值',
  withdraw: '链上提现',
  wallet: '存量余额开账',
  'wallet-adjust': '用户余额调账',
  'manual-adjust': '账簿调账',
};

export function voucherDetailsUrl(voucherId: string) {
  return `/finance/vouchers?voucherId=${encodeURIComponent(voucherId)}`;
}

export function withdrawalVouchersUrl(withdrawalId: string) {
  return `/finance/vouchers?bizType=withdraw&bizRefId=${encodeURIComponent(withdrawalId)}`;
}

export function depositVouchersUrl(depositId: string) {
  return `/finance/vouchers?bizType=deposit&bizRefId=${encodeURIComponent(depositId)}`;
}

export function depositRecordUrl(depositId: string) {
  return `/web3/operations?view=deposits&depositId=${encodeURIComponent(depositId)}`;
}

export function withdrawalRecordUrl(withdrawalId: string) {
  return `/web3/operations?view=withdrawals&status=all&withdrawalId=${encodeURIComponent(withdrawalId)}`;
}

export function voucherEntriesUrl(voucherId: string) {
  return `/finance/transactions?voucherId=${encodeURIComponent(voucherId)}`;
}

export function voucherSourceUrl(bizRefId: string) {
  return `/finance/biz-docs?bizRefId=${encodeURIComponent(bizRefId)}`;
}

export function balanceAdjustmentUrl(adjustmentId: string) {
  return `/finance/balance-adjustments?adjustmentId=${encodeURIComponent(adjustmentId)}`;
}
