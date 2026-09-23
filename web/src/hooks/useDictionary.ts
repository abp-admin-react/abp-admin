import { useQuery } from '@tanstack/react-query';
import { getDataDictionaryByCode } from '@/abp/dataDictionary';

export interface DictionaryItem {
  code: string;
  displayText: string;
  description?: string;
  /** 标签颜色等展示元数据，来自自建端点（T3.4 第 12 步的 AppDataDictionaryItemMetas） */
  tagType?: string;
}

/** 字典数据变化频率极低，缓存 10 分钟，窗口聚焦不重取。 */
const DICTIONARY_QUERY_OPTIONS = {
  staleTime: 10 * 60 * 1000,
  gcTime: 30 * 60 * 1000,
  refetchOnWindowFocus: false,
  retry: 1,
} as const;

/** 字典的 react-query key。管理页保存后用 ['data-dictionary'] 前缀 invalidate。 */
export const dictionaryQueryKey = (code: string) =>
  ['data-dictionary', code] as const;

/**
 * 组件内消费字典的 hook（T3.4 第 8 步）。
 * 列定义在组件外、用不了 hook 的场景用 @/utils/dictionary 的 dictionaryRequest。
 */
export function useDictionary(code: string) {
  const query = useQuery({
    queryKey: dictionaryQueryKey(code),
    queryFn: () => getDataDictionaryByCode(code),
    enabled: !!code,
    ...DICTIONARY_QUERY_OPTIONS,
  });

  return {
    items: query.data?.items ?? [],
    loading: query.isLoading,
    /** 给 ProTable 的 valueEnum 用（同步场景；color 让 ProTable 直接渲染带颜色的标签） */
    valueEnum: toValueEnum(query.data?.items),
    /** 给 ProFormSelect / Select 的 options 用 */
    options: toOptions(query.data?.items),
  };
}

function toValueEnum(items?: API.DataDictionaryItemViewDto[]) {
  return Object.fromEntries(
    (items ?? []).map((it) => [
      it.code ?? '',
      { text: it.displayText, color: it.tagType ?? undefined },
    ]),
  );
}

function toOptions(items?: API.DataDictionaryItemViewDto[]) {
  return (items ?? []).map((it) => ({
    label: it.displayText ?? it.code ?? '',
    value: it.code ?? '',
  }));
}
