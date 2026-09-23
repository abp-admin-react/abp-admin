// @ts-ignore
/* eslint-disable */
import { request } from "@umijs/max";

/** 此处后端没有提供注释 GET /api/app/audit-log/export */
export async function getApiAppAuditLogOpenApiExport(
  // 叠加生成的Param类型 (非body参数swagger默认没有生成对象)
  params: API.getApiAppAuditLog_openAPI_exportParams,
  options?: { [key: string]: any }
) {
  return request<any>("/api/app/audit-log/export", {
    method: "GET",
    params: {
      ...params,
    },
    ...(options || {}),
  });
}

/** 此处后端没有提供注释 GET /api/app/audit-log/export-file/${param0} */
export async function getApiAppAuditLogExportFileId(
  // 叠加生成的Param类型 (非body参数swagger默认没有生成对象)
  params: API.getApiAppAuditLogExportFileIdParams,
  options?: { [key: string]: any }
) {
  const { id: param0, ...queryParams } = params;
  return request<any>(`/api/app/audit-log/export-file/${param0}`, {
    method: "GET",
    params: { ...queryParams },
    ...(options || {}),
  });
}
