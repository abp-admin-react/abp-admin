// @ts-ignore
/* eslint-disable */
import { request } from "@umijs/max";

/** 此处后端没有提供注释 GET /api/payment-service/payment */
export async function getApiPaymentServicePayment(
  // 叠加生成的Param类型 (非body参数swagger默认没有生成对象)
  params: API.getApiPaymentServicePaymentParams,
  options?: { [key: string]: any }
) {
  return request<API.PagedResultDto1PaymentDto>(
    "/api/payment-service/payment",
    {
      method: "GET",
      params: {
        ...params,
      },
      ...(options || {}),
    }
  );
}

/** 此处后端没有提供注释 GET /api/payment-service/payment/${param0} */
export async function getApiPaymentServicePaymentId(
  // 叠加生成的Param类型 (非body参数swagger默认没有生成对象)
  params: API.getApiPaymentServicePaymentIdParams,
  options?: { [key: string]: any }
) {
  const { id: param0, ...queryParams } = params;
  return request<API.PaymentDto>(`/api/payment-service/payment/${param0}`, {
    method: "GET",
    params: { ...queryParams },
    ...(options || {}),
  });
}

/** 此处后端没有提供注释 POST /api/payment-service/payment/${param0}/cancel */
export async function postApiPaymentServicePaymentIdCancel(
  // 叠加生成的Param类型 (非body参数swagger默认没有生成对象)
  params: API.postApiPaymentServicePaymentIdCancelParams,
  options?: { [key: string]: any }
) {
  const { id: param0, ...queryParams } = params;
  return request<API.PaymentDto>(
    `/api/payment-service/payment/${param0}/cancel`,
    {
      method: "POST",
      params: { ...queryParams },
      ...(options || {}),
    }
  );
}

/** 此处后端没有提供注释 POST /api/payment-service/payment/${param0}/pay */
export async function postApiPaymentServicePaymentIdPay(
  // 叠加生成的Param类型 (非body参数swagger默认没有生成对象)
  params: API.postApiPaymentServicePaymentIdPayParams,
  body: API.PayInput,
  options?: { [key: string]: any }
) {
  const { id: param0, ...queryParams } = params;
  return request<API.PaymentDto>(`/api/payment-service/payment/${param0}/pay`, {
    method: "POST",
    headers: {
      "Content-Type": "application/json",
    },
    params: { ...queryParams },
    data: body,
    ...(options || {}),
  });
}

/** 此处后端没有提供注释 POST /api/payment-service/payment/${param0}/refund/rollback */
export async function postApiPaymentServicePaymentIdRefundRollback(
  // 叠加生成的Param类型 (非body参数swagger默认没有生成对象)
  params: API.postApiPaymentServicePaymentIdRefundRollbackParams,
  options?: { [key: string]: any }
) {
  const { id: param0, ...queryParams } = params;
  return request<API.PaymentDto>(
    `/api/payment-service/payment/${param0}/refund/rollback`,
    {
      method: "POST",
      params: { ...queryParams },
      ...(options || {}),
    }
  );
}

/** 此处后端没有提供注释 GET /api/payment-service/payment/payment-method */
export async function getApiPaymentServicePaymentPaymentMethod(options?: {
  [key: string]: any;
}) {
  return request<API.ListResultDto1PaymentMethodDto>(
    "/api/payment-service/payment/payment-method",
    {
      method: "GET",
      ...(options || {}),
    }
  );
}
