// @ts-ignore
/* eslint-disable */
import { request } from "@umijs/max";

/** 按支付单行项的可退余额拆分退款。
input.RefundAmount 为 null 时表示全额退款（按行项剩余可退总额，服务端计算）；
指定金额时必须 > 0 且不得超过 Σ(ActualPaymentAmount - RefundAmount - PendingRefundAmount)；
超额或无可退余额则失败，不截断成部分退。
客户端可以乱填金额，服务端以库里的行项为唯一真相。
退款额分配基于读出的行项快照：并发退款（两个管理员同时操作同一单）由
abpadmin:refund:{id} 分布式锁串行化，防止双双通过"剩余可退"校验后超退。 POST /api/app/payment-admin/${param0}/refund */
export async function postApiAppPaymentAdminIdRefund(
  // 叠加生成的Param类型 (非body参数swagger默认没有生成对象)
  params: API.postApiAppPaymentAdminIdRefundParams,
  body: API.RefundPaymentInput,
  options?: { [key: string]: any }
) {
  const { id: param0, ...queryParams } = params;
  return request<any>(`/api/app/payment-admin/${param0}/refund`, {
    method: "POST",
    headers: {
      "Content-Type": "application/json",
    },
    params: { ...queryParams },
    data: body,
    ...(options || {}),
  });
}
