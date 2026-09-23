// @ts-ignore
/* eslint-disable */
import { request } from "@umijs/max";

/** 此处后端没有提供注释 GET /api/file-management/file */
export async function getApiFileManagementFile(
  // 叠加生成的Param类型 (非body参数swagger默认没有生成对象)
  params: API.getApiFileManagementFileParams,
  options?: { [key: string]: any }
) {
  return request<API.PagedResultDto1FileInfoDto>("/api/file-management/file", {
    method: "GET",
    params: {
      ...params,
    },
    ...(options || {}),
  });
}

/** 此处后端没有提供注释 POST /api/file-management/file */
export async function postApiFileManagementFile(
  body: {
    FileContainerName: string;
    FileType?: API.FileType;
    ParentId?: string;
    OwnerUserId?: string;
    GenerateUniqueFileName?: boolean;
    ExtraProperties?: Record<string, any>;
  },
  File?: File,
  options?: { [key: string]: any }
) {
  const formData = new FormData();

  if (File) {
    formData.append("File", File);
  }

  Object.keys(body).forEach((ele) => {
    const item = (body as any)[ele];

    if (item !== undefined && item !== null) {
      // 注意：此处用 Object.prototype.toString 判别 File 而非 item instanceof File。
        // 本文件由 openapi 生成器产出（npm run openapi 会覆盖手工改动，需同步生成模板）；
        // @types/node 的 File 声明会把 DOM File 遮蔽成纯类型，instanceof 直接编译报
        // TS2359（pnpm tsc 存量失败的根因，见 docs/TEST_EVIDENCE_2026-09-21.md D6）。
        if (typeof item === "object" && Object.prototype.toString.call(item) !== "[object File]") {
        if (item instanceof Array) {
          item.forEach((f) => formData.append(ele, f || ""));
        } else {
          formData.append(
            ele,
            new Blob([JSON.stringify(item)], { type: "application/json" })
          );
        }
      } else {
        formData.append(ele, item);
      }
    }
  });

  return request<API.CreateFileOutput>("/api/file-management/file", {
    method: "POST",
    data: formData,
    requestType: "form",
    ...(options || {}),
  });
}

/** 此处后端没有提供注释 GET /api/file-management/file/${param0} */
export async function getApiFileManagementFileId(
  // 叠加生成的Param类型 (非body参数swagger默认没有生成对象)
  params: API.getApiFileManagementFileIdParams,
  options?: { [key: string]: any }
) {
  const { id: param0, ...queryParams } = params;
  return request<API.FileInfoDto>(`/api/file-management/file/${param0}`, {
    method: "GET",
    params: { ...queryParams },
    ...(options || {}),
  });
}

/** 此处后端没有提供注释 DELETE /api/file-management/file/${param0} */
export async function deleteApiFileManagementFileId(
  // 叠加生成的Param类型 (非body参数swagger默认没有生成对象)
  params: API.deleteApiFileManagementFileIdParams,
  options?: { [key: string]: any }
) {
  const { id: param0, ...queryParams } = params;
  return request<any>(`/api/file-management/file/${param0}`, {
    method: "DELETE",
    params: { ...queryParams },
    ...(options || {}),
  });
}

/** 此处后端没有提供注释 GET /api/file-management/file/${param0}/download */
export async function getApiFileManagementFileIdDownload(
  // 叠加生成的Param类型 (非body参数swagger默认没有生成对象)
  params: API.getApiFileManagementFileIdDownloadParams,
  options?: { [key: string]: any }
) {
  const { id: param0, ...queryParams } = params;
  return request<any>(`/api/file-management/file/${param0}/download`, {
    method: "GET",
    params: {
      ...queryParams,
    },
    ...(options || {}),
  });
}

/** 此处后端没有提供注释 GET /api/file-management/file/${param0}/download-info */
export async function getApiFileManagementFileIdDownloadInfo(
  // 叠加生成的Param类型 (非body参数swagger默认没有生成对象)
  params: API.getApiFileManagementFileIdDownloadInfoParams,
  options?: { [key: string]: any }
) {
  const { id: param0, ...queryParams } = params;
  return request<API.FileDownloadInfoModel>(
    `/api/file-management/file/${param0}/download-info`,
    {
      method: "GET",
      params: { ...queryParams },
      ...(options || {}),
    }
  );
}

/** 此处后端没有提供注释 GET /api/file-management/file/${param0}/download/with-bytes */
export async function getApiFileManagementFileIdDownloadWithBytes(
  // 叠加生成的Param类型 (非body参数swagger默认没有生成对象)
  params: API.getApiFileManagementFileIdDownloadWithBytesParams,
  options?: { [key: string]: any }
) {
  const { id: param0, ...queryParams } = params;
  return request<API.FileDownloadOutput>(
    `/api/file-management/file/${param0}/download/with-bytes`,
    {
      method: "GET",
      params: {
        ...queryParams,
      },
      ...(options || {}),
    }
  );
}

