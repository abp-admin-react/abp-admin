// @ts-ignore
/* eslint-disable */
import { request } from "@umijs/max";

/** 此处后端没有提供注释 GET /api/setting-management/timezone */
export async function getApiSettingManagementTimezone(options?: {
  [key: string]: any;
}) {
  return request<string>("/api/setting-management/timezone", {
    method: "GET",
    ...(options || {}),
  });
}

/** 此处后端没有提供注释 POST /api/setting-management/timezone */
export async function postApiSettingManagementTimezone(
  // 叠加生成的Param类型 (非body参数swagger默认没有生成对象)
  params: API.postApiSettingManagementTimezoneParams,
  options?: { [key: string]: any }
) {
  return request<any>("/api/setting-management/timezone", {
    method: "POST",
    params: {
      ...params,
    },
    ...(options || {}),
  });
}

/** 此处后端没有提供注释 GET /api/setting-management/timezone/timezones */
export async function getApiSettingManagementTimezoneTimezones(options?: {
  [key: string]: any;
}) {
  return request<API.NameValue[]>(
    "/api/setting-management/timezone/timezones",
    {
      method: "GET",
      ...(options || {}),
    }
  );
}
