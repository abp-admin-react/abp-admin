import { request } from '@umijs/max';
import {
  getApiAppDataDictionaryViewCode,
  putApiAppDataDictionaryViewDictionaryCodeItems,
} from '@/services/abpadmin/dataDictionaryView';

/**
 * 数据字典 API 包装（T3.4）。
 * 页面只调这里的包装函数，不直接调生成的客户端（参数适配在这层做）。
 *
 * 端点收口说明：EasyAbp 模块端点（/api/data-dictionary/*，UpdateAsync 全量替换、
 * 无静态结构锁）已随模块 HttpApi 依赖下线，字典的列表/创建/删除改走自建
 * /api/app/data-dictionary-view/*；列表/创建/删除暂为手写 request——
 * 待后端重启后 pnpm openapi 再生成，生成客户端出现同名端点后换成包装生成函数
 * （ getDataDictionaryByCode / saveDataDictionaryItems 已是生成函数形态）。
 */

/** 字典列表项（管理页左列；不含 Items）。 */
export type DataDictionaryListItem = {
  id: string;
  code: string;
  displayText: string;
  description?: string | null;
  isStatic: boolean;
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
export async function getDataDictionaryByCode(code: string) {
  return getApiAppDataDictionaryViewCode({ code });
}

/** 字典分页列表（管理页左列）。服务端按 Code 排序，需要 DataDictionary.Default 权限。 */
export async function getDataDictionaries(params: {
  current?: number;
  pageSize?: number;
}): Promise<{ items: DataDictionaryListItem[]; totalCount: number }> {
  const maxResultCount = params.pageSize ?? 20;
  const skipCount = ((params.current ?? 1) - 1) * maxResultCount;
  const result = await request<{ totalCount: number; items: DataDictionaryListItem[] }>(
    '/api/app/data-dictionary-view',
    { method: 'GET', params: { skipCount, maxResultCount } },
  );
  return { items: result.items ?? [], totalCount: result.totalCount ?? 0 };
}

/** 创建非静态字典（静态字典只能由代码定义，API 不开放 IsStatic），返回创建后的合并视图。 */
export async function createDataDictionary(input: {
  code: string;
  displayText: string;
  description?: string | null;
}) {
  return request('/api/app/data-dictionary-view', {
    method: 'POST',
    data: input,
  });
}

/** 删除字典（按编码；静态字典被服务端拒绝，非静态字典的展示元数据随删）。 */
export async function deleteDataDictionary(code: string) {
  return request(`/api/app/data-dictionary-view/${encodeURIComponent(code)}`, {
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
  input: { displayText: string; description?: string | null; items: DataDictionaryItemSaveInput[] },
) {
  // null 归一为 undefined：生成类型把可空字段声明为 `?: string`，null 直接传会过不了 tsc
  return putApiAppDataDictionaryViewDictionaryCodeItems(
    { dictionaryCode },
    {
      displayText: input.displayText,
      description: input.description ?? undefined,
      items: input.items.map((it) => ({
        code: it.code,
        displayText: it.displayText,
        description: it.description ?? undefined,
        tagType: it.tagType ?? undefined,
      })),
    },
  );
}
