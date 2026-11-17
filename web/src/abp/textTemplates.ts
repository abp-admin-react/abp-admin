import { request } from '@umijs/max';

const path = '/api/app/text-template';

export type TextTemplate = {
  name: string;
  displayName: string | null;
  isLayout: boolean;
  layout: string | null;
  cultureName: string | null;
  content: string | null;
  isSandboxed: boolean;
};

export function getTextTemplates(filter?: string) {
  return request<TextTemplate[]>(path, { params: { Filter: filter } });
}

export function getTextTemplateContent(name: string, cultureName?: string) {
  return request<TextTemplate>(`${path}/content`, {
    params: { Name: name, CultureName: cultureName },
  });
}

type TemplateContentKey = { name: string; cultureName?: string | null };

export function updateTextTemplate(
  data: TemplateContentKey & { content: string },
) {
  return request<void>(path, { method: 'PUT', data });
}

export function restoreTextTemplate(data: TemplateContentKey) {
  return request<void>(`${path}/restore-to-default`, { method: 'POST', data });
}
