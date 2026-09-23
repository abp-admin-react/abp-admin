// @ts-ignore
/* eslint-disable */
import { request } from "@umijs/max";

/** 此处后端没有提供注释 GET /api/app/account-security/authenticator-status */
export async function getApiAppAccountSecurityAuthenticatorStatus(options?: {
  [key: string]: any;
}) {
  return request<API.AuthenticatorStatusDto>(
    "/api/app/account-security/authenticator-status",
    {
      method: "GET",
      ...(options || {}),
    }
  );
}

/** 此处后端没有提供注释 POST /api/app/account-security/disable-authenticator */
export async function postApiAppAccountSecurityDisableAuthenticator(
  body: API.DisableAuthenticatorInput,
  options?: { [key: string]: any }
) {
  return request<any>("/api/app/account-security/disable-authenticator", {
    method: "POST",
    headers: {
      "Content-Type": "application/json",
    },
    data: body,
    ...(options || {}),
  });
}

/** 此处后端没有提供注释 POST /api/app/account-security/enable-authenticator */
export async function postApiAppAccountSecurityEnableAuthenticator(
  body: API.EnableAuthenticatorInput,
  options?: { [key: string]: any }
) {
  return request<API.AuthenticatorRecoveryCodesDto>(
    "/api/app/account-security/enable-authenticator",
    {
      method: "POST",
      headers: {
        "Content-Type": "application/json",
      },
      data: body,
      ...(options || {}),
    }
  );
}

/** 此处后端没有提供注释 DELETE /api/app/account-security/login */
export async function deleteApiAppAccountSecurityLogin(
  // 叠加生成的Param类型 (非body参数swagger默认没有生成对象)
  params: API.deleteApiAppAccountSecurityLoginParams,
  options?: { [key: string]: any }
) {
  return request<any>("/api/app/account-security/login", {
    method: "DELETE",
    params: {
      ...params,
    },
    ...(options || {}),
  });
}

/** 此处后端没有提供注释 GET /api/app/account-security/logins */
export async function getApiAppAccountSecurityLogins(options?: {
  [key: string]: any;
}) {
  return request<API.UserLoginDto[]>("/api/app/account-security/logins", {
    method: "GET",
    ...(options || {}),
  });
}

/** 此处后端没有提供注释 POST /api/app/account-security/reset-authenticator-key */
export async function postApiAppAccountSecurityResetAuthenticatorKey(options?: {
  [key: string]: any;
}) {
  return request<API.AuthenticatorKeyDto>(
    "/api/app/account-security/reset-authenticator-key",
    {
      method: "POST",
      ...(options || {}),
    }
  );
}
