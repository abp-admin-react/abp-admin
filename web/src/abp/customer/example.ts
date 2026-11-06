/**
 * Biz.Template 客户入口手写函数（/api/client/biz-template/projects）。
 * DTO 仅在本文件定义；传输与错误处理只使用 createCustomerRequest 类型。
 * 后端返回项目数组，没有分页包装，也不下发 isActive。
 */
import type { CustomerRequestFn, CustomerRequestOptions } from './request';

export type ClientBizProjectDto = {
  id: string;
  name: string;
  description: string | null;
};

type CustomerCallOptions = Pick<
  CustomerRequestOptions,
  'display' | 'skipErrorHandler' | 'signal' | 'timeoutMs'
>;

const PROJECTS_URL = '/api/client/biz-template/projects';

export function getClientBizProjectList(
  request: CustomerRequestFn,
  options?: CustomerCallOptions,
): Promise<ClientBizProjectDto[]> {
  return request<ClientBizProjectDto[]>(PROJECTS_URL, {
    method: 'GET',
    ...options,
  });
}

export function getClientBizProject(
  request: CustomerRequestFn,
  id: string,
  options?: CustomerCallOptions,
): Promise<ClientBizProjectDto> {
  return request<ClientBizProjectDto>(
    `${PROJECTS_URL}/${encodeURIComponent(id)}`,
    {
      method: 'GET',
      ...options,
    },
  );
}
