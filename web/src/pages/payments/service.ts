import { request } from '@umijs/max';

export type PaymentDto = {
  id: string;
  userId?: string;
  paymentMethod?: string;
  currency?: string;
  originalPaymentAmount?: number;
  actualPaymentAmount?: number;
  refundAmount?: number;
  pendingRefundAmount?: number;
  completionTime?: string;
  canceledTime?: string;
  creationTime?: string;
  externalTradingCode?: string;
};

export type PrepaymentAccountDto = {
  id: string;
  accountGroupName?: string;
  userId?: string;
  balance?: number;
  lockedBalance?: number;
  pendingBalance?: number;
  creationTime?: string;
};

export type PrepaymentTransactionDto = {
  id: string;
  accountId?: string;
  accountUserId?: string;
  changedBalance?: number;
  originalBalance?: number;
  type?: number | string;
  creationTime?: string;
};

export async function getPayments(params: {
  current?: number;
  pageSize?: number;
}) {
  const maxResultCount = params.pageSize ?? 10;
  const skipCount = ((params.current ?? 1) - 1) * maxResultCount;
  return request<{ items: PaymentDto[]; totalCount: number }>(
    '/api/payment-service/payment',
    {
      method: 'GET',
      params: { SkipCount: skipCount, MaxResultCount: maxResultCount },
    },
  );
}

export async function getPayment(id: string) {
  return request<PaymentDto>(`/api/payment-service/payment/${id}`, {
    method: 'GET',
  });
}

export async function refundPayment(
  id: string,
  data: { refundAmount?: number; displayReason?: string },
) {
  // refundAmount 省略 = 全额退款，金额由服务端按行项剩余可退计算
  //（前端不做 decimal 运算，避免 IEEE-754 精度尾差）
  return request(`/api/app/payment-admin/${id}/refund`, {
    method: 'POST',
    data,
  });
}

export async function getPrepaymentAccounts(params: {
  current?: number;
  pageSize?: number;
}) {
  const maxResultCount = params.pageSize ?? 10;
  const skipCount = ((params.current ?? 1) - 1) * maxResultCount;
  return request<{ items: PrepaymentAccountDto[]; totalCount: number }>(
    '/api/payment-service/prepayment/account',
    {
      method: 'GET',
      params: { SkipCount: skipCount, MaxResultCount: maxResultCount },
    },
  );
}

export async function getPrepaymentTransactions(params: {
  current?: number;
  pageSize?: number;
  accountId?: string;
}) {
  const maxResultCount = params.pageSize ?? 10;
  const skipCount = ((params.current ?? 1) - 1) * maxResultCount;
  return request<{ items: PrepaymentTransactionDto[]; totalCount: number }>(
    '/api/payment-service/prepayment/transaction',
    {
      method: 'GET',
      params: {
        SkipCount: skipCount,
        MaxResultCount: maxResultCount,
        AccountId: params.accountId,
      },
    },
  );
}
