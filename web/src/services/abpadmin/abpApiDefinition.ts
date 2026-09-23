// @ts-ignore
/* eslint-disable */
import { request } from "@umijs/max";

/** 此处后端没有提供注释 GET /api/abp/api-definition */
export async function getApiAbpApiDefinition(
  // 叠加生成的Param类型 (非body参数swagger默认没有生成对象)
  params: API.getApiAbpApiDefinitionParams,
  options?: { [key: string]: any }
) {
  return request<API.ApplicationApiDescriptionModel>(
    "/api/abp/api-definition",
    {
      method: "GET",
      params: {
        ...params,
      },
      ...(options || {}),
    }
  );
}
