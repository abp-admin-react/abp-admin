// @ts-ignore
/* eslint-disable */
import { request } from "@umijs/max";

/** 此处后端没有提供注释 GET /api/permission-management/permissions */
export async function getApiPermissionManagementPermissions(
  // 叠加生成的Param类型 (非body参数swagger默认没有生成对象)
  params: API.getApiPermissionManagementPermissionsParams,
  options?: { [key: string]: any }
) {
  return request<API.GetPermissionListResultDto>(
    "/api/permission-management/permissions",
    {
      method: "GET",
      params: {
        ...params,
      },
      ...(options || {}),
    }
  );
}

/** 此处后端没有提供注释 PUT /api/permission-management/permissions */
export async function putApiPermissionManagementPermissions(
  // 叠加生成的Param类型 (非body参数swagger默认没有生成对象)
  params: API.putApiPermissionManagementPermissionsParams,
  body: API.UpdatePermissionsDto,
  options?: { [key: string]: any }
) {
  return request<any>("/api/permission-management/permissions", {
    method: "PUT",
    headers: {
      "Content-Type": "application/json",
    },
    params: {
      ...params,
    },
    data: body,
    ...(options || {}),
  });
}

/** 此处后端没有提供注释 GET /api/permission-management/permissions/by-group */
export async function getApiPermissionManagementPermissionsByGroup(
  // 叠加生成的Param类型 (非body参数swagger默认没有生成对象)
  params: API.getApiPermissionManagementPermissionsByGroupParams,
  options?: { [key: string]: any }
) {
  return request<API.GetPermissionListResultDto>(
    "/api/permission-management/permissions/by-group",
    {
      method: "GET",
      params: {
        ...params,
      },
      ...(options || {}),
    }
  );
}

/** 此处后端没有提供注释 GET /api/permission-management/permissions/resource */
export async function getApiPermissionManagementPermissionsResource(
  // 叠加生成的Param类型 (非body参数swagger默认没有生成对象)
  params: API.getApiPermissionManagementPermissionsResourceParams,
  options?: { [key: string]: any }
) {
  return request<API.GetResourcePermissionListResultDto>(
    "/api/permission-management/permissions/resource",
    {
      method: "GET",
      params: {
        ...params,
      },
      ...(options || {}),
    }
  );
}

/** 此处后端没有提供注释 PUT /api/permission-management/permissions/resource */
export async function putApiPermissionManagementPermissionsResource(
  // 叠加生成的Param类型 (非body参数swagger默认没有生成对象)
  params: API.putApiPermissionManagementPermissionsResourceParams,
  body: API.UpdateResourcePermissionsDto,
  options?: { [key: string]: any }
) {
  return request<any>("/api/permission-management/permissions/resource", {
    method: "PUT",
    headers: {
      "Content-Type": "application/json",
    },
    params: {
      ...params,
    },
    data: body,
    ...(options || {}),
  });
}

/** 此处后端没有提供注释 DELETE /api/permission-management/permissions/resource */
export async function deleteApiPermissionManagementPermissionsResource(
  // 叠加生成的Param类型 (非body参数swagger默认没有生成对象)
  params: API.deleteApiPermissionManagementPermissionsResourceParams,
  options?: { [key: string]: any }
) {
  return request<any>("/api/permission-management/permissions/resource", {
    method: "DELETE",
    params: {
      ...params,
    },
    ...(options || {}),
  });
}

/** 此处后端没有提供注释 GET /api/permission-management/permissions/resource-definitions */
export async function getApiPermissionManagementPermissionsResourceDefinitions(
  // 叠加生成的Param类型 (非body参数swagger默认没有生成对象)
  params: API.getApiPermissionManagementPermissionsResourceDefinitionsParams,
  options?: { [key: string]: any }
) {
  return request<API.GetResourcePermissionDefinitionListResultDto>(
    "/api/permission-management/permissions/resource-definitions",
    {
      method: "GET",
      params: {
        ...params,
      },
      ...(options || {}),
    }
  );
}

/** 此处后端没有提供注释 GET /api/permission-management/permissions/resource-provider-key-lookup-services */
export async function getApiPermissionManagementPermissionsResourceProviderKeyLookupServices(
  // 叠加生成的Param类型 (非body参数swagger默认没有生成对象)
  params: API.getApiPermissionManagementPermissionsResourceProviderKeyLookupServicesParams,
  options?: { [key: string]: any }
) {
  return request<API.GetResourceProviderListResultDto>(
    "/api/permission-management/permissions/resource-provider-key-lookup-services",
    {
      method: "GET",
      params: {
        ...params,
      },
      ...(options || {}),
    }
  );
}

/** 此处后端没有提供注释 GET /api/permission-management/permissions/resource/by-provider */
export async function getApiPermissionManagementPermissionsResourceByProvider(
  // 叠加生成的Param类型 (非body参数swagger默认没有生成对象)
  params: API.getApiPermissionManagementPermissionsResourceByProviderParams,
  options?: { [key: string]: any }
) {
  return request<API.GetResourcePermissionWithProviderListResultDto>(
    "/api/permission-management/permissions/resource/by-provider",
    {
      method: "GET",
      params: {
        ...params,
      },
      ...(options || {}),
    }
  );
}

/** 此处后端没有提供注释 GET /api/permission-management/permissions/search-resource-provider-keys */
export async function getApiPermissionManagementPermissionsSearchResourceProviderKeys(
  // 叠加生成的Param类型 (非body参数swagger默认没有生成对象)
  params: API.getApiPermissionManagementPermissionsSearchResourceProviderKeysParams,
  options?: { [key: string]: any }
) {
  return request<API.SearchProviderKeyListResultDto>(
    "/api/permission-management/permissions/search-resource-provider-keys",
    {
      method: "GET",
      params: {
        ...params,
      },
      ...(options || {}),
    }
  );
}
