// @ts-ignore
/* eslint-disable */
import { request } from "@umijs/max";

/** 此处后端没有提供注释 GET /api/app/file-share */
export async function getApiAppFileShare(
  // 叠加生成的Param类型 (非body参数swagger默认没有生成对象)
  params: API.getApiAppFileShareParams,
  options?: { [key: string]: any }
) {
  return request<API.ListResultDto1FileShareLinkDto>("/api/app/file-share", {
    method: "GET",
    params: {
      ...params,
    },
    ...(options || {}),
  });
}

/** 此处后端没有提供注释 POST /api/app/file-share */
export async function postApiAppFileShare(
  body: API.CreateFileShareLinkInput,
  options?: { [key: string]: any }
) {
  return request<API.FileShareLinkDto>("/api/app/file-share", {
    method: "POST",
    headers: {
      "Content-Type": "application/json",
    },
    data: body,
    ...(options || {}),
  });
}

/** 此处后端没有提供注释 DELETE /api/app/file-share/${param0} */
export async function deleteApiAppFileShareId(
  // 叠加生成的Param类型 (非body参数swagger默认没有生成对象)
  params: API.deleteApiAppFileShareIdParams,
  options?: { [key: string]: any }
) {
  const { id: param0, ...queryParams } = params;
  return request<any>(`/api/app/file-share/${param0}`, {
    method: "DELETE",
    params: { ...queryParams },
    ...(options || {}),
  });
}

/** 此处后端没有提供注释 GET /api/app/file-share/by-token/${param0} */
export async function getApiAppFileShareByTokenToken(
  // 叠加生成的Param类型 (非body参数swagger默认没有生成对象)
  params: API.getApiAppFileShareByTokenTokenParams,
  options?: { [key: string]: any }
) {
  const { token: param0, ...queryParams } = params;
  return request<any>(`/api/app/file-share/by-token/${param0}`, {
    method: "GET",
    params: { ...queryParams },
    ...(options || {}),
  });
}
