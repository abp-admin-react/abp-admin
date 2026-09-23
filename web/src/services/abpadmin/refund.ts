// @ts-ignore
/* eslint-disable */
import { request } from "@umijs/max";

/** 此处后端没有提供注释 GET /api/payment-service/refund */
export async function getApiPaymentServiceRefund(
  // 叠加生成的Param类型 (非body参数swagger默认没有生成对象)
  params: API.getApiPaymentServiceRefundParams,
  options?: { [key: string]: any }
) {
  return request<API.PagedResultDto1RefundDto>("/api/payment-service/refund", {
    method: "GET",
    params: {
      ...params,
    },
    ...(options || {}),
  });
}

/** 此处后端没有提供注释 GET /api/payment-service/refund/${param0} */
export async function getApiPaymentServiceRefundId(
  // 叠加生成的Param类型 (非body参数swagger默认没有生成对象)
  params: API.getApiPaymentServiceRefundIdParams,
  options?: { [key: string]: any }
) {
  const { id: param0, ...queryParams } = params;
  return request<API.RefundDto>(`/api/payment-service/refund/${param0}`, {
    method: "GET",
    params: { ...queryParams },
    ...(options || {}),
  });
}
