// @ts-ignore
/* eslint-disable */
import { request } from "@umijs/max";

/** 此处后端没有提供注释 GET /api/app/text-template */
export async function getApiAppTextTemplate(
  // 叠加生成的Param类型 (非body参数swagger默认没有生成对象)
  params: API.getApiAppTextTemplateParams,
  options?: { [key: string]: any }
) {
  return request<API.TextTemplateDto[]>("/api/app/text-template", {
    method: "GET",
    params: {
      ...params,
    },
    ...(options || {}),
  });
}

/** 此处后端没有提供注释 PUT /api/app/text-template */
export async function putApiAppTextTemplate(
  body: API.UpdateTextTemplateDto,
  options?: { [key: string]: any }
) {
  return request<any>("/api/app/text-template", {
    method: "PUT",
    headers: {
      "Content-Type": "application/json",
    },
    data: body,
    ...(options || {}),
  });
}

/** 此处后端没有提供注释 GET /api/app/text-template/content */
export async function getApiAppTextTemplateContent(
  // 叠加生成的Param类型 (非body参数swagger默认没有生成对象)
  params: API.getApiAppTextTemplateContentParams,
  options?: { [key: string]: any }
) {
  return request<API.TextTemplateDto>("/api/app/text-template/content", {
    method: "GET",
    params: {
      ...params,
    },
    ...(options || {}),
  });
}

/** 此处后端没有提供注释 POST /api/app/text-template/restore-to-default */
export async function postApiAppTextTemplateRestoreToDefault(
  body: API.RestoreTextTemplateToDefaultInput,
  options?: { [key: string]: any }
) {
  return request<any>("/api/app/text-template/restore-to-default", {
    method: "POST",
    headers: {
      "Content-Type": "application/json",
    },
    data: body,
    ...(options || {}),
  });
}
