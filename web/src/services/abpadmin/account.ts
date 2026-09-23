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

/** 此处后端没有提供注释 GET /api/payment-service/prepayment/account */
export async function getApiPaymentServicePrepaymentAccount(
  // 叠加生成的Param类型 (非body参数swagger默认没有生成对象)
  params: API.getApiPaymentServicePrepaymentAccountParams,
  options?: { [key: string]: any }
) {
  return request<API.PagedResultDto1AccountDto>(
    "/api/payment-service/prepayment/account",
    {
      method: "GET",
      params: {
        ...params,
      },
      ...(options || {}),
    }
  );
}

/** 此处后端没有提供注释 GET /api/payment-service/prepayment/account/${param0} */
export async function getApiPaymentServicePrepaymentAccountId(
  // 叠加生成的Param类型 (非body参数swagger默认没有生成对象)
  params: API.getApiPaymentServicePrepaymentAccountIdParams,
  options?: { [key: string]: any }
) {
  const { id: param0, ...queryParams } = params;
  return request<API.AccountDto>(
    `/api/payment-service/prepayment/account/${param0}`,
    {
      method: "GET",
      params: { ...queryParams },
      ...(options || {}),
    }
  );
}

/** 此处后端没有提供注释 POST /api/payment-service/prepayment/account/${param0}/change/balance */
export async function postApiPaymentServicePrepaymentAccountIdChangeBalance(
  // 叠加生成的Param类型 (非body参数swagger默认没有生成对象)
  params: API.postApiPaymentServicePrepaymentAccountIdChangeBalanceParams,
  body: API.ChangeBalanceInput,
  options?: { [key: string]: any }
) {
  const { id: param0, ...queryParams } = params;
  return request<API.AccountDto>(
    `/api/payment-service/prepayment/account/${param0}/change/balance`,
    {
      method: "POST",
      headers: {
        "Content-Type": "application/json",
      },
      params: { ...queryParams },
      data: body,
      ...(options || {}),
    }
  );
}

/** 此处后端没有提供注释 POST /api/payment-service/prepayment/account/${param0}/change/locked-balance */
export async function postApiPaymentServicePrepaymentAccountIdChangeLockedBalance(
  // 叠加生成的Param类型 (非body参数swagger默认没有生成对象)
  params: API.postApiPaymentServicePrepaymentAccountIdChangeLockedBalanceParams,
  body: API.ChangeLockedBalanceInput,
  options?: { [key: string]: any }
) {
  const { id: param0, ...queryParams } = params;
  return request<API.AccountDto>(
    `/api/payment-service/prepayment/account/${param0}/change/locked-balance`,
    {
      method: "POST",
      headers: {
        "Content-Type": "application/json",
      },
      params: { ...queryParams },
      data: body,
      ...(options || {}),
    }
  );
}

/** 此处后端没有提供注释 POST /api/payment-service/prepayment/account/${param0}/top-up */
export async function postApiPaymentServicePrepaymentAccountIdTopUp(
  // 叠加生成的Param类型 (非body参数swagger默认没有生成对象)
  params: API.postApiPaymentServicePrepaymentAccountIdTopUpParams,
  body: API.TopUpInput,
  options?: { [key: string]: any }
) {
  const { id: param0, ...queryParams } = params;
  return request<any>(
    `/api/payment-service/prepayment/account/${param0}/top-up`,
    {
      method: "POST",
      headers: {
        "Content-Type": "application/json",
      },
      params: { ...queryParams },
      data: body,
      ...(options || {}),
    }
  );
}

/** 此处后端没有提供注释 POST /api/payment-service/prepayment/account/${param0}/withdraw */
export async function postApiPaymentServicePrepaymentAccountIdWithdraw(
  // 叠加生成的Param类型 (非body参数swagger默认没有生成对象)
  params: API.postApiPaymentServicePrepaymentAccountIdWithdrawParams,
  body: API.WithdrawInput,
  options?: { [key: string]: any }
) {
  const { id: param0, ...queryParams } = params;
  return request<any>(
    `/api/payment-service/prepayment/account/${param0}/withdraw`,
    {
      method: "POST",
      headers: {
        "Content-Type": "application/json",
      },
      params: { ...queryParams },
      data: body,
      ...(options || {}),
    }
  );
}
