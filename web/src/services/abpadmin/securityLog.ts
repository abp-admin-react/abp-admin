// @ts-ignore
/* eslint-disable */
import { request } from "@umijs/max";

/** 此处后端没有提供注释 GET /api/app/security-log */
export async function getApiAppSecurityLog(
  // 叠加生成的Param类型 (非body参数swagger默认没有生成对象)
  params: API.getApiAppSecurityLogParams,
  options?: { [key: string]: any }
) {
  return request<API.PagedResultDto1SecurityLogDto>("/api/app/security-log", {
    method: "GET",
    params: {
      ...params,
    },
    ...(options || {}),
  });
}
