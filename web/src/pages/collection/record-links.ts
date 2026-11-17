export function orderRecordUrl(orderId: string) {
  return `/collection/orders?${new URLSearchParams({ orderId })}`;
}

export function topUpOrderRecordsUrl(userId: string, balanceTxnId: string) {
  return `/collection/orders?${new URLSearchParams({ orderType: 'TopUp', userId, balanceTxnId })}`;
}

export function paymentRecordUrl(paymentId: string) {
  return `/operations/payments?${new URLSearchParams({ paymentId })}`;
}

export function depositRecordUrl(depositId: string) {
  return `/operations/payments?${new URLSearchParams({ view: 'deposits', depositId })}`;
}

export function memberFundsUrl(userId: string, txnId?: string) {
  return `/collection/member-funds?${new URLSearchParams(txnId ? { userId, txnId } : { userId })}`;
}

export function withdrawalFundsUrl(userId: string, withdrawalId: string, txnId?: string) {
  const query = new URLSearchParams({ userId, withdrawalId });
  if (txnId) query.set('txnId', txnId);
  return `/collection/member-funds?${query}`;
}

export function withdrawalRecordUrl(withdrawalId: string) {
  return `/collection/withdrawals?${new URLSearchParams({ withdrawalId })}`;
}

export function sweepDepositUrl(depositId: string) {
  return `/web3/operations?${new URLSearchParams({ view: 'sweeps', depositId })}`;
}

export function sweepTaskUrl(sweepTaskId: string) {
  return `/web3/operations?${new URLSearchParams({ view: 'sweeps', sweepTaskId })}`;
}

export function depositRecordsForSweepUrl(sweepTaskId: string) {
  return `/operations/payments?${new URLSearchParams({ view: 'deposits', sweepTaskId })}`;
}
