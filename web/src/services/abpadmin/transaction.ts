// @ts-ignore
/* eslint-disable */
import { request } from "@umijs/max";

/** 此处后端没有提供注释 GET /api/payment-service/prepayment/transaction */
export async function getApiPaymentServicePrepaymentTransaction(
  // 叠加生成的Param类型 (非body参数swagger默认没有生成对象)
  params: API.getApiPaymentServicePrepaymentTransactionParams,
  options?: { [key: string]: any }
) {
  return request<API.PagedResultDto1TransactionDto>(
    "/api/payment-service/prepayment/transaction",
    {
      method: "GET",
      params: {
        ...params,
      },
      ...(options || {}),
    }
  );
}

/** 此处后端没有提供注释 GET /api/payment-service/prepayment/transaction/${param0} */
export async function getApiPaymentServicePrepaymentTransactionId(
  // 叠加生成的Param类型 (非body参数swagger默认没有生成对象)
  params: API.getApiPaymentServicePrepaymentTransactionIdParams,
  options?: { [key: string]: any }
) {
  const { id: param0, ...queryParams } = params;
  return request<API.TransactionDto>(
    `/api/payment-service/prepayment/transaction/${param0}`,
    {
      method: "GET",
      params: { ...queryParams },
      ...(options || {}),
    }
  );
}
