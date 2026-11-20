import { type GridQuery, postGridQuery } from './gridQuery';
import { request } from '@umijs/max';
import { isNotFound } from '@/requestErrorConfig';

/**
 * 数据字典 API。用自建视图保留字典项的静态结构和展示元数据。
 */
const path = '/api/app/data-dictionary-view';

/** 字典列表项（管理页左列；不含 Items）。 */
export type DataDictionaryListItem = {
  id: string;
  code: string;
  displayText: string;
  description: string | null;
  isStatic: boolean;
};

type DataDictionaryView = Pick<DataDictionaryListItem, 'code' | 'displayText' | 'description' | 'isStatic'> & {
  items: DataDictionaryItemView[];
};

export type DataDictionaryItemView = {
  code: string;
  displayText: string;
  description: string | null;
  isStatic: boolean;
  tagType: string | null;
  order: number;
};

/** 原子保存字典项的条目形状（display 与 meta 一起提交）；null 与 undefined 等价"无"。 */
export type DataDictionaryItemSaveInput = {
  code: string;
  displayText: string;
  description?: string | null;
  tagType?: string | null;
};

/** 字典合并视图（带 isStatic/tagType/order）。前端所有字典消费统一走这个自建端点，
 * 不直连模块端点（模块 DTO 没有 isStatic/tagType，且读取也要模块权限）。 */
export async function getDataDictionaryByCode(code: string): Promise<DataDictionaryView | null> {
  try {
    // ASP.NET Core 将空响应处理成 204，React Query 需要明确的 null。
    return (await request<DataDictionaryView | null>(`${path}/${encodeURIComponent(code)}`)) ?? null;
  } catch (error) {
    if (isNotFound(error)) return null;
    throw error;
  }
}

/**
 * 字典分页列表（管理页左列）。服务端按 Code 排序，需要 DataDictionary.Default 权限。
 * 分页参数在此换算成 ABP 口径：current/pageSize → skipCount/maxResultCount（0 起计、按条数跳过）。
 * 响应做 null 兜底：items/totalCount 缺失时归一为 []/0，调用方无需再判空。
 */
export async function getDataDictionaries(params: {
  current?: number;
  pageSize?: number;
}): Promise<{ items: DataDictionaryListItem[]; totalCount: number }> {
  const maxResultCount = params.pageSize ?? 20;
  const skipCount = ((params.current ?? 1) - 1) * maxResultCount;
  const result = await request<{
    totalCount: number;
    items: DataDictionaryListItem[];
  }>(path, {
    method: 'GET',
    params: { SkipCount: skipCount, MaxResultCount: maxResultCount },
  });
  return { items: result.items ?? [], totalCount: result.totalCount ?? 0 };
}

/**
 * 创建非静态字典（静态字典只能由代码定义，API 不开放 IsStatic），返回创建后的合并视图。
 * 响应原样透传，不做 getDataDictionaries 那样的 null 兜底；code 在请求体里，不经 URL。
 */
export async function createDataDictionary(input: {
  code: string;
  displayText: string;
  description?: string | null;
}) {
  return request<DataDictionaryView>(path, {
    method: 'POST',
    data: input,
  });
}

/**
 * 删除字典（按编码；静态字典被服务端拒绝，非静态字典的展示元数据随删）。
 * code 拼进路径段，先 encodeURIComponent——编码合法字符集比 URL 路径段安全集宽。
 */
export async function deleteDataDictionary(code: string) {
  return request<void>(`${path}/${encodeURIComponent(code)}`, {
    method: 'DELETE',
  });
}

/**
 * 原子保存字典显示信息与全部字典项（PUT /api/app/data-dictionary-view/{code}/items）：
 * 显示信息 + 全部项 + 展示元数据同一后端工作单元提交。
 * Items 是全量替换语义：不在列表里的项会被删除（静态字典有服务端结构锁，整单拒绝）；
 * Order 取数组顺序。返回保存后的合并视图。
 */
export async function saveDataDictionaryItems(
  dictionaryCode: string,
  input: {
    displayText: string;
    description?: string | null;
    items: DataDictionaryItemSaveInput[];
  },
) {
  return request<DataDictionaryView>(`${path}/${encodeURIComponent(dictionaryCode)}/items`, {
    method: 'PUT',
    data: input,
  });
}

export function queryDataDictionaries(input: GridQuery, signal: AbortSignal) {
  return postGridQuery<DataDictionaryListItem>(
    '/api/app/data-dictionary-view/query',
    input,
    signal,
  );
}