/** 此处后端没有提供注释 GET /api/file-management/file/${param0}/download/with-stream */
export async function getApiFileManagementFileIdDownloadWithStream(
  // 叠加生成的Param类型 (非body参数swagger默认没有生成对象)
  params: API.getApiFileManagementFileIdDownloadWithStreamParams,
  options?: { [key: string]: any }
) {
  const { id: param0, ...queryParams } = params;
  return request<string>(
    `/api/file-management/file/${param0}/download/with-stream`,
    {
      method: "GET",
      params: {
        ...queryParams,
      },
      ...(options || {}),
    }
  );
}

/** 此处后端没有提供注释 PUT /api/file-management/file/${param0}/info */
export async function putApiFileManagementFileIdInfo(
  // 叠加生成的Param类型 (非body参数swagger默认没有生成对象)
  params: API.putApiFileManagementFileIdInfoParams,
  body: API.UpdateFileInfoInput,
  options?: { [key: string]: any }
) {
  const { id: param0, ...queryParams } = params;
  return request<API.FileInfoDto>(`/api/file-management/file/${param0}/info`, {
    method: "PUT",
    headers: {
      "Content-Type": "application/json",
    },
    params: { ...queryParams },
    data: body,
    ...(options || {}),
  });
}

/** 此处后端没有提供注释 PUT /api/file-management/file/${param0}/move */
export async function putApiFileManagementFileIdMove(
  // 叠加生成的Param类型 (非body参数swagger默认没有生成对象)
  params: API.putApiFileManagementFileIdMoveParams,
  body: API.MoveFileInput,
  options?: { [key: string]: any }
) {
  const { id: param0, ...queryParams } = params;
  return request<API.FileInfoDto>(`/api/file-management/file/${param0}/move`, {
    method: "PUT",
    headers: {
      "Content-Type": "application/json",
    },
    params: { ...queryParams },
    data: body,
    ...(options || {}),
  });
}

/** 此处后端没有提供注释 GET /api/file-management/file/by-path */
export async function getApiFileManagementFileByPath(
  // 叠加生成的Param类型 (非body参数swagger默认没有生成对象)
  params: API.getApiFileManagementFileByPathParams,
  options?: { [key: string]: any }
) {
  return request<API.GetFileByPathOutputDto>(
    "/api/file-management/file/by-path",
    {
      method: "GET",
      params: {
        ...params,
      },
      ...(options || {}),
    }
  );
}

/** 此处后端没有提供注释 GET /api/file-management/file/configuration */
export async function getApiFileManagementFileConfiguration(
  // 叠加生成的Param类型 (非body参数swagger默认没有生成对象)
  params: API.getApiFileManagementFileConfigurationParams,
  options?: { [key: string]: any }
) {
  return request<API.PublicFileContainerConfiguration>(
    "/api/file-management/file/configuration",
    {
      method: "GET",
      params: {
        ...params,
      },
      ...(options || {}),
    }
  );
}

/** 此处后端没有提供注释 GET /api/file-management/file/location */
export async function getApiFileManagementFileLocation(
  // 叠加生成的Param类型 (非body参数swagger默认没有生成对象)
  params: API.getApiFileManagementFileLocationParams,
  options?: { [key: string]: any }
) {
  return request<API.FileLocationDto>("/api/file-management/file/location", {
    method: "GET",
    params: {
      ...params,
    },
    ...(options || {}),
  });
}

/** 此处后端没有提供注释 POST /api/file-management/file/many */
export async function postApiFileManagementFileMany(
  body: {
    FileContainerName: string;
    FileType?: API.FileType;
    ParentId?: string;
    OwnerUserId?: string;
    GenerateUniqueFileName?: boolean;
    ExtraProperties?: Record<string, any>;
  },
  Files?: File[],
  options?: { [key: string]: any }
) {
  const formData = new FormData();

  if (Files) {
    Files.forEach((f) => formData.append("Files", f || ""));
  }

  Object.keys(body).forEach((ele) => {
    const item = (body as any)[ele];

    if (item !== undefined && item !== null) {
      // 注意：此处用 Object.prototype.toString 判别 File 而非 item instanceof File。
        // 本文件由 openapi 生成器产出（npm run openapi 会覆盖手工改动，需同步生成模板）；
        // @types/node 的 File 声明会把 DOM File 遮蔽成纯类型，instanceof 直接编译报
        // TS2359（pnpm tsc 存量失败的根因，见 docs/TEST_EVIDENCE_2026-09-21.md D6）。
        if (typeof item === "object" && Object.prototype.toString.call(item) !== "[object File]") {
        if (item instanceof Array) {
          item.forEach((f) => formData.append(ele, f || ""));
        } else {
          formData.append(
            ele,
            new Blob([JSON.stringify(item)], { type: "application/json" })
          );
        }
      } else {
        formData.append(ele, item);
      }
    }
  });

  return request<API.CreateManyFileOutput>("/api/file-management/file/many", {
    method: "POST",
    data: formData,
    requestType: "form",
    ...(options || {}),
  });
}

