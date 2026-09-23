// @ts-ignore
/* eslint-disable */
import { request } from "@umijs/max";

/** 此处后端没有提供注释 GET /api/app/cache-monitor/info */
export async function getApiAppCacheMonitorInfo(options?: {
  [key: string]: any;
}) {
  return request<API.CacheMonitorInfoDto>("/api/app/cache-monitor/info", {
    method: "GET",
    ...(options || {}),
  });
}

/** 此处后端没有提供注释 DELETE /api/app/cache-monitor/key */
export async function deleteApiAppCacheMonitorKey(
  // 叠加生成的Param类型 (非body参数swagger默认没有生成对象)
  params: API.deleteApiAppCacheMonitorKeyParams,
  options?: { [key: string]: any }
) {
  return request<any>("/api/app/cache-monitor/key", {
    method: "DELETE",
    params: {
      ...params,
    },
    ...(options || {}),
  });
}

/** 此处后端没有提供注释 GET /api/app/cache-monitor/keys */
export async function getApiAppCacheMonitorKeys(
  // 叠加生成的Param类型 (非body参数swagger默认没有生成对象)
  params: API.getApiAppCacheMonitorKeysParams,
  options?: { [key: string]: any }
) {
  return request<API.CacheKeyListResultDto>("/api/app/cache-monitor/keys", {
    method: "GET",
    params: {
      ...params,
    },
    ...(options || {}),
  });
}

/** 此处后端没有提供注释 GET /api/app/cache-monitor/value */
export async function getApiAppCacheMonitorValue(
  // 叠加生成的Param类型 (非body参数swagger默认没有生成对象)
  params: API.getApiAppCacheMonitorValueParams,
  options?: { [key: string]: any }
) {
  return request<API.CacheValueDto>("/api/app/cache-monitor/value", {
    method: "GET",
    params: {
      ...params,
    },
    ...(options || {}),
  });
}
