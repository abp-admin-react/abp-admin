/** 实体变更类型（EntityChange.ChangeType）→ 展示文案的唯一出处。 */
const CHANGE_TYPE_TEXT: Record<number, string> = {
  0: '新增',
  1: '修改',
  2: '删除',
};

export const changeTypeText = (value?: number): string =>
  CHANGE_TYPE_TEXT[value ?? -1] ?? String(value ?? '-');
