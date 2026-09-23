// @ts-ignore
/* eslint-disable */
import { request } from "@umijs/max";

/** 此处后端没有提供注释 GET /api/app/account-passkey */
export async function getApiAppAccountPasskey(options?: {
  [key: string]: any;
}) {
  return request<API.ListResultDto1UserPasskeyDto>("/api/app/account-passkey", {
    method: "GET",
    ...(options || {}),
  });
}

/** 此处后端没有提供注释 DELETE /api/app/account-passkey */
export async function deleteApiAppAccountPasskey(
  // 叠加生成的Param类型 (非body参数swagger默认没有生成对象)
  params: API.deleteApiAppAccountPasskeyParams,
  options?: { [key: string]: any }
) {
  return request<any>("/api/app/account-passkey", {
    method: "DELETE",
    params: {
      ...params,
    },
    ...(options || {}),
  });
}

/** 此处后端没有提供注释 GET /api/app/account-passkey/assertion-options */
export async function getApiAppAccountPasskeyAssertionOptions(options?: {
  [key: string]: any;
}) {
  return request<API.PasskeyJsonDto>(
    "/api/app/account-passkey/assertion-options",
    {
      method: "GET",
      ...(options || {}),
    }
  );
}

/** 此处后端没有提供注释 GET /api/app/account-passkey/creation-options */
export async function getApiAppAccountPasskeyCreationOptions(options?: {
  [key: string]: any;
}) {
  return request<API.PasskeyJsonDto>(
    "/api/app/account-passkey/creation-options",
    {
      method: "GET",
      ...(options || {}),
    }
  );
}

/** 此处后端没有提供注释 POST /api/app/account-passkey/register */
export async function postApiAppAccountPasskeyRegister(
  body: API.PasskeyCredentialInput,
  options?: { [key: string]: any }
) {
  return request<any>("/api/app/account-passkey/register", {
    method: "POST",
    headers: {
      "Content-Type": "application/json",
    },
    data: body,
    ...(options || {}),
  });
}
