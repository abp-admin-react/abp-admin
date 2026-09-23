// @ts-ignore
/* eslint-disable */
import { request } from "@umijs/max";

/** 此处后端没有提供注释 GET /api/identity/roles */
export async function getApiIdentityRoles(
  // 叠加生成的Param类型 (非body参数swagger默认没有生成对象)
  params: API.getApiIdentityRolesParams,
  options?: { [key: string]: any }
) {
  return request<API.PagedResultDto1IdentityRoleDto>("/api/identity/roles", {
    method: "GET",
    params: {
      ...params,
      ExtraProperties: undefined,
      ...params["ExtraProperties"],
    },
    ...(options || {}),
  });
}

/** 此处后端没有提供注释 POST /api/identity/roles */
export async function postApiIdentityRoles(
  body: API.IdentityRoleCreateDto,
  options?: { [key: string]: any }
) {
  return request<API.IdentityRoleDto>("/api/identity/roles", {
    method: "POST",
    headers: {
      "Content-Type": "application/json",
    },
    data: body,
    ...(options || {}),
  });
}

/** 此处后端没有提供注释 GET /api/identity/roles/${param0} */
export async function getApiIdentityRolesId(
  // 叠加生成的Param类型 (非body参数swagger默认没有生成对象)
  params: API.getApiIdentityRolesIdParams,
  options?: { [key: string]: any }
) {
  const { id: param0, ...queryParams } = params;
  return request<API.IdentityRoleDto>(`/api/identity/roles/${param0}`, {
    method: "GET",
    params: { ...queryParams },
    ...(options || {}),
  });
}

/** 此处后端没有提供注释 PUT /api/identity/roles/${param0} */
export async function putApiIdentityRolesId(
  // 叠加生成的Param类型 (非body参数swagger默认没有生成对象)
  params: API.putApiIdentityRolesIdParams,
  body: API.IdentityRoleUpdateDto,
  options?: { [key: string]: any }
) {
  const { id: param0, ...queryParams } = params;
  return request<API.IdentityRoleDto>(`/api/identity/roles/${param0}`, {
    method: "PUT",
    headers: {
      "Content-Type": "application/json",
    },
    params: { ...queryParams },
    data: body,
    ...(options || {}),
  });
}

/** 此处后端没有提供注释 DELETE /api/identity/roles/${param0} */
export async function deleteApiIdentityRolesId(
  // 叠加生成的Param类型 (非body参数swagger默认没有生成对象)
  params: API.deleteApiIdentityRolesIdParams,
  options?: { [key: string]: any }
) {
  const { id: param0, ...queryParams } = params;
  return request<any>(`/api/identity/roles/${param0}`, {
    method: "DELETE",
    params: { ...queryParams },
    ...(options || {}),
  });
}

/** 此处后端没有提供注释 GET /api/identity/roles/all */
export async function getApiIdentityRolesAll(options?: { [key: string]: any }) {
  return request<API.ListResultDto1IdentityRoleDto>("/api/identity/roles/all", {
    method: "GET",
    ...(options || {}),
  });
}
