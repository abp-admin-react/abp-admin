import { request } from '@umijs/max';
import { getApplicationConfiguration } from './config';

const languagePath = '/api/app/language';
const textPath = '/api/app/language-text';

export type EnabledLanguage = { cultureName: string; displayName: string };

export type LanguageData = EnabledLanguage & {
  uiCultureName: string;
  flagIcon: string | null;
  isEnabled: boolean;
};

export type Language = LanguageData & {
  id: string;
  isDefault: boolean;
  creationTime: string;
  creatorId: string | null;
  lastModificationTime: string | null;
  lastModifierId: string | null;
};

export type LanguageUpdate = Pick<
  LanguageData,
  'displayName' | 'flagIcon' | 'isEnabled'
>;

export type LanguageText = {
  id: string | null;
  resourceName: string;
  cultureName: string;
  name: string;
  value: string;
  baseValue: string | null;
  isOverridden: boolean;
  isReadOnly: boolean;
};

type LanguageTextKey = Pick<
  LanguageText,
  'resourceName' | 'cultureName' | 'name'
>;

/** 选择语言只需要启用项，来自 ABP 应用配置的 ILanguageProvider。 */
export async function getEnabledLanguages() {
  return (await getApplicationConfiguration()).localization?.languages ?? [];
}

export function getLanguages() {
  return request<{ items: Language[] }>(languagePath);
}

export function createLanguage(data: LanguageData) {
  return request<Language>(languagePath, { method: 'POST', data });
}

export function updateLanguage(id: string, data: LanguageUpdate) {
  return request<Language>(`${languagePath}/${id}`, { method: 'PUT', data });
}

export function deleteLanguage(id: string) {
  return request<void>(`${languagePath}/${id}`, { method: 'DELETE' });
}

export function setDefaultLanguage(id: string) {
  return request<void>(`${languagePath}/${id}/set-as-default`, {
    method: 'POST',
  });
}

export function getLanguageTextResourceNames() {
  return request<{ items: string[] }>(`${textPath}/resource-names`);
}

export function getLanguageTexts(input: {
  resourceName?: string;
  cultureName: string;
  baseCultureName?: string;
  filter?: string;
  onlyEmpty: boolean;
  skipCount: number;
  maxResultCount: number;
}) {
  return request<{ items: LanguageText[]; totalCount: number }>(textPath, {
    params: {
      ResourceName: input.resourceName,
      CultureName: input.cultureName,
      BaseCultureName: input.baseCultureName,
      Filter: input.filter,
      OnlyEmpty: input.onlyEmpty,
      SkipCount: input.skipCount,
      MaxResultCount: input.maxResultCount,
    },
  });
}

export function updateLanguageText(data: LanguageTextKey & { value: string }) {
  return request<LanguageText>(textPath, { method: 'PUT', data });
}

export function restoreLanguageText(input: LanguageTextKey) {
  return request<void>(`${textPath}/restore-to-default`, {
    method: 'POST',
    params: {
      ResourceName: input.resourceName,
      CultureName: input.cultureName,
      Name: input.name,
    },
  });
}
