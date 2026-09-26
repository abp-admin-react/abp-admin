/**
 * ABP 标准排序参数（`Sorting` 查询参数，如 "UserName desc"）与
 * ProTable request 第二参数 sorter 的互转工具。
 *
 * 列定义时用字符串 sorter 指定服务端排序字段（需与后端实体属性一致，
 * 如 `sorter: 'UserName'`）；纯函数 sorter 会被 ProTable 视为本地排序，
 * 不会出现在 request 的 sorter 参数里。
 */

export type ProSorterMap = Record<
  string,
  'ascend' | 'descend' | null | undefined
>;

/** ProTable 的 sorter 对象 → ABP sorting 字符串（"UserName desc,CreationTime asc"） */
export function sorterToAbpSorting(
  sorter?: ProSorterMap | null,
): string | undefined {
  const parts = Object.entries(sorter ?? {})
    .filter(
      (entry): entry is [string, 'ascend' | 'descend'] =>
        entry[1] === 'ascend' || entry[1] === 'descend',
    )
    .map(([field, order]) => `${field} ${order === 'ascend' ? 'asc' : 'desc'}`);
  return parts.length > 0 ? parts.join(',') : undefined;
}
