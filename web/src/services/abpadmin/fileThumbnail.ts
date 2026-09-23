// @ts-ignore
/* eslint-disable */
import { request } from "@umijs/max";

/** 此处后端没有提供注释 GET /api/app/file-thumbnail/${param0} */
export async function getApiAppFileThumbnailId(
  // 叠加生成的Param类型 (非body参数swagger默认没有生成对象)
  params: API.getApiAppFileThumbnailIdParams,
  options?: { [key: string]: any }
) {
  const { id: param0, ...queryParams } = params;
  return request<string>(`/api/app/file-thumbnail/${param0}`, {
    method: "GET",
    params: { ...queryParams },
    ...(options || {}),
  });
}

/** 此处后端没有提供注释 GET /api/app/file-thumbnail/with-thumbnails */
export async function getApiAppFileThumbnailWithThumbnails(
  // 叠加生成的Param类型 (非body参数swagger默认没有生成对象)
  params: API.getApiAppFileThumbnailWithThumbnailsParams,
  options?: { [key: string]: any }
) {
  return request<API.GetFileListWithThumbnailsOutput>(
    "/api/app/file-thumbnail/with-thumbnails",
    {
      method: "GET",
      params: {
        ...params,
      },
      ...(options || {}),
    }
  );
}
