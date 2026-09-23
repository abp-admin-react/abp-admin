// @ts-ignore
/* eslint-disable */
import { request } from "@umijs/max";

/** 此处后端没有提供注释 DELETE /api/app/gdpr-request/current-user-account */
export async function deleteApiAppGdprRequestCurrentUserAccount(
  body: API.DeleteAccountInput,
  options?: { [key: string]: any }
) {
  return request<any>("/api/app/gdpr-request/current-user-account", {
    method: "DELETE",
    headers: {
      "Content-Type": "application/json",
    },
    data: body,
    ...(options || {}),
  });
}
