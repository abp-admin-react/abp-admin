// @ts-ignore
/* eslint-disable */
import { request } from "@umijs/max";

/** 此处后端没有提供注释 GET /api/identity/users */
export async function getApiIdentityUsers(
  // 叠加生成的Param类型 (非body参数swagger默认没有生成对象)
  params: API.getApiIdentityUsersParams,
  options?: { [key: string]: any }
) {
  return request<API.PagedResultDto1IdentityUserDto>("/api/identity/users", {
    method: "GET",
    params: {
      ...params,
      ExtraProperties: undefined,
      ...params["ExtraProperties"],
    },
    ...(options || {}),
  });
}

/** 此处后端没有提供注释 POST /api/identity/users */
export async function postApiIdentityUsers(
  body: API.IdentityUserCreateDto,
  options?: { [key: string]: any }
) {
  return request<API.IdentityUserDto>("/api/identity/users", {
    method: "POST",
    headers: {
      "Content-Type": "application/json",
    },
    data: body,
    ...(options || {}),
  });
}

/** 此处后端没有提供注释 GET /api/identity/users/${param0} */
export async function getApiIdentityUsersId(
  // 叠加生成的Param类型 (非body参数swagger默认没有生成对象)
  params: API.getApiIdentityUsersIdParams,
  options?: { [key: string]: any }
) {
  const { id: param0, ...queryParams } = params;
  return request<API.IdentityUserDto>(`/api/identity/users/${param0}`, {
    method: "GET",
    params: { ...queryParams },
    ...(options || {}),
  });
}

/** 此处后端没有提供注释 PUT /api/identity/users/${param0} */
export async function putApiIdentityUsersId(
  // 叠加生成的Param类型 (非body参数swagger默认没有生成对象)
  params: API.putApiIdentityUsersIdParams,
  body: API.IdentityUserUpdateDto,
  options?: { [key: string]: any }
) {
  const { id: param0, ...queryParams } = params;
  return request<API.IdentityUserDto>(`/api/identity/users/${param0}`, {
    method: "PUT",
    headers: {
      "Content-Type": "application/json",
    },
    params: { ...queryParams },
    data: body,
    ...(options || {}),
  });
}

/** 此处后端没有提供注释 DELETE /api/identity/users/${param0} */
export async function deleteApiIdentityUsersId(
  // 叠加生成的Param类型 (非body参数swagger默认没有生成对象)
  params: API.deleteApiIdentityUsersIdParams,
  options?: { [key: string]: any }
) {
  const { id: param0, ...queryParams } = params;
  return request<any>(`/api/identity/users/${param0}`, {
    method: "DELETE",
    params: { ...queryParams },
    ...(options || {}),
  });
}

/** 此处后端没有提供注释 GET /api/identity/users/${param0}/roles */
export async function getApiIdentityUsersIdRoles(
  // 叠加生成的Param类型 (非body参数swagger默认没有生成对象)
  params: API.getApiIdentityUsersIdRolesParams,
  options?: { [key: string]: any }
) {
  const { id: param0, ...queryParams } = params;
  return request<API.ListResultDto1IdentityRoleDto>(
    `/api/identity/users/${param0}/roles`,
    {
      method: "GET",
      params: { ...queryParams },
      ...(options || {}),
    }
  );
}

/** 此处后端没有提供注释 PUT /api/identity/users/${param0}/roles */
export async function putApiIdentityUsersIdRoles(
  // 叠加生成的Param类型 (非body参数swagger默认没有生成对象)
  params: API.putApiIdentityUsersIdRolesParams,
  body: API.IdentityUserUpdateRolesDto,
  options?: { [key: string]: any }
) {
  const { id: param0, ...queryParams } = params;
  return request<any>(`/api/identity/users/${param0}/roles`, {
    method: "PUT",
    headers: {
      "Content-Type": "application/json",
    },
    params: { ...queryParams },
    data: body,
    ...(options || {}),
  });
}

/** 此处后端没有提供注释 GET /api/identity/users/assignable-roles */
export async function getApiIdentityUsersAssignableRoles(options?: {
  [key: string]: any;
}) {
  return request<API.ListResultDto1IdentityRoleDto>(
    "/api/identity/users/assignable-roles",
    {
      method: "GET",
      ...(options || {}),
    }
  );
}

/** 此处后端没有提供注释 GET /api/identity/users/by-email/${param0} */
export async function getApiIdentityUsersByEmailEmail(
  // 叠加生成的Param类型 (非body参数swagger默认没有生成对象)
  params: API.getApiIdentityUsersByEmailEmailParams,
  options?: { [key: string]: any }
) {
  const { email: param0, ...queryParams } = params;
  return request<API.IdentityUserDto>(
    `/api/identity/users/by-email/${param0}`,
    {
      method: "GET",
      params: { ...queryParams },
      ...(options || {}),
    }
  );
}

/** 此处后端没有提供注释 GET /api/identity/users/by-id/${param0} */
export async function getApiIdentityUsersByIdId(
  // 叠加生成的Param类型 (非body参数swagger默认没有生成对象)
  params: API.getApiIdentityUsersByIdIdParams,
  options?: { [key: string]: any }
) {
  const { id: param0, ...queryParams } = params;
  return request<API.IdentityUserDto>(`/api/identity/users/by-id/${param0}`, {
    method: "GET",
    params: { ...queryParams },
    ...(options || {}),
  });
}

/** 此处后端没有提供注释 GET /api/identity/users/by-username/${param0} */
export async function getApiIdentityUsersByUsernameUserName(
  // 叠加生成的Param类型 (非body参数swagger默认没有生成对象)
  params: API.getApiIdentityUsersByUsernameUserNameParams,
  options?: { [key: string]: any }
) {
  const { userName: param0, ...queryParams } = params;
  return request<API.IdentityUserDto>(
    `/api/identity/users/by-username/${param0}`,
    {
      method: "GET",
      params: { ...queryParams },
      ...(options || {}),
    }
  );
}
