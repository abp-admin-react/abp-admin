// @ts-ignore
/* eslint-disable */
import { request } from "@umijs/max";

/** 此处后端没有提供注释 GET /api/app/data-scope-demo */
export async function getApiAppDataScopeDemo(
  // 叠加生成的Param类型 (非body参数swagger默认没有生成对象)
  params: API.getApiAppDataScopeDemoParams,
  options?: { [key: string]: any }
) {
  return request<API.PagedResultDto1DataScopeDemoDto>(
    "/api/app/data-scope-demo",
    {
      method: "GET",
      params: {
        ...params,
      },
      ...(options || {}),
    }
  );
}

/** 此处后端没有提供注释 POST /api/app/data-scope-demo */
export async function postApiAppDataScopeDemo(
  body: API.CreateDataScopeDemoDto,
  options?: { [key: string]: any }
) {
  return request<API.DataScopeDemoDto>("/api/app/data-scope-demo", {
    method: "POST",
    headers: {
      "Content-Type": "application/json",
    },
    data: body,
    ...(options || {}),
  });
}

/** 此处后端没有提供注释 DELETE /api/app/data-scope-demo/${param0} */
export async function deleteApiAppDataScopeDemoId(
  // 叠加生成的Param类型 (非body参数swagger默认没有生成对象)
  params: API.deleteApiAppDataScopeDemoIdParams,
  options?: { [key: string]: any }
) {
  const { id: param0, ...queryParams } = params;
  return request<any>(`/api/app/data-scope-demo/${param0}`, {
    method: "DELETE",
    params: { ...queryParams },
    ...(options || {}),
  });
}
