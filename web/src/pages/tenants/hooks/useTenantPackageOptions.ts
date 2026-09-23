import { useQuery } from '@tanstack/react-query';
import { getTenantPackages } from '@/abp/tenantPackages';

/** 套餐下拉一次拉取的上限（下拉选择用；套餐超过该数量时应提供后端 lookup 端点）。 */
const PACKAGE_SELECT_MAX = 100;

export type TenantPackageOption = {
  value: string;
  label: string;
};

/**
 * 套餐下拉选项（新建租户 / 应用套餐两处共用）。
 * 走 react-query 缓存（同一 queryKey 只发一次请求），替代两处逐字重复的
 * getTenantPackages({current:1,pageSize:100}) + map 拼装。
 */
export function useTenantPackageOptions() {
  const query = useQuery({
    queryKey: ['tenant-package-options', PACKAGE_SELECT_MAX],
    queryFn: async (): Promise<TenantPackageOption[]> => {
      const res = await getTenantPackages({
        current: 1,
        pageSize: PACKAGE_SELECT_MAX,
      });
      return res.items.map((x) => ({
        value: x.id,
        label: `${x.name}（${x.menuCount} 个菜单）`,
      }));
    },
    staleTime: 60_000,
  });

  return { options: query.data ?? [], loading: query.isLoading };
}
