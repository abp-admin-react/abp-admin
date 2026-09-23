// @ts-ignore
/* eslint-disable */
import { request } from "@umijs/max";

/** 此处后端没有提供注释 GET /api/app/identity-user-admin/export */
export async function getApiAppIdentityUserAdminOpenApiExport(
  // 叠加生成的Param类型 (非body参数swagger默认没有生成对象)
  params: API.getApiAppIdentityUserAdmin_openAPI_exportParams,
  options?: { [key: string]: any }
) {
  return request<any>("/api/app/identity-user-admin/export", {
    method: "GET",
    params: {
      ...params,
    },
    ...(options || {}),
  });
}

/** 此处后端没有提供注释 GET /api/app/identity-user-admin/export-file/${param0} */
export async function getApiAppIdentityUserAdminExportFileId(
  // 叠加生成的Param类型 (非body参数swagger默认没有生成对象)
  params: API.getApiAppIdentityUserAdminExportFileIdParams,
  options?: { [key: string]: any }
) {
  const { id: param0, ...queryParams } = params;
  return request<any>(`/api/app/identity-user-admin/export-file/${param0}`, {
    method: "GET",
    params: { ...queryParams },
    ...(options || {}),
  });
}

/** 此处后端没有提供注释 GET /api/app/identity-user-admin/import-failure-report/${param0} */
export async function getApiAppIdentityUserAdminImportFailureReportId(
  // 叠加生成的Param类型 (非body参数swagger默认没有生成对象)
  params: API.getApiAppIdentityUserAdminImportFailureReportIdParams,
  options?: { [key: string]: any }
) {
  const { id: param0, ...queryParams } = params;
  return request<any>(
    `/api/app/identity-user-admin/import-failure-report/${param0}`,
    {
      method: "GET",
      params: { ...queryParams },
      ...(options || {}),
    }
  );
}

/** 此处后端没有提供注释 GET /api/app/identity-user-admin/import-template */
export async function getApiAppIdentityUserAdminImportTemplate(options?: {
  [key: string]: any;
}) {
  return request<any>("/api/app/identity-user-admin/import-template", {
    method: "GET",
    ...(options || {}),
  });
}
