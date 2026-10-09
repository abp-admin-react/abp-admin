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
  /** 显式指定时后端校验归属合法性：组织必须在当前数据范围内（All 范围退化为校验存在性，
   * SelfOnly 无可见组织集合则任何显式组织都拒）。越权/不存在会被拒并返回友好错误
   * （本地化键 BizDataScope:OrganizationUnitNotFound / OrganizationUnitOutOfScope）。 */
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

/**
 * 新建示例数据。组织归属两条路径，与后端写入侧语义一一对齐：
 * - 留空 organizationUnitId：DbContext 写入侧自动填充当前快照第一个可见组织，算不出组织抛业务异常；
 * - 显式指定：AppService 校验在当前数据范围内（见 CreateBizDataScopeDemoDto 注释），不在即拒。
 */
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
