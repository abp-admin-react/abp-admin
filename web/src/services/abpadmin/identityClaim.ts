// @ts-ignore
/* eslint-disable */
import { request } from "@umijs/max";

/** 此处后端没有提供注释 GET /api/app/identity-claim/role-claims/${param0} */
export async function getApiAppIdentityClaimRoleClaimsRoleId(
  // 叠加生成的Param类型 (非body参数swagger默认没有生成对象)
  params: API.getApiAppIdentityClaimRoleClaimsRoleIdParams,
  options?: { [key: string]: any }
) {
  const { roleId: param0, ...queryParams } = params;
  return request<API.ClaimValueDto[]>(
    `/api/app/identity-claim/role-claims/${param0}`,
    {
      method: "GET",
      params: { ...queryParams },
      ...(options || {}),
    }
  );
}

/** 此处后端没有提供注释 PUT /api/app/identity-claim/role-claims/${param0} */
export async function putApiAppIdentityClaimRoleClaimsRoleId(
  // 叠加生成的Param类型 (非body参数swagger默认没有生成对象)
  params: API.putApiAppIdentityClaimRoleClaimsRoleIdParams,
  body: API.ClaimValueDto[],
  options?: { [key: string]: any }
) {
  const { roleId: param0, ...queryParams } = params;
  return request<any>(`/api/app/identity-claim/role-claims/${param0}`, {
    method: "PUT",
    headers: {
      "Content-Type": "application/json",
    },
    params: { ...queryParams },
    data: body,
    ...(options || {}),
  });
}

/** 此处后端没有提供注释 GET /api/app/identity-claim/user-claims/${param0} */
export async function getApiAppIdentityClaimUserClaimsUserId(
  // 叠加生成的Param类型 (非body参数swagger默认没有生成对象)
  params: API.getApiAppIdentityClaimUserClaimsUserIdParams,
  options?: { [key: string]: any }
) {
  const { userId: param0, ...queryParams } = params;
  return request<API.ClaimValueDto[]>(
    `/api/app/identity-claim/user-claims/${param0}`,
    {
      method: "GET",
      params: { ...queryParams },
      ...(options || {}),
    }
  );
}

/** 此处后端没有提供注释 PUT /api/app/identity-claim/user-claims/${param0} */
export async function putApiAppIdentityClaimUserClaimsUserId(
  // 叠加生成的Param类型 (非body参数swagger默认没有生成对象)
  params: API.putApiAppIdentityClaimUserClaimsUserIdParams,
  body: API.ClaimValueDto[],
  options?: { [key: string]: any }
) {
  const { userId: param0, ...queryParams } = params;
  return request<any>(`/api/app/identity-claim/user-claims/${param0}`, {
    method: "PUT",
    headers: {
      "Content-Type": "application/json",
    },
    params: { ...queryParams },
    data: body,
    ...(options || {}),
  });
}
