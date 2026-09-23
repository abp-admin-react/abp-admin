// @ts-ignore
/* eslint-disable */
import { request } from "@umijs/max";

/** 此处后端没有提供注释 GET /api/payment-service/wechat-pay/refund-record */
export async function getApiPaymentServiceWechatPayRefundRecord(
  // 叠加生成的Param类型 (非body参数swagger默认没有生成对象)
  params: API.getApiPaymentServiceWechatPayRefundRecordParams,
  options?: { [key: string]: any }
) {
  return request<API.PagedResultDto1RefundRecordDto>(
    "/api/payment-service/wechat-pay/refund-record",
    {
      method: "GET",
      params: {
        ...params,
      },
      ...(options || {}),
    }
  );
}

/** 此处后端没有提供注释 GET /api/payment-service/wechat-pay/refund-record/${param0} */
export async function getApiPaymentServiceWechatPayRefundRecordId(
  // 叠加生成的Param类型 (非body参数swagger默认没有生成对象)
  params: API.getApiPaymentServiceWechatPayRefundRecordIdParams,
  options?: { [key: string]: any }
) {
  const { id: param0, ...queryParams } = params;
  return request<API.RefundRecordDto>(
    `/api/payment-service/wechat-pay/refund-record/${param0}`,
    {
      method: "GET",
      params: { ...queryParams },
      ...(options || {}),
    }
  );
}
