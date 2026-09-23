// @ts-ignore
/* eslint-disable */
import { request } from "@umijs/max";

/** 此处后端没有提供注释 GET /api/app/operation-log */
export async function getApiAppOperationLog(
  // 叠加生成的Param类型 (非body参数swagger默认没有生成对象)
  params: API.getApiAppOperationLogParams,
  options?: { [key: string]: any }
) {
  return request<API.PagedResultDto1OperationLogDto>("/api/app/operation-log", {
    method: "GET",
    params: {
      ...params,
    },
    ...(options || {}),
  });
}
