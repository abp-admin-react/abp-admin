import { getDataDictionaryByCode } from '@/abp/dataDictionary';
import { dictionaryQueryKey } from '@/hooks/useDictionary';
import { queryClient } from '@/queryClient';

/**
 * 给 ProTable 列定义 / ProFormSelect 用的字典异步数据源（T3.4 第 8 步）。
 *
 * 一个版本差异要写明（实测 @ant-design/pro-components 3.x 的类型）：
 * 规格里的 dictionaryValueEnum（返回 Promise 的 valueEnum）在这个版本不成立——
 * valueEnum 只接受同步的 Map/Record（ProSchemaValueEnumObj | ProSchemaValueEnumMap），
 * 异步取数的官方通道是列/表单项的 request 属性（ProFieldRequestData）。
 * 所以本文件只提供 request 形态；组件内部要 valueEnum 时用 useDictionary hook。
 *
 * 走 queryClient.fetchQuery 复用 useDictionary 的同一份缓存（queryKey 相同），不会重复请求。
 */
export function dictionaryRequest(code: string) {
  return async () => {
    const data = await queryClient.fetchQuery({
      queryKey: dictionaryQueryKey(code),
      queryFn: () => getDataDictionaryByCode(code),
      staleTime: 10 * 60 * 1000,
    });

    return (data?.items ?? []).map((it) => ({
      label: it.displayText ?? it.code ?? '',
      value: it.code ?? '',
    }));
  };
}
