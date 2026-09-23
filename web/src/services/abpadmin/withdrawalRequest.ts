// @ts-ignore
/* eslint-disable */
import { request } from "@umijs/max";

/** 此处后端没有提供注释 GET /api/payment-service/prepayment/withdrawal-request */
export async function getApiPaymentServicePrepaymentWithdrawalRequest(
  // 叠加生成的Param类型 (非body参数swagger默认没有生成对象)
  params: API.getApiPaymentServicePrepaymentWithdrawalRequestParams,
  options?: { [key: string]: any }
) {
  return request<API.PagedResultDto1WithdrawalRequestDto>(
    "/api/payment-service/prepayment/withdrawal-request",
    {
      method: "GET",
      params: {
        ...params,
      },
      ...(options || {}),
    }
  );
}

/** 此处后端没有提供注释 GET /api/payment-service/prepayment/withdrawal-request/${param0} */
export async function getApiPaymentServicePrepaymentWithdrawalRequestId(
  // 叠加生成的Param类型 (非body参数swagger默认没有生成对象)
  params: API.getApiPaymentServicePrepaymentWithdrawalRequestIdParams,
  options?: { [key: string]: any }
) {
  const { id: param0, ...queryParams } = params;
  return request<API.WithdrawalRequestDto>(
    `/api/payment-service/prepayment/withdrawal-request/${param0}`,
    {
      method: "GET",
      params: { ...queryParams },
      ...(options || {}),
    }
  );
}

/** 此处后端没有提供注释 POST /api/payment-service/prepayment/withdrawal-request/${param0}/review */
export async function postApiPaymentServicePrepaymentWithdrawalRequestIdReview(
  // 叠加生成的Param类型 (非body参数swagger默认没有生成对象)
  params: API.postApiPaymentServicePrepaymentWithdrawalRequestIdReviewParams,
  body: API.ReviewWithdrawalRequestInput,
  options?: { [key: string]: any }
) {
  const { id: param0, ...queryParams } = params;
  return request<API.WithdrawalRequestDto>(
    `/api/payment-service/prepayment/withdrawal-request/${param0}/review`,
    {
      method: "POST",
      headers: {
        "Content-Type": "application/json",
      },
      params: { ...queryParams },
      data: body,
      ...(options || {}),
    }
  );
}
