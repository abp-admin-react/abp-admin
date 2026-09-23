// @ts-ignore
/* eslint-disable */
import { request } from "@umijs/max";

/** 此处后端没有提供注释 GET /api/app/virtual-file-explorer */
export async function getApiAppVirtualFileExplorer(
  // 叠加生成的Param类型 (非body参数swagger默认没有生成对象)
  params: API.getApiAppVirtualFileExplorerParams,
  options?: { [key: string]: any }
) {
  return request<API.VirtualFileDirectoryDto>(
    "/api/app/virtual-file-explorer",
    {
      method: "GET",
      params: {
        ...params,
      },
      ...(options || {}),
    }
  );
}

/** 此处后端没有提供注释 GET /api/app/virtual-file-explorer/content */
export async function getApiAppVirtualFileExplorerContent(
  // 叠加生成的Param类型 (非body参数swagger默认没有生成对象)
  params: API.getApiAppVirtualFileExplorerContentParams,
  options?: { [key: string]: any }
) {
  return request<API.VirtualFileContentDto>(
    "/api/app/virtual-file-explorer/content",
    {
      method: "GET",
      params: {
        ...params,
      },
      ...(options || {}),
    }
  );
}
