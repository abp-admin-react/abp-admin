// @ts-ignore
/* eslint-disable */
import { request } from "@umijs/max";

/** 此处后端没有提供注释 GET /api/setting-ui */
export async function getApiSettingUi(options?: { [key: string]: any }) {
  return request<API.SettingGroup[]>("/api/setting-ui", {
    method: "GET",
    ...(options || {}),
  });
}

/** 此处后端没有提供注释 PUT /api/setting-ui/reset-setting-values */
export async function putApiSettingUiResetSettingValues(
  body: string[],
  options?: { [key: string]: any }
) {
  return request<any>("/api/setting-ui/reset-setting-values", {
    method: "PUT",
    headers: {
      "Content-Type": "application/json",
    },
    data: body,
    ...(options || {}),
  });
}

/** 此处后端没有提供注释 PUT /api/setting-ui/set-setting-values */
export async function putApiSettingUiSetSettingValues(
  body: Record<string, any>,
  options?: { [key: string]: any }
) {
  return request<any>("/api/setting-ui/set-setting-values", {
    method: "PUT",
    headers: {
      "Content-Type": "application/json",
    },
    data: body,
    ...(options || {}),
  });
}