/** 此处后端没有提供注释 POST /api/file-management/file/many/with-bytes */
export async function postApiFileManagementFileManyWithBytes(
  body: API.CreateManyFileInput,
  options?: { [key: string]: any }
) {
  return request<API.CreateManyFileOutput>(
    "/api/file-management/file/many/with-bytes",
    {
      method: "POST",
      headers: {
        "Content-Type": "application/json",
      },
      data: body,
      ...(options || {}),
    }
  );
}

/** 此处后端没有提供注释 POST /api/file-management/file/many/with-stream */
export async function postApiFileManagementFileManyWithStream(
  // 叠加生成的Param类型 (非body参数swagger默认没有生成对象)
  params: API.postApiFileManagementFileManyWithStreamParams,
  body: {},
  FileContents?: File[],
  options?: { [key: string]: any }
) {
  const formData = new FormData();

  if (FileContents) {
    FileContents.forEach((f) => formData.append("FileContents", f || ""));
  }

  Object.keys(body).forEach((ele) => {
    const item = (body as any)[ele];

    if (item !== undefined && item !== null) {
      // 注意：此处用 Object.prototype.toString 判别 File 而非 item instanceof File。
        // 本文件由 openapi 生成器产出（npm run openapi 会覆盖手工改动，需同步生成模板）；
        // @types/node 的 File 声明会把 DOM File 遮蔽成纯类型，instanceof 直接编译报
        // TS2359（pnpm tsc 存量失败的根因，见 docs/TEST_EVIDENCE_2026-09-21.md D6）。
        if (typeof item === "object" && Object.prototype.toString.call(item) !== "[object File]") {
        if (item instanceof Array) {
          item.forEach((f) => formData.append(ele, f || ""));
        } else {
          formData.append(
            ele,
            new Blob([JSON.stringify(item)], { type: "application/json" })
          );
        }
      } else {
        formData.append(ele, item);
      }
    }
  });

  return request<API.CreateManyFileOutput>(
    "/api/file-management/file/many/with-stream",
    {
      method: "POST",
      params: {
        ...params,
        ExtraProperties: undefined,
        ...params["ExtraProperties"],
      },
      data: formData,
      requestType: "form",
      ...(options || {}),
    }
  );
}

/** 此处后端没有提供注释 POST /api/file-management/file/with-bytes */
export async function postApiFileManagementFileWithBytes(
  body: API.CreateFileInput,
  options?: { [key: string]: any }
) {
  return request<API.CreateFileOutput>("/api/file-management/file/with-bytes", {
    method: "POST",
    headers: {
      "Content-Type": "application/json",
    },
    data: body,
    ...(options || {}),
  });
}

/** 此处后端没有提供注释 POST /api/file-management/file/with-stream */
export async function postApiFileManagementFileWithStream(
  // 叠加生成的Param类型 (非body参数swagger默认没有生成对象)
  params: API.postApiFileManagementFileWithStreamParams,
  body: {},
  Content?: File,
  options?: { [key: string]: any }
) {
  const formData = new FormData();

  if (Content) {
    formData.append("Content", Content);
  }

  Object.keys(body).forEach((ele) => {
    const item = (body as any)[ele];

    if (item !== undefined && item !== null) {
      // 注意：此处用 Object.prototype.toString 判别 File 而非 item instanceof File。
        // 本文件由 openapi 生成器产出（npm run openapi 会覆盖手工改动，需同步生成模板）；
        // @types/node 的 File 声明会把 DOM File 遮蔽成纯类型，instanceof 直接编译报
        // TS2359（pnpm tsc 存量失败的根因，见 docs/TEST_EVIDENCE_2026-09-21.md D6）。
        if (typeof item === "object" && Object.prototype.toString.call(item) !== "[object File]") {
        if (item instanceof Array) {
          item.forEach((f) => formData.append(ele, f || ""));
        } else {
          formData.append(
            ele,
            new Blob([JSON.stringify(item)], { type: "application/json" })
          );
        }
      } else {
        formData.append(ele, item);
      }
    }
  });

  return request<API.CreateFileOutput>(
    "/api/file-management/file/with-stream",
    {
      method: "POST",
      params: {
        ...params,
        ExtraProperties: undefined,
        ...params["ExtraProperties"],
      },
      data: formData,
      requestType: "form",
      ...(options || {}),
    }
  );
}
