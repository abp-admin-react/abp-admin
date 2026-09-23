// @ts-ignore
/* eslint-disable */
import { request } from "@umijs/max";

/** 此处后端没有提供注释 DELETE /api/app/identity-user-delegation/${param0} */
export async function deleteApiAppIdentityUserDelegationId(
  // 叠加生成的Param类型 (非body参数swagger默认没有生成对象)
  params: API.deleteApiAppIdentityUserDelegationIdParams,
  options?: { [key: string]: any }
) {
  const { id: param0, ...queryParams } = params;
  return request<any>(`/api/app/identity-user-delegation/${param0}`, {
    method: "DELETE",
    params: { ...queryParams },
    ...(options || {}),
  });
}

/** 此处后端没有提供注释 POST /api/app/identity-user-delegation/${param0}/start */
export async function postApiAppIdentityUserDelegationIdStart(
  // 叠加生成的Param类型 (非body参数swagger默认没有生成对象)
  params: API.postApiAppIdentityUserDelegationIdStartParams,
  options?: { [key: string]: any }
) {
  const { id: param0, ...queryParams } = params;
  return request<API.ImpersonationResultDto>(
    `/api/app/identity-user-delegation/${param0}/start`,
    {
      method: "POST",
      params: { ...queryParams },
      ...(options || {}),
    }
  );
}

/** 此处后端没有提供注释 POST /api/app/identity-user-delegation/delegate */
export async function postApiAppIdentityUserDelegationDelegate(
  body: API.CreateUserDelegationInput,
  options?: { [key: string]: any }
) {
  return request<API.IdentityUserDelegationDto>(
    "/api/app/identity-user-delegation/delegate",
    {
      method: "POST",
      headers: {
        "Content-Type": "application/json",
      },
      data: body,
      ...(options || {}),
    }
  );
}

/** 此处后端没有提供注释 GET /api/app/identity-user-delegation/delegated-to-me */
export async function getApiAppIdentityUserDelegationDelegatedToMe(options?: {
  [key: string]: any;
}) {
  return request<API.IdentityUserDelegationDto[]>(
    "/api/app/identity-user-delegation/delegated-to-me",
    {
      method: "GET",
      ...(options || {}),
    }
  );
}

/** 此处后端没有提供注释 GET /api/app/identity-user-delegation/delegated-to-others */
export async function getApiAppIdentityUserDelegationDelegatedToOthers(options?: {
  [key: string]: any;
}) {
  return request<API.IdentityUserDelegationDto[]>(
    "/api/app/identity-user-delegation/delegated-to-others",
    {
      method: "GET",
      ...(options || {}),
    }
  );
}
