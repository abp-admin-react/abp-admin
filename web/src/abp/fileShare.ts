import { request } from '@umijs/max';

export type FileShareLinkDto = {
  id: string;
  fileId: string;
  token: string;
  expireTime: string;
  isPublic: boolean;
  downloadCount: number;
  maxDownloads?: number;
  downloadUrl: string;
};

export async function createFileShare(data: {
  fileId: string;
  expireTime?: string;
  isPublic?: boolean;
  maxDownloads?: number;
}) {
  return request<FileShareLinkDto>('/api/app/file-share', {
    method: 'POST',
    data,
  });
}

export async function getFileShares(fileId: string) {
  return request<{ items: FileShareLinkDto[] }>(
    `/api/app/file-share?fileId=${encodeURIComponent(fileId)}`,
    { method: 'GET' },
  );
}

export async function deleteFileShare(id: string) {
  return request(`/api/app/file-share/${id}`, { method: 'DELETE' });
}

export function getAnonymousShareUrl(downloadUrl: string) {
  return downloadUrl.startsWith('http')
    ? downloadUrl
    : `${window.location.origin}${downloadUrl}`;
}
