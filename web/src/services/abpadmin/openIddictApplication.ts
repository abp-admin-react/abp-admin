// @ts-ignore
/* eslint-disable */
import { request } from "@umijs/max";

/** 此处后端没有提供注释 GET /api/app/open-iddict-application */
export async function getApiAppOpenIddictApplication(
  // 叠加生成的Param类型 (非body参数swagger默认没有生成对象)
  params: API.getApiAppOpenIddictApplicationParams,
  options?: { [key: string]: any }
) {
  return request<API.PagedResultDto1OpenIddictApplicationDto>(
    "/api/app/open-iddict-application",
    {
      method: "GET",
      params: {
        ...params,
      },
      ...(options || {}),
    }
  );
}

/** 此处后端没有提供注释 POST /api/app/open-iddict-application */
export async function postApiAppOpenIddictApplication(
  body: API.CreateOpenIddictApplicationDto,
  options?: { [key: string]: any }
) {
  return request<API.OpenIddictApplicationDto>(
    "/api/app/open-iddict-application",
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

/** 此处后端没有提供注释 GET /api/app/open-iddict-application/${param0} */
export async function getApiAppOpenIddictApplicationId(
  // 叠加生成的Param类型 (非body参数swagger默认没有生成对象)
  params: API.getApiAppOpenIddictApplicationIdParams,
  options?: { [key: string]: any }
) {
  const { id: param0, ...queryParams } = params;
  return request<API.OpenIddictApplicationDto>(
    `/api/app/open-iddict-application/${param0}`,
    {
      method: "GET",
      params: { ...queryParams },
      ...(options || {}),
    }
  );
}

/** 此处后端没有提供注释 PUT /api/app/open-iddict-application/${param0} */
export async function putApiAppOpenIddictApplicationId(
  // 叠加生成的Param类型 (非body参数swagger默认没有生成对象)
  params: API.putApiAppOpenIddictApplicationIdParams,
  body: API.UpdateOpenIddictApplicationDto,
  options?: { [key: string]: any }
) {
  const { id: param0, ...queryParams } = params;
  return request<API.OpenIddictApplicationDto>(
    `/api/app/open-iddict-application/${param0}`,
    {
      method: "PUT",
      headers: {
        "Content-Type": "application/json",
      },
      params: { ...queryParams },
      data: body,
      ...(options || {}),
    }
  );
}

/** 此处后端没有提供注释 DELETE /api/app/open-iddict-application/${param0} */
export async function deleteApiAppOpenIddictApplicationId(
  // 叠加生成的Param类型 (非body参数swagger默认没有生成对象)
  params: API.deleteApiAppOpenIddictApplicationIdParams,
  options?: { [key: string]: any }
) {
  const { id: param0, ...queryParams } = params;
  return request<any>(`/api/app/open-iddict-application/${param0}`, {
    method: "DELETE",
    params: { ...queryParams },
    ...(options || {}),
  });
}

/** Client Credentials 代取 access token。四个条件前后端都判（缺一即拒）：
1. 当前用户有 GenerateAccessToken 权限（[Authorize] 保证）；
2. 应用是 Confidential；3. 已启用 Client Credentials flow；4. 请求的 scope 全部分配。
已存 secret 是哈希读不回来，所以 secret 必须由调用者输入；
HTTP 交换交给 AbpAdmin.OpenIddict.IOpenIddictTokenExchanger（走标准 /connect/token 端点）。 POST /api/app/open-iddict-application/${param0}/generate-access-token */
export async function postApiAppOpenIddictApplicationIdGenerateAccessToken(
  // 叠加生成的Param类型 (非body参数swagger默认没有生成对象)
  params: API.postApiAppOpenIddictApplicationIdGenerateAccessTokenParams,
  body: API.GenerateAccessTokenInput,
  options?: { [key: string]: any }
) {
  const { id: param0, ...queryParams } = params;
  return request<API.GenerateAccessTokenResultDto>(
    `/api/app/open-iddict-application/${param0}/generate-access-token`,
    {
      method: "POST",
      headers: {
        "Content-Type": "application/json",
      },
      params: { ...queryParams },
      data: body,
      ...(options || {}),
    }
  );
}

/** 此处后端没有提供注释 GET /api/app/open-iddict-application/${param0}/token-lifetime */
export async function getApiAppOpenIddictApplicationIdTokenLifetime(
  // 叠加生成的Param类型 (非body参数swagger默认没有生成对象)
  params: API.getApiAppOpenIddictApplicationIdTokenLifetimeParams,
  options?: { [key: string]: any }
) {
  const { id: param0, ...queryParams } = params;
  return request<API.OpenIddictApplicationTokenLifetimeDto>(
    `/api/app/open-iddict-application/${param0}/token-lifetime`,
    {
      method: "GET",
      params: { ...queryParams },
      ...(options || {}),
    }
  );
}

/** 此处后端没有提供注释 PUT /api/app/open-iddict-application/${param0}/token-lifetime */
export async function putApiAppOpenIddictApplicationIdTokenLifetime(
  // 叠加生成的Param类型 (非body参数swagger默认没有生成对象)
  params: API.putApiAppOpenIddictApplicationIdTokenLifetimeParams,
  body: API.UpdateOpenIddictApplicationTokenLifetimeDto,
  options?: { [key: string]: any }
) {
  const { id: param0, ...queryParams } = params;
  return request<API.OpenIddictApplicationTokenLifetimeDto>(
    `/api/app/open-iddict-application/${param0}/token-lifetime`,
    {
      method: "PUT",
      headers: {
        "Content-Type": "application/json",
      },
      params: { ...queryParams },
      data: body,
      ...(options || {}),
    }
  );
}
