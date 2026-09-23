// @ts-ignore
/* eslint-disable */
import { request } from "@umijs/max";

/** ABP 动态 API 路由：GET /api/app/captcha-image GET /api/app/captcha-image */
export async function getApiAppCaptchaImage(options?: { [key: string]: any }) {
  return request<API.CaptchaImageDto>("/api/app/captcha-image", {
    method: "GET",
    ...(options || {}),
  });
}
