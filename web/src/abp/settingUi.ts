import {
  getApiSettingUi,
  putApiSettingUiResetSettingValues,
  putApiSettingUiSetSettingValues,
} from '@/services/abpadmin/settingUi';

/**
 * EasyAbp.Abp.SettingUi 语义化封装（T1.4）
 *
 * SettingGroup 是一级分组（Group1），SettingInfo.properties 里含 Group2/Type/Options。
 * Type 值是小写：text/number/checkbox/select/date/dateTime。
 * Options 值是竖线分隔字符串："|val1|val2|val3"（开头有竖线）。
 * 所有设置值都是 string，布尔转 "true"/"false"，数字转十进制字符串。
 */

/** SettingInfo.properties 里的键名（SettingUiConst 常量值） */
export const SettingUiPropertyKeys = {
  Group1: 'Group1',
  Group2: 'Group2',
  Type: 'Type',
  Options: 'Options',
} as const;

/** SettingInfo.properties.Type 的可选值（SettingUiConst.Components + T3.5 扩展的 password） */
export const SettingUiComponentTypes = {
  Text: 'text',
  Number: 'number',
  Checkbox: 'checkbox',
  Select: 'select',
  Date: 'date',
  DateTime: 'dateTime',
  // T3.5：加密设置项（SMTP 密码、短信 SecretKey）用密码框。
  // 后端对 isEncrypted 设置项永不下发明文（AbpAdminSettingUiAppService 脱敏），
  // 该控件永远显示空，输入新值提交才修改，留空保存 = 不修改。
  Password: 'password',
} as const;

export type SettingUiComponentType =
  (typeof SettingUiComponentTypes)[keyof typeof SettingUiComponentTypes];

/** 从 SettingInfo 读取 Type（缺省按 text 处理） */
export function getSettingType(info: API.SettingInfo): SettingUiComponentType {
  const t = info.properties?.[SettingUiPropertyKeys.Type];
  if (
    t === SettingUiComponentTypes.Number ||
    t === SettingUiComponentTypes.Checkbox ||
    t === SettingUiComponentTypes.Select ||
    t === SettingUiComponentTypes.Date ||
    t === SettingUiComponentTypes.DateTime ||
    t === SettingUiComponentTypes.Password
  ) {
    return t;
  }
  return SettingUiComponentTypes.Text;
}

/** 从 SettingInfo 读取 Group2（缺省返回空串，调用方归到默认卡片） */
export function getSettingGroup2(info: API.SettingInfo): string {
  return (info.properties?.[SettingUiPropertyKeys.Group2] as string) || '';
}

/** 从 SettingInfo 读取 Select 选项（"|a|b|c" → [{label,value}]） */
export function getSettingOptions(
  info: API.SettingInfo,
): { label: string; value: string }[] {
  const raw = info.properties?.[SettingUiPropertyKeys.Options] as string;
  if (!raw) return [];
  return raw
    .split('|')
    .map((s) => s.trim())
    .filter(Boolean)
    .map((s) => ({ label: s, value: s }));
}

/** 拉取全部设置分组（一级 Tab） */
export async function getSettingGroups(): Promise<API.SettingGroup[]> {
  return getApiSettingUi();
}

/**
 * 批量保存设置值
 *
 * SettingUi 后端期望的键名格式：Setting_Abp_Admin_SiteTitle
 * （FormNamePrefix="Setting_"，点号转下划线）
 *
 * @param values 键是设置名（如 AbpAdmin.SiteTitle），值是字符串
 */
export async function setSettingValues(
  values: Record<string, string>,
): Promise<void> {
  const payload: Record<string, string> = {};
  for (const [name, value] of Object.entries(values)) {
    // AbpAdmin.SiteTitle → Setting_AbpAdmin_SiteTitle
    const key = `Setting_${name.replace(/\./g, '_')}`;
    payload[key] = value;
  }
  await putApiSettingUiSetSettingValues(payload);
}

/** 重置指定设置为默认值（body 是设置名数组） */
export async function resetSettingValues(
  settingNames: string[],
): Promise<void> {
  await putApiSettingUiResetSettingValues(settingNames);
}

/** 把表单值（boolean/number/string）统一转成 string 提交 */
export function toSettingString(value: unknown): string {
  if (typeof value === 'boolean') return value ? 'true' : 'false';
  if (value === null || value === undefined) return '';
  return String(value);
}

/** 把后端 string 值按控件类型转回表单值 */
export function fromSettingString(
  value: string | undefined,
  type: SettingUiComponentType,
): string | number | boolean | undefined {
  if (value === undefined || value === null) return undefined;
  switch (type) {
    case SettingUiComponentTypes.Checkbox:
      return value.toLowerCase() === 'true';
    case SettingUiComponentTypes.Number: {
      // 空串要先拦：Number('') === 0，不拦会把"清空"变成"0"渲染出来并被保存回写
      if (value.trim() === '') return undefined;
      const n = Number(value);
      return Number.isNaN(n) ? undefined : n;
    }
    default:
      return value;
  }
}
