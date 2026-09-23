// 页面级 API 出口：实现见 @/abp/dataDictionary（项目约定：页面不直接调生成的客户端）
// 字典管理面端点：列表/创建/删除/按码视图/原子保存，全部走自建 /api/app/data-dictionary-view/*；
// EasyAbp 模块端点（全量替换、无静态结构锁）已随模块 HttpApi 下线，页面不再提供其包装入口。
export {
  createDataDictionary,
  deleteDataDictionary,
  getDataDictionaries,
  getDataDictionaryByCode,
  saveDataDictionaryItems,
} from '@/abp/dataDictionary';

export type { DataDictionaryListItem } from '@/abp/dataDictionary';
