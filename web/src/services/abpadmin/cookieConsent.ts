// @ts-ignore
/* eslint-disable */
import { request } from "@umijs/max";

/** 此处后端没有提供注释 POST /api/app/cookie-consent/accept */
export async function postApiAppCookieConsentAccept(options?: {
  [key: string]: any;
}) {
  return request<any>("/api/app/cookie-consent/accept", {
    method: "POST",
    ...(options || {}),
  });
}
