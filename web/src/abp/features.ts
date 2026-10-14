/**
 * 功能管理（Feature Management）读写。
 *
 * - providerName/providerKey：Host 功能与租户功能分 provider；Host 列表勿传空字符串 providerKey
 *   （OSS FeatureAppService 会当成租户侧），应省略该查询参数——见 getFeatures 实现。
 * - 与 permissions 类似：本文件管「功能开关值」，access.ts 管「权限策略」。
 */
import { request } from '@umijs/max';

export type FeatureDto = {
  name: string;
  displayName?: string;
  value?: string;
  description?: string;
  parentName?: string;
  valueType?: { name?: string };
};

export type FeatureGroup = {
  name: string;
  displayName?: string;
  features?: FeatureDto[];
};

/**
 * providerKey 为 undefined 时必须整体省略查询参数（不能发 providerKey=）：
 * OSS FeatureAppService 的 Host 功能分支是严格的 providerKey == null（C# null），
 * 空字符串会绑定成 "" 落到租户策略映射——Host 功能被错误授权且写入 (T, "") 死行，
 * 任何解析路径都读不到（host 解析用 ProviderKey=null）。
 */
function providerKeyParams(providerName: string, providerKey?: string) {
  return providerKey === undefined
    ? { providerName }
    : { providerName, providerKey };
}

export async function getFeatures(providerName: string, providerKey?: string) {
  return request<{ groups: FeatureGroup[] }>(
    '/api/feature-management/features',
    {
      method: 'GET',
      params: providerKeyParams(providerName, providerKey),
    },
  );
}

export async function updateFeatures(
  providerName: string,
  providerKey: string | undefined,
  features: { name: string; value: string }[],
) {
  return request('/api/feature-management/features', {
    method: 'PUT',
    params: providerKeyParams(providerName, providerKey),
    data: { features },
  });
}

// 恢复默认：删除该 provider 下全部功能值（后端逐项失效功能值缓存）
export async function deleteFeatures(
  providerName: string,
  providerKey?: string,
) {
  return request('/api/feature-management/features', {
    method: 'DELETE',
    params: providerKeyParams(providerName, providerKey),
  });
}
