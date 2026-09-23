// @ts-ignore
/* eslint-disable */
import { request } from "@umijs/max";

/** 此处后端没有提供注释 GET /api/app/gdpr-request */
export async function getApiAppGdprRequest(
  // 叠加生成的Param类型 (非body参数swagger默认没有生成对象)
  params: API.getApiAppGdprRequestParams,
  options?: { [key: string]: any }
) {
  return request<API.PagedResultDto1GdprRequestDto>("/api/app/gdpr-request", {
    method: "GET",
    params: {
      ...params,
    },
    ...(options || {}),
  });
}

/** 此处后端没有提供注释 POST /api/app/gdpr-request */
export async function postApiAppGdprRequest(options?: { [key: string]: any }) {
  return request<API.GdprRequestDto>("/api/app/gdpr-request", {
    method: "POST",
    ...(options || {}),
  });
}

/** 此处后端没有提供注释 GET /api/app/gdpr-request/download-token/${param0} */
export async function getApiAppGdprRequestDownloadTokenRequestId(
  // 叠加生成的Param类型 (非body参数swagger默认没有生成对象)
  params: API.getApiAppGdprRequestDownloadTokenRequestIdParams,
  options?: { [key: string]: any }
) {
  const { requestId: param0, ...queryParams } = params;
  return request<string>(`/api/app/gdpr-request/download-token/${param0}`, {
    method: "GET",
    params: { ...queryParams },
    ...(options || {}),
  });
}

/** 此处后端没有提供注释 POST /api/app/gdpr-request/is-new-request-allowed */
export async function postApiAppGdprRequestIsNewRequestAllowed(options?: {
  [key: string]: any;
}) {
  return request<boolean>("/api/app/gdpr-request/is-new-request-allowed", {
    method: "POST",
    ...(options || {}),
  });
}
