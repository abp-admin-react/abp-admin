// @ts-ignore
/* eslint-disable */
import { request } from "@umijs/max";

/** 此处后端没有提供注释 POST /api/account/check-password */
export async function postApiAccountCheckPassword(
  body: API.UserLoginInfo,
  options?: { [key: string]: any }
) {
  return request<API.AbpLoginResult>("/api/account/check-password", {
    method: "POST",
    headers: {
      "Content-Type": "application/json",
    },
    data: body,
    ...(options || {}),
  });
}

/** 此处后端没有提供注释 POST /api/account/login */
export async function postApiAccountLogin(
  body: API.UserLoginInfo,
  options?: { [key: string]: any }
) {
  return request<API.AbpLoginResult>("/api/account/login", {
    method: "POST",
    headers: {
      "Content-Type": "application/json",
    },
    data: body,
    ...(options || {}),
  });
}

/** 此处后端没有提供注释 GET /api/account/logout */
export async function getApiAccountLogout(options?: { [key: string]: any }) {
  return request<any>("/api/account/logout", {
    method: "GET",
    ...(options || {}),
  });
}
