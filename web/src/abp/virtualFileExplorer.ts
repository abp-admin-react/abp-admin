import { request } from '@umijs/max';
import { type GridQuery, postGridQuery } from './gridQuery';

export interface VirtualFileItemDto {
  name: string;
  path: string;
  isDirectory: boolean;
  exists: boolean;
  length: number;
}
export interface VirtualFileContentDto {
  name: string;
  path: string;
  length: number;
  isBinary: boolean;
  truncated: boolean;
  content: string | null;
}
export function queryVirtualFiles(
  path: string,
  input: GridQuery,
  signal: AbortSignal,
) {
  const params = new URLSearchParams({ Path: path });
  return postGridQuery<VirtualFileItemDto>(
    `/api/app/virtual-file-explorer/query?${params}`,
    input,
    signal,
  );
}
export function getVirtualFileContent(path: string) {
  return request<VirtualFileContentDto>(
    '/api/app/virtual-file-explorer/content',
    { params: { Path: path } },
  );
}
