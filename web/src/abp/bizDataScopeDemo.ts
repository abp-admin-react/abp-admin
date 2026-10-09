import { request } from '@umijs/max';

/**
 * 业务模块数据权限示例（AbpAdmin.Biz.Template）的 API 镜像。
 * 端点由模块 ConventionalControllers 自动暴露（/api/app/biz-data-scope-demo），
 * 生成器不可信（见 docs/framework-contracts.md §3），按 src/abp 手写镜像约定书写。
 */

export type BizDataScopeDemoDto = {
  id: string;
  name: string;
  organizationUnitId?: string;
  creationTime: string;
  creatorId?: string;
};

export type CreateBizDataScopeDemoDto = {
  name: string;
  organizationUnitId?: string;
};

export type PagedResult<T> = {
  items: T[];
  totalCount: number;
};

/** 示例数据分页列表（后端按当前用户的数据范围过滤——服务端零过滤代码） */
export async function getBizDataScopeDemos(params: {
  skipCount?: number;
  maxResultCount?: number;
  sorting?: string;
}) {
  return request<PagedResult<BizDataScopeDemoDto>>('/api/app/biz-data-scope-demo', {
    method: 'GET',
    params: {
      SkipCount: params.skipCount ?? 0,
      MaxResultCount: params.maxResultCount ?? 100,
      Sorting: params.sorting,
    },
  });
}

/** 新建示例数据（不选组织时后端按当前数据范围自动归属） */
export async function createBizDataScopeDemo(data: CreateBizDataScopeDemoDto) {
  return request<BizDataScopeDemoDto>('/api/app/biz-data-scope-demo', {
    method: 'POST',
    data,
  });
}

/** 删除示例数据 */
export async function deleteBizDataScopeDemo(id: string) {
  return request(`/api/app/biz-data-scope-demo/${id}`, {
    method: 'DELETE',
  });
}
