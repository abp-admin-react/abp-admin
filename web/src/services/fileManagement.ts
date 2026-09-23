/**
 * EasyAbp.FileManagement 语义化 API 封装。
 *
 * 类型全部复用 pnpm openapi 生成的 typings（API.FileInfoDto 等），
 * 页面不直接依赖生成产物；本模块按生成代码相同的 URL/契约直接用 request 实现
 * （生成桶文件 src/services/abpadmin/index.ts 已在 tsconfig 中排除）。
 */
import { request } from '@umijs/max';

/** 与后端 AdminFileManagementContainer 上的 [FileContainerName("admin")] 对应 */
export const FILE_CONTAINER_NAME = 'admin';

/** FileType 枚举（后端 [Flags]：Directory=1, RegularFile=2） */
export const FileType = {
  Directory: 1 as API.FileType,
  RegularFile: 2 as API.FileType,
};

export type FileListParams = {
  parentId?: string;
  /** 只查目录（目录树懒加载用） */
  directoryOnly?: boolean;
  filter?: string;
  sorting?: string;
  skipCount?: number;
  maxResultCount?: number;
};

/** 获取文件/目录列表 */
export async function getFileList(params: FileListParams) {
  return request<API.PagedResultDto1FileInfoDto>('/api/file-management/file', {
    method: 'GET',
    params: {
      FileContainerName: FILE_CONTAINER_NAME,
      ParentId: params.parentId,
      DirectoryOnly: params.directoryOnly,
      Filter: params.filter,
      Sorting: params.sorting,
      SkipCount: params.skipCount,
      MaxResultCount: params.maxResultCount,
    },
  });
}

/** 获取容器公开配置（上传预校验用：大小限制、扩展名白名单等） */
export async function getFileConfiguration() {
  return request<API.PublicFileContainerConfiguration>(
    '/api/file-management/file/configuration',
    {
      method: 'GET',
      params: { fileContainerName: FILE_CONTAINER_NAME },
    },
  );
}

/**
 * multipart 上传单个文件。
 * FormData 字段与生成函数一致（FileContainerName/FileType/ParentId/GenerateUniqueFileName/File），
 * 不要手动设置 Content-Type，浏览器会自动带 boundary。
 */
export async function uploadFile(file: File, parentId?: string) {
  const formData = new FormData();
  formData.append('FileContainerName', FILE_CONTAINER_NAME);
  formData.append('FileType', String(FileType.RegularFile));
  formData.append('GenerateUniqueFileName', 'false');
  if (parentId !== undefined) {
    formData.append('ParentId', parentId);
  }
  formData.append('File', file);
  return request<API.CreateFileOutput>('/api/file-management/file', {
    method: 'POST',
    data: formData,
    requestType: 'form',
  });
}

/** 新建目录（with-bytes 端点，Content 传 undefined，FileType=Directory） */
export async function createDirectory(name: string, parentId?: string) {
  return request<API.CreateFileOutput>(
    '/api/file-management/file/with-bytes',
    {
      method: 'POST',
      headers: {
        'Content-Type': 'application/json',
      },
      data: {
        fileContainerName: FILE_CONTAINER_NAME,
        parentId,
        fileName: name,
        fileType: FileType.Directory,
      } satisfies API.CreateFileInput,
    },
  );
}

/** 获取下载信息（两步下载第一步：拿 downloadUrl + token） */
export async function getDownloadInfo(id: string) {
  return request<API.FileDownloadInfoModel>(
    `/api/file-management/file/${id}/download-info`,
    { method: 'GET' },
  );
}

/** 重命名 */
export async function renameFile(id: string, fileName: string) {
  return request<API.FileInfoDto>(`/api/file-management/file/${id}/info`, {
    method: 'PUT',
    headers: {
      'Content-Type': 'application/json',
    },
    data: { fileName } satisfies API.UpdateFileInfoInput,
  });
}

/**
 * 移动文件/目录。
 * 注意：MoveFileInput.NewFileName 是 [Required]，即使不改名也必须传当前文件名。
 */
export async function moveFile(
  id: string,
  newParentId: string | undefined,
  newFileName: string,
) {
  return request<API.FileInfoDto>(`/api/file-management/file/${id}/move`, {
    method: 'PUT',
    headers: {
      'Content-Type': 'application/json',
    },
    data: { newParentId, newFileName } satisfies API.MoveFileInput,
  });
}

/** 删除文件/目录 */
export async function deleteFile(id: string) {
  return request<any>(`/api/file-management/file/${id}`, {
    method: 'DELETE',
  });
}
