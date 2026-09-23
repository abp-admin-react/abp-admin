// @ts-ignore
/* eslint-disable */
import { request } from "@umijs/max";

/** 此处后端没有提供注释 POST /api/app/menu */
export async function postApiAppMenu(
  body: API.MenuCreateDto,
  options?: { [key: string]: any }
) {
  return request<API.MenuDto>("/api/app/menu", {
    method: "POST",
    headers: {
      "Content-Type": "application/json",
    },
    data: body,
    ...(options || {}),
  });
}

/** 此处后端没有提供注释 GET /api/app/menu/${param0} */
export async function getApiAppMenuId(
  // 叠加生成的Param类型 (非body参数swagger默认没有生成对象)
  params: API.getApiAppMenuIdParams,
  options?: { [key: string]: any }
) {
  const { id: param0, ...queryParams } = params;
  return request<API.MenuDto>(`/api/app/menu/${param0}`, {
    method: "GET",
    params: { ...queryParams },
    ...(options || {}),
  });
}

/** 此处后端没有提供注释 PUT /api/app/menu/${param0} */
export async function putApiAppMenuId(
  // 叠加生成的Param类型 (非body参数swagger默认没有生成对象)
  params: API.putApiAppMenuIdParams,
  body: API.MenuUpdateDto,
  options?: { [key: string]: any }
) {
  const { id: param0, ...queryParams } = params;
  return request<API.MenuDto>(`/api/app/menu/${param0}`, {
    method: "PUT",
    headers: {
      "Content-Type": "application/json",
    },
    params: { ...queryParams },
    data: body,
    ...(options || {}),
  });
}

/** 此处后端没有提供注释 DELETE /api/app/menu/${param0} */
export async function deleteApiAppMenuId(
  // 叠加生成的Param类型 (非body参数swagger默认没有生成对象)
  params: API.deleteApiAppMenuIdParams,
  options?: { [key: string]: any }
) {
  const { id: param0, ...queryParams } = params;
  return request<any>(`/api/app/menu/${param0}`, {
    method: "DELETE",
    params: { ...queryParams },
    ...(options || {}),
  });
}

/** 此处后端没有提供注释 GET /api/app/menu/permission-options */
export async function getApiAppMenuPermissionOptions(options?: {
  [key: string]: any;
}) {
  return request<API.ListResultDto1PermissionOptionDto>(
    "/api/app/menu/permission-options",
    {
      method: "GET",
      ...(options || {}),
    }
  );
}

/** 此处后端没有提供注释 GET /api/app/menu/role-grants/${param0} */
export async function getApiAppMenuRoleGrantsMenuId(
  // 叠加生成的Param类型 (非body参数swagger默认没有生成对象)
  params: API.getApiAppMenuRoleGrantsMenuIdParams,
  options?: { [key: string]: any }
) {
  const { menuId: param0, ...queryParams } = params;
  return request<API.ListResultDto1String>(
    `/api/app/menu/role-grants/${param0}`,
    {
      method: "GET",
      params: { ...queryParams },
      ...(options || {}),
    }
  );
}

/** 此处后端没有提供注释 PUT /api/app/menu/role-grants/${param0} */
export async function putApiAppMenuRoleGrantsMenuId(
  // 叠加生成的Param类型 (非body参数swagger默认没有生成对象)
  params: API.putApiAppMenuRoleGrantsMenuIdParams,
  body: API.UpdateMenuGrantsDto,
  options?: { [key: string]: any }
) {
  const { menuId: param0, ...queryParams } = params;
  return request<any>(`/api/app/menu/role-grants/${param0}`, {
    method: "PUT",
    headers: {
      "Content-Type": "application/json",
    },
    params: { ...queryParams },
    data: body,
    ...(options || {}),
  });
}

/** 此处后端没有提供注释 GET /api/app/menu/tree */
export async function getApiAppMenuTree(options?: { [key: string]: any }) {
  return request<API.ListResultDto1MenuTreeDto>("/api/app/menu/tree", {
    method: "GET",
    ...(options || {}),
  });
}
