// @ts-ignore
/* eslint-disable */
import { request } from "@umijs/max";

/** 此处后端没有提供注释 GET /api/app/claim-type */
export async function getApiAppClaimType(
  // 叠加生成的Param类型 (非body参数swagger默认没有生成对象)
  params: API.getApiAppClaimTypeParams,
  options?: { [key: string]: any }
) {
  return request<API.PagedResultDto1ClaimTypeDto>("/api/app/claim-type", {
    method: "GET",
    params: {
      ...params,
    },
    ...(options || {}),
  });
}

/** 此处后端没有提供注释 POST /api/app/claim-type */
export async function postApiAppClaimType(
  body: API.CreateClaimTypeDto,
  options?: { [key: string]: any }
) {
  return request<API.ClaimTypeDto>("/api/app/claim-type", {
    method: "POST",
    headers: {
      "Content-Type": "application/json",
    },
    data: body,
    ...(options || {}),
  });
}

/** 此处后端没有提供注释 PUT /api/app/claim-type/${param0} */
export async function putApiAppClaimTypeId(
  // 叠加生成的Param类型 (非body参数swagger默认没有生成对象)
  params: API.putApiAppClaimTypeIdParams,
  body: API.UpdateClaimTypeDto,
  options?: { [key: string]: any }
) {
  const { id: param0, ...queryParams } = params;
  return request<API.ClaimTypeDto>(`/api/app/claim-type/${param0}`, {
    method: "PUT",
    headers: {
      "Content-Type": "application/json",
    },
    params: { ...queryParams },
    data: body,
    ...(options || {}),
  });
}

/** 此处后端没有提供注释 DELETE /api/app/claim-type/${param0} */
export async function deleteApiAppClaimTypeId(
  // 叠加生成的Param类型 (非body参数swagger默认没有生成对象)
  params: API.deleteApiAppClaimTypeIdParams,
  options?: { [key: string]: any }
) {
  const { id: param0, ...queryParams } = params;
  return request<any>(`/api/app/claim-type/${param0}`, {
    method: "DELETE",
    params: { ...queryParams },
    ...(options || {}),
  });
}

/** 此处后端没有提供注释 GET /api/app/claim-type/lookup */
export async function getApiAppClaimTypeLookup(options?: {
  [key: string]: any;
}) {
  return request<API.ClaimTypeDto[]>("/api/app/claim-type/lookup", {
    method: "GET",
    ...(options || {}),
  });
}
