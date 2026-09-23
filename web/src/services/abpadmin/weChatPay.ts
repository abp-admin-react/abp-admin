// @ts-ignore
/* eslint-disable */
import { request } from "@umijs/max";

/** 此处后端没有提供注释 GET /wechat-pay/js-sdk-config-parameters */
export async function getWechatPayJsSdkConfigParameters(
  // 叠加生成的Param类型 (非body参数swagger默认没有生成对象)
  params: API.getWechatPayJsSdkConfigParametersParams,
  options?: { [key: string]: any }
) {
  return request<any>("/wechat-pay/js-sdk-config-parameters", {
    method: "GET",
    params: {
      ...params,
    },
    ...(options || {}),
  });
}

/** 此处后端没有提供注释 POST /wechat-pay/notify */
export async function postWechatPayNotify(
  // 叠加生成的Param类型 (非body参数swagger默认没有生成对象)
  params: API.postWechatPayNotifyParams,
  options?: { [key: string]: any }
) {
  return request<any>("/wechat-pay/notify", {
    method: "POST",
    params: {
      ...params,
    },
    ...(options || {}),
  });
}

/** 此处后端没有提供注释 POST /wechat-pay/notify/mch-id/${param0} */
export async function postWechatPayNotifyMchIdMchId(
  // 叠加生成的Param类型 (非body参数swagger默认没有生成对象)
  params: API.postWechatPayNotifyMchIdMchIdParams,
  options?: { [key: string]: any }
) {
  const { mchId: param0, ...queryParams } = params;
  return request<any>(`/wechat-pay/notify/mch-id/${param0}`, {
    method: "POST",
    params: {
      ...queryParams,
    },
    ...(options || {}),
  });
}

/** 此处后端没有提供注释 POST /wechat-pay/notify/tenant-id/${param0} */
export async function postWechatPayNotifyTenantIdTenantId(
  // 叠加生成的Param类型 (非body参数swagger默认没有生成对象)
  params: API.postWechatPayNotifyTenantIdTenantIdParams,
  options?: { [key: string]: any }
) {
  const { tenantId: param0, ...queryParams } = params;
  return request<any>(`/wechat-pay/notify/tenant-id/${param0}`, {
    method: "POST",
    params: {
      ...queryParams,
    },
    ...(options || {}),
  });
}

/** 此处后端没有提供注释 POST /wechat-pay/notify/tenant-id/${param0}/mch-id/${param1} */
export async function postWechatPayNotifyTenantIdTenantIdMchIdMchId(
  // 叠加生成的Param类型 (非body参数swagger默认没有生成对象)
  params: API.postWechatPayNotifyTenantIdTenantIdMchIdMchIdParams,
  options?: { [key: string]: any }
) {
  const { tenantId: param0, mchId: param1, ...queryParams } = params;
  return request<any>(
    `/wechat-pay/notify/tenant-id/${param0}/mch-id/${param1}`,
    {
      method: "POST",
      params: { ...queryParams },
      ...(options || {}),
    }
  );
}

/** 此处后端没有提供注释 POST /wechat-pay/refund-notify */
export async function postWechatPayRefundNotify(
  // 叠加生成的Param类型 (非body参数swagger默认没有生成对象)
  params: API.postWechatPayRefundNotifyParams,
  options?: { [key: string]: any }
) {
  return request<any>("/wechat-pay/refund-notify", {
    method: "POST",
    params: {
      ...params,
    },
    ...(options || {}),
  });
}

/** 此处后端没有提供注释 POST /wechat-pay/refund-notify/mch-id/${param0} */
export async function postWechatPayRefundNotifyMchIdMchId(
  // 叠加生成的Param类型 (非body参数swagger默认没有生成对象)
  params: API.postWechatPayRefundNotifyMchIdMchIdParams,
  options?: { [key: string]: any }
) {
  const { mchId: param0, ...queryParams } = params;
  return request<any>(`/wechat-pay/refund-notify/mch-id/${param0}`, {
    method: "POST",
    params: {
      ...queryParams,
    },
    ...(options || {}),
  });
}

/** 此处后端没有提供注释 POST /wechat-pay/refund-notify/tenant-id/${param0} */
export async function postWechatPayRefundNotifyTenantIdTenantId(
  // 叠加生成的Param类型 (非body参数swagger默认没有生成对象)
  params: API.postWechatPayRefundNotifyTenantIdTenantIdParams,
  options?: { [key: string]: any }
) {
  const { tenantId: param0, ...queryParams } = params;
  return request<any>(`/wechat-pay/refund-notify/tenant-id/${param0}`, {
    method: "POST",
    params: {
      ...queryParams,
    },
    ...(options || {}),
  });
}

/** 此处后端没有提供注释 POST /wechat-pay/refund-notify/tenant-id/${param0}/mch-id/${param1} */
export async function postWechatPayRefundNotifyTenantIdTenantIdMchIdMchId(
  // 叠加生成的Param类型 (非body参数swagger默认没有生成对象)
  params: API.postWechatPayRefundNotifyTenantIdTenantIdMchIdMchIdParams,
  options?: { [key: string]: any }
) {
  const { tenantId: param0, mchId: param1, ...queryParams } = params;
  return request<any>(
    `/wechat-pay/refund-notify/tenant-id/${param0}/mch-id/${param1}`,
    {
      method: "POST",
      params: { ...queryParams },
      ...(options || {}),
    }
  );
}
