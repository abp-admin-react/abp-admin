// @ts-ignore
/* eslint-disable */
import { request } from "@umijs/max";

/** 此处后端没有提供注释 POST /api/account/register */
export async function postApiAccountRegister(
  body: API.RegisterDto,
  options?: { [key: string]: any }
) {
  return request<API.IdentityUserDto>("/api/account/register", {
    method: "POST",
    headers: {
      "Content-Type": "application/json",
    },
    data: body,
    ...(options || {}),
  });
}

/** 此处后端没有提供注释 POST /api/account/reset-password */
export async function postApiAccountResetPassword(
  body: API.ResetPasswordDto,
  options?: { [key: string]: any }
) {
  return request<any>("/api/account/reset-password", {
    method: "POST",
    headers: {
      "Content-Type": "application/json",
    },
    data: body,
    ...(options || {}),
  });
}

/** 此处后端没有提供注释 POST /api/account/send-password-reset-code */
export async function postApiAccountSendPasswordResetCode(
  body: API.SendPasswordResetCodeDto,
  options?: { [key: string]: any }
) {
  return request<any>("/api/account/send-password-reset-code", {
    method: "POST",
    headers: {
      "Content-Type": "application/json",
    },
    data: body,
    ...(options || {}),
  });
}

/** 此处后端没有提供注释 POST /api/account/verify-password-reset-token */
export async function postApiAccountVerifyPasswordResetToken(
  body: API.VerifyPasswordResetTokenInput,
  options?: { [key: string]: any }
) {
  return request<boolean>("/api/account/verify-password-reset-token", {
    method: "POST",
    headers: {
      "Content-Type": "application/json",
    },
    data: body,
    ...(options || {}),
  });
}
