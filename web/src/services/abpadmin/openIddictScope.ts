// @ts-ignore
/* eslint-disable */
import { request } from "@umijs/max";

/** 此处后端没有提供注释 GET /api/app/open-iddict-scope */
export async function getApiAppOpenIddictScope(
  // 叠加生成的Param类型 (非body参数swagger默认没有生成对象)
  params: API.getApiAppOpenIddictScopeParams,
  options?: { [key: string]: any }
) {
  return request<API.PagedResultDto1OpenIddictScopeDto>(
    "/api/app/open-iddict-scope",
    {
      method: "GET",
      params: {
        ...params,
      },
      ...(options || {}),
    }
  );
}

/** 此处后端没有提供注释 POST /api/app/open-iddict-scope */
export async function postApiAppOpenIddictScope(
  body: API.CreateOpenIddictScopeDto,
  options?: { [key: string]: any }
) {
  return request<API.OpenIddictScopeDto>("/api/app/open-iddict-scope", {
    method: "POST",
    headers: {
      "Content-Type": "application/json",
    },
    data: body,
    ...(options || {}),
  });
}

/** 此处后端没有提供注释 PUT /api/app/open-iddict-scope/${param0} */
export async function putApiAppOpenIddictScopeId(
  // 叠加生成的Param类型 (非body参数swagger默认没有生成对象)
  params: API.putApiAppOpenIddictScopeIdParams,
  body: API.UpdateOpenIddictScopeDto,
  options?: { [key: string]: any }
) {
  const { id: param0, ...queryParams } = params;
  return request<API.OpenIddictScopeDto>(
    `/api/app/open-iddict-scope/${param0}`,
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

/** 此处后端没有提供注释 DELETE /api/app/open-iddict-scope/${param0} */
export async function deleteApiAppOpenIddictScopeId(
  // 叠加生成的Param类型 (非body参数swagger默认没有生成对象)
  params: API.deleteApiAppOpenIddictScopeIdParams,
  options?: { [key: string]: any }
) {
  const { id: param0, ...queryParams } = params;
  return request<any>(`/api/app/open-iddict-scope/${param0}`, {
    method: "DELETE",
    params: { ...queryParams },
    ...(options || {}),
  });
}

/** 托管 scope（数据库记录）+ 5 个内置 scope（IsBuiltIn=true，不在 OpenIddictScopes 表）。 GET /api/app/open-iddict-scope/all */
export async function getApiAppOpenIddictScopeAll(options?: {
  [key: string]: any;
}) {
  return request<API.ListResultDto1OpenIddictScopeLookupDto>(
    "/api/app/open-iddict-scope/all",
    {
      method: "GET",
      ...(options || {}),
    }
  );
}
