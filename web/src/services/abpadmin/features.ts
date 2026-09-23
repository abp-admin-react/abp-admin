// @ts-ignore
/* eslint-disable */
import { request } from "@umijs/max";

/** 此处后端没有提供注释 GET /api/feature-management/features */
export async function getApiFeatureManagementFeatures(
  // 叠加生成的Param类型 (非body参数swagger默认没有生成对象)
  params: API.getApiFeatureManagementFeaturesParams,
  options?: { [key: string]: any }
) {
  return request<API.GetFeatureListResultDto>(
    "/api/feature-management/features",
    {
      method: "GET",
      params: {
        ...params,
      },
      ...(options || {}),
    }
  );
}

/** 此处后端没有提供注释 PUT /api/feature-management/features */
export async function putApiFeatureManagementFeatures(
  // 叠加生成的Param类型 (非body参数swagger默认没有生成对象)
  params: API.putApiFeatureManagementFeaturesParams,
  body: API.UpdateFeaturesDto,
  options?: { [key: string]: any }
) {
  return request<any>("/api/feature-management/features", {
    method: "PUT",
    headers: {
      "Content-Type": "application/json",
    },
    params: {
      ...params,
    },
    data: body,
    ...(options || {}),
  });
}

/** 此处后端没有提供注释 DELETE /api/feature-management/features */
export async function deleteApiFeatureManagementFeatures(
  // 叠加生成的Param类型 (非body参数swagger默认没有生成对象)
  params: API.deleteApiFeatureManagementFeaturesParams,
  options?: { [key: string]: any }
) {
  return request<any>("/api/feature-management/features", {
    method: "DELETE",
    params: {
      ...params,
    },
    ...(options || {}),
  });
}
