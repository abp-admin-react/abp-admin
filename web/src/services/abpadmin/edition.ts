// @ts-ignore
/* eslint-disable */
import { request } from "@umijs/max";

/** 此处后端没有提供注释 GET /api/app/edition */
export async function getApiAppEdition(
  // 叠加生成的Param类型 (非body参数swagger默认没有生成对象)
  params: API.getApiAppEditionParams,
  options?: { [key: string]: any }
) {
  return request<API.PagedResultDto1EditionDto>("/api/app/edition", {
    method: "GET",
    params: {
      ...params,
    },
    ...(options || {}),
  });
}

/** 此处后端没有提供注释 POST /api/app/edition */
export async function postApiAppEdition(
  body: API.CreateEditionDto,
  options?: { [key: string]: any }
) {
  return request<API.EditionDto>("/api/app/edition", {
    method: "POST",
    headers: {
      "Content-Type": "application/json",
    },
    data: body,
    ...(options || {}),
  });
}

/** 此处后端没有提供注释 PUT /api/app/edition/${param0} */
export async function putApiAppEditionId(
  // 叠加生成的Param类型 (非body参数swagger默认没有生成对象)
  params: API.putApiAppEditionIdParams,
  body: API.UpdateEditionDto,
  options?: { [key: string]: any }
) {
  const { id: param0, ...queryParams } = params;
  return request<API.EditionDto>(`/api/app/edition/${param0}`, {
    method: "PUT",
    headers: {
      "Content-Type": "application/json",
    },
    params: { ...queryParams },
    data: body,
    ...(options || {}),
  });
}

/** 此处后端没有提供注释 DELETE /api/app/edition/${param0} */
export async function deleteApiAppEditionId(
  // 叠加生成的Param类型 (非body参数swagger默认没有生成对象)
  params: API.deleteApiAppEditionIdParams,
  options?: { [key: string]: any }
) {
  const { id: param0, ...queryParams } = params;
  return request<any>(`/api/app/edition/${param0}`, {
    method: "DELETE",
    params: {
      ...queryParams,
    },
    ...(options || {}),
  });
}

/** 此处后端没有提供注释 GET /api/app/edition/${param0}/tenant-count */
export async function getApiAppEditionIdTenantCount(
  // 叠加生成的Param类型 (非body参数swagger默认没有生成对象)
  params: API.getApiAppEditionIdTenantCountParams,
  options?: { [key: string]: any }
) {
  const { id: param0, ...queryParams } = params;
  return request<number>(`/api/app/edition/${param0}/tenant-count`, {
    method: "GET",
    params: { ...queryParams },
    ...(options || {}),
  });
}

/** 此处后端没有提供注释 GET /api/app/edition/lookup */
export async function getApiAppEditionLookup(options?: { [key: string]: any }) {
  return request<API.ListResultDto1EditionDto>("/api/app/edition/lookup", {
    method: "GET",
    ...(options || {}),
  });
}

/** 此处后端没有提供注释 POST /api/app/edition/set-tenant-edition */
export async function postApiAppEditionSetTenantEdition(
  // 叠加生成的Param类型 (非body参数swagger默认没有生成对象)
  params: API.postApiAppEditionSetTenantEditionParams,
  options?: { [key: string]: any }
) {
  return request<any>("/api/app/edition/set-tenant-edition", {
    method: "POST",
    params: {
      ...params,
    },
    ...(options || {}),
  });
}
