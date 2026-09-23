// @ts-ignore
/* eslint-disable */
import { request } from "@umijs/max";

/** 此处后端没有提供注释 GET /api/setting-management/emailing */
export async function getApiSettingManagementEmailing(options?: {
  [key: string]: any;
}) {
  return request<API.EmailSettingsDto>("/api/setting-management/emailing", {
    method: "GET",
    ...(options || {}),
  });
}

/** 此处后端没有提供注释 POST /api/setting-management/emailing */
export async function postApiSettingManagementEmailing(
  body: API.UpdateEmailSettingsDto,
  options?: { [key: string]: any }
) {
  return request<any>("/api/setting-management/emailing", {
    method: "POST",
    headers: {
      "Content-Type": "application/json",
    },
    data: body,
    ...(options || {}),
  });
}

/** 此处后端没有提供注释 POST /api/setting-management/emailing/send-test-email */
export async function postApiSettingManagementEmailingSendTestEmail(
  body: API.SendTestEmailInput,
  options?: { [key: string]: any }
) {
  return request<any>("/api/setting-management/emailing/send-test-email", {
    method: "POST",
    headers: {
      "Content-Type": "application/json",
    },
    data: body,
    ...(options || {}),
  });
}
