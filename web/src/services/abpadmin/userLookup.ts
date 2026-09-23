// @ts-ignore
/* eslint-disable */
import { request } from "@umijs/max";

/** 此处后端没有提供注释 GET /api/identity/users/lookup/${param0} */
export async function getApiIdentityUsersLookupId(
  // 叠加生成的Param类型 (非body参数swagger默认没有生成对象)
  params: API.getApiIdentityUsersLookupIdParams,
  options?: { [key: string]: any }
) {
  const { id: param0, ...queryParams } = params;
  return request<API.UserData>(`/api/identity/users/lookup/${param0}`, {
    method: "GET",
    params: { ...queryParams },
    ...(options || {}),
  });
}

/** 此处后端没有提供注释 GET /api/identity/users/lookup/by-username/${param0} */
export async function getApiIdentityUsersLookupByUsernameUserName(
  // 叠加生成的Param类型 (非body参数swagger默认没有生成对象)
  params: API.getApiIdentityUsersLookupByUsernameUserNameParams,
  options?: { [key: string]: any }
) {
  const { userName: param0, ...queryParams } = params;
  return request<API.UserData>(
    `/api/identity/users/lookup/by-username/${param0}`,
    {
      method: "GET",
      params: { ...queryParams },
      ...(options || {}),
    }
  );
}

/** 此处后端没有提供注释 GET /api/identity/users/lookup/count */
export async function getApiIdentityUsersLookupCount(
  // 叠加生成的Param类型 (非body参数swagger默认没有生成对象)
  params: API.getApiIdentityUsersLookupCountParams,
  options?: { [key: string]: any }
) {
  return request<number>("/api/identity/users/lookup/count", {
    method: "GET",
    params: {
      ...params,
    },
    ...(options || {}),
  });
}

/** 此处后端没有提供注释 GET /api/identity/users/lookup/search */
export async function getApiIdentityUsersLookupSearch(
  // 叠加生成的Param类型 (非body参数swagger默认没有生成对象)
  params: API.getApiIdentityUsersLookupSearchParams,
  options?: { [key: string]: any }
) {
  return request<API.ListResultDto1UserData>(
    "/api/identity/users/lookup/search",
    {
      method: "GET",
      params: {
        ...params,
        ExtraProperties: undefined,
        ...params["ExtraProperties"],
      },
      ...(options || {}),
    }
  );
}
