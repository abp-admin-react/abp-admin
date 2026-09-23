import { request } from '@umijs/max';

/**
 * T3.1 图片处理（用户头像 + 文件缩略图）语义化 API 封装。
 *
 * 头像/缩略图的 URL 规则全部由后端下发（avatarUrl / thumbnailUrls），
 * 页面不要自己拼 blob 路径——后端改命名规则时前端不需要动。
 */

export interface AvatarInfoDto {
  /** 形如 /api/app/profile-avatar/{userId}?v={version}；无头像时为 null */
  avatarUrl?: string | null;
}

/** 当前用户头像信息（上传后 URL 里的 v 会变，用于击穿缓存） */
export async function getMyAvatarInfo() {
  return request<AvatarInfoDto>('/api/app/profile-avatar/my-avatar-info', {
    method: 'GET',
  });
}

/** 上传并替换当前用户头像（multipart/form-data，字段名 File） */
export async function uploadAvatar(file: File) {
  const formData = new FormData();
  formData.append('File', file);
  return request('/api/app/profile-avatar/upload', {
    method: 'POST',
    data: formData,
    requestType: 'form',
  });
}

/** 删除当前用户头像 */
export async function deleteAvatar() {
  return request('/api/app/profile-avatar', { method: 'DELETE' });
}

export type FileListWithThumbnailsParams = {
  parentId?: string;
  sorting?: string;
  skipCount?: number;
  maxResultCount?: number;
};

export type FileListWithThumbnailsResult = {
  items: API.FileInfoDto[];
  totalCount: number;
  /** FileId → 缩略图 URL，仅后端 State == Done 的文件会出现 */
  thumbnailUrls: Record<string, string>;
};

/** 文件列表（透传 EasyAbp 列表）+ 本页文件的缩略图 URL 表 */
export async function getFileListWithThumbnails(
  fileContainerName: string,
  params: FileListWithThumbnailsParams,
) {
  return request<FileListWithThumbnailsResult>(
    '/api/app/file-thumbnail/with-thumbnails',
    {
      method: 'GET',
      params: {
        FileContainerName: fileContainerName,
        ParentId: params.parentId,
        Sorting: params.sorting,
        SkipCount: params.skipCount,
        MaxResultCount: params.maxResultCount,
      },
    },
  );
}
