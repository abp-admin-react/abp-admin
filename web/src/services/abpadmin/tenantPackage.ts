// @ts-ignore
/* eslint-disable */
import { request } from "@umijs/max";

/** 此处后端没有提供注释 GET /api/app/tenant-package */
export async function getApiAppTenantPackage(
  // 叠加生成的Param类型 (非body参数swagger默认没有生成对象)
  params: API.getApiAppTenantPackageParams,
  options?: { [key: string]: any }
) {
  return request<API.PagedResultDto1TenantPackageDto>(
    "/api/app/tenant-package",
    {
      method: "GET",
      params: {
        ...params,
      },
      ...(options || {}),
    }
  );
}

/** 此处后端没有提供注释 POST /api/app/tenant-package */
export async function postApiAppTenantPackage(
  body: API.TenantPackageCreateDto,
  options?: { [key: string]: any }
) {
  return request<API.TenantPackageDto>("/api/app/tenant-package", {
    method: "POST",
    headers: {
      "Content-Type": "application/json",
    },
    data: body,
    ...(options || {}),
  });
}

/** 此处后端没有提供注释 GET /api/app/tenant-package/${param0} */
export async function getApiAppTenantPackageId(
  // 叠加生成的Param类型 (非body参数swagger默认没有生成对象)
  params: API.getApiAppTenantPackageIdParams,
  options?: { [key: string]: any }
) {
  const { id: param0, ...queryParams } = params;
  return request<API.TenantPackageDto>(`/api/app/tenant-package/${param0}`, {
    method: "GET",
    params: { ...queryParams },
    ...(options || {}),
  });
}

/** 此处后端没有提供注释 PUT /api/app/tenant-package/${param0} */
export async function putApiAppTenantPackageId(
  // 叠加生成的Param类型 (非body参数swagger默认没有生成对象)
  params: API.putApiAppTenantPackageIdParams,
  body: API.TenantPackageUpdateDto,
  options?: { [key: string]: any }
) {
  const { id: param0, ...queryParams } = params;
  return request<API.TenantPackageDto>(`/api/app/tenant-package/${param0}`, {
    method: "PUT",
    headers: {
      "Content-Type": "application/json",
    },
    params: { ...queryParams },
    data: body,
    ...(options || {}),
  });
}

/** 此处后端没有提供注释 DELETE /api/app/tenant-package/${param0} */
export async function deleteApiAppTenantPackageId(
  // 叠加生成的Param类型 (非body参数swagger默认没有生成对象)
  params: API.deleteApiAppTenantPackageIdParams,
  options?: { [key: string]: any }
) {
  const { id: param0, ...queryParams } = params;
  return request<any>(`/api/app/tenant-package/${param0}`, {
    method: "DELETE",
    params: { ...queryParams },
    ...(options || {}),
  });
}

/** 此处后端没有提供注释 GET /api/app/tenant-package/${param0}/menu-selection */
export async function getApiAppTenantPackageIdMenuSelection(
  // 叠加生成的Param类型 (非body参数swagger默认没有生成对象)
  params: API.getApiAppTenantPackageIdMenuSelectionParams,
  options?: { [key: string]: any }
) {
  const { id: param0, ...queryParams } = params;
  return request<API.TenantPackageMenuSelectionDto>(
    `/api/app/tenant-package/${param0}/menu-selection`,
    {
      method: "GET",
      params: { ...queryParams },
      ...(options || {}),
    }
  );
}

/** 此处后端没有提供注释 PUT /api/app/tenant-package/${param0}/menu-selection */
export async function putApiAppTenantPackageIdMenuSelection(
  // 叠加生成的Param类型 (非body参数swagger默认没有生成对象)
  params: API.putApiAppTenantPackageIdMenuSelectionParams,
  body: API.UpdateTenantPackageMenusDto,
  options?: { [key: string]: any }
) {
  const { id: param0, ...queryParams } = params;
  return request<any>(`/api/app/tenant-package/${param0}/menu-selection`, {
    method: "PUT",
    headers: {
      "Content-Type": "application/json",
    },
    params: { ...queryParams },
    data: body,
    ...(options || {}),
  });
}
