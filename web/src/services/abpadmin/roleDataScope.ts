// @ts-ignore
/* eslint-disable */
import { request } from "@umijs/max";

/** 此处后端没有提供注释 GET /api/app/role-data-scope */
export async function getApiAppRoleDataScope(
  // 叠加生成的Param类型 (非body参数swagger默认没有生成对象)
  params: API.getApiAppRoleDataScopeParams,
  options?: { [key: string]: any }
) {
  return request<API.PagedResultDto1RoleDataScopeDto>(
    "/api/app/role-data-scope",
    {
      method: "GET",
      params: {
        ...params,
      },
      ...(options || {}),
    }
  );
}

/** 此处后端没有提供注释 POST /api/app/role-data-scope */
export async function postApiAppRoleDataScope(
  body: API.CreateUpdateRoleDataScopeDto,
  options?: { [key: string]: any }
) {
  return request<API.RoleDataScopeDto>("/api/app/role-data-scope", {
    method: "POST",
    headers: {
      "Content-Type": "application/json",
    },
    data: body,
    ...(options || {}),
  });
}

/** 此处后端没有提供注释 GET /api/app/role-data-scope/${param0} */
export async function getApiAppRoleDataScopeId(
  // 叠加生成的Param类型 (非body参数swagger默认没有生成对象)
  params: API.getApiAppRoleDataScopeIdParams,
  options?: { [key: string]: any }
) {
  const { id: param0, ...queryParams } = params;
  return request<API.RoleDataScopeDto>(`/api/app/role-data-scope/${param0}`, {
    method: "GET",
    params: { ...queryParams },
    ...(options || {}),
  });
}

/** 此处后端没有提供注释 PUT /api/app/role-data-scope/${param0} */
export async function putApiAppRoleDataScopeId(
  // 叠加生成的Param类型 (非body参数swagger默认没有生成对象)
  params: API.putApiAppRoleDataScopeIdParams,
  body: API.CreateUpdateRoleDataScopeDto,
  options?: { [key: string]: any }
) {
  const { id: param0, ...queryParams } = params;
  return request<API.RoleDataScopeDto>(`/api/app/role-data-scope/${param0}`, {
    method: "PUT",
    headers: {
      "Content-Type": "application/json",
    },
    params: { ...queryParams },
    data: body,
    ...(options || {}),
  });
}

/** 此处后端没有提供注释 DELETE /api/app/role-data-scope/${param0} */
export async function deleteApiAppRoleDataScopeId(
  // 叠加生成的Param类型 (非body参数swagger默认没有生成对象)
  params: API.deleteApiAppRoleDataScopeIdParams,
  options?: { [key: string]: any }
) {
  const { id: param0, ...queryParams } = params;
  return request<any>(`/api/app/role-data-scope/${param0}`, {
    method: "DELETE",
    params: { ...queryParams },
    ...(options || {}),
  });
}

/** 此处后端没有提供注释 GET /api/app/role-data-scope/by-role-name */
export async function getApiAppRoleDataScopeByRoleName(
  // 叠加生成的Param类型 (非body参数swagger默认没有生成对象)
  params: API.getApiAppRoleDataScopeByRoleNameParams,
  options?: { [key: string]: any }
) {
  return request<API.RoleDataScopeDto>(
    "/api/app/role-data-scope/by-role-name",
    {
      method: "GET",
      params: {
        ...params,
      },
      ...(options || {}),
    }
  );
}
