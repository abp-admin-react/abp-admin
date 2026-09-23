// @ts-ignore
/* eslint-disable */
import { request } from "@umijs/max";

/** 此处后端没有提供注释 GET /api/app/language */
export async function getApiAppLanguage(options?: { [key: string]: any }) {
  return request<API.ListResultDto1LanguageDto>("/api/app/language", {
    method: "GET",
    ...(options || {}),
  });
}

/** 此处后端没有提供注释 POST /api/app/language */
export async function postApiAppLanguage(
  body: API.CreateLanguageDto,
  options?: { [key: string]: any }
) {
  return request<API.LanguageDto>("/api/app/language", {
    method: "POST",
    headers: {
      "Content-Type": "application/json",
    },
    data: body,
    ...(options || {}),
  });
}

/** 此处后端没有提供注释 GET /api/app/language/${param0} */
export async function getApiAppLanguageId(
  // 叠加生成的Param类型 (非body参数swagger默认没有生成对象)
  params: API.getApiAppLanguageIdParams,
  options?: { [key: string]: any }
) {
  const { id: param0, ...queryParams } = params;
  return request<API.LanguageDto>(`/api/app/language/${param0}`, {
    method: "GET",
    params: { ...queryParams },
    ...(options || {}),
  });
}

/** 此处后端没有提供注释 PUT /api/app/language/${param0} */
export async function putApiAppLanguageId(
  // 叠加生成的Param类型 (非body参数swagger默认没有生成对象)
  params: API.putApiAppLanguageIdParams,
  body: API.UpdateLanguageDto,
  options?: { [key: string]: any }
) {
  const { id: param0, ...queryParams } = params;
  return request<API.LanguageDto>(`/api/app/language/${param0}`, {
    method: "PUT",
    headers: {
      "Content-Type": "application/json",
    },
    params: { ...queryParams },
    data: body,
    ...(options || {}),
  });
}

/** 此处后端没有提供注释 DELETE /api/app/language/${param0} */
export async function deleteApiAppLanguageId(
  // 叠加生成的Param类型 (非body参数swagger默认没有生成对象)
  params: API.deleteApiAppLanguageIdParams,
  options?: { [key: string]: any }
) {
  const { id: param0, ...queryParams } = params;
  return request<any>(`/api/app/language/${param0}`, {
    method: "DELETE",
    params: { ...queryParams },
    ...(options || {}),
  });
}

/** 此处后端没有提供注释 POST /api/app/language/${param0}/set-as-default */
export async function postApiAppLanguageIdSetAsDefault(
  // 叠加生成的Param类型 (非body参数swagger默认没有生成对象)
  params: API.postApiAppLanguageIdSetAsDefaultParams,
  options?: { [key: string]: any }
) {
  const { id: param0, ...queryParams } = params;
  return request<any>(`/api/app/language/${param0}/set-as-default`, {
    method: "POST",
    params: { ...queryParams },
    ...(options || {}),
  });
}
