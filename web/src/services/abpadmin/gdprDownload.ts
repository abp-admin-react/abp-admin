// @ts-ignore
/* eslint-disable */
import { request } from "@umijs/max";

/** 此处后端没有提供注释 GET /api/app/gdpr-request/download */
export async function getApiAppGdprRequestDownload(
  // 叠加生成的Param类型 (非body参数swagger默认没有生成对象)
  params: API.getApiAppGdprRequestDownloadParams,
  options?: { [key: string]: any }
) {
  return request<any>("/api/app/gdpr-request/download", {
    method: "GET",
    params: {
      ...params,
    },
    ...(options || {}),
  });
}
