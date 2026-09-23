import { request } from '@umijs/max';

export type EmailSettingsDto = {
  smtpHost?: string;
  smtpPort?: number;
  smtpUserName?: string;
  smtpPassword?: string;
  smtpDomain?: string;
  smtpEnableSsl?: boolean;
  smtpUseDefaultCredentials?: boolean;
  defaultFromAddress?: string;
  defaultFromDisplayName?: string;
};

export type SendTestEmailInput = {
  senderEmailAddress: string;
  targetEmailAddress: string;
  subject: string;
  body?: string;
};

export type NameValue = {
  name?: string;
  value?: string;
};

export async function getEmailSettings() {
  return request<EmailSettingsDto>('/api/setting-management/emailing', {
    method: 'GET',
  });
}

export async function updateEmailSettings(data: EmailSettingsDto) {
  // ABP 的 EmailingAppService.UpdateAsync 是 POST，不是 PUT（PUT 会 405）
  return request('/api/setting-management/emailing', {
    method: 'POST',
    data,
  });
}

export async function sendTestEmail(data: SendTestEmailInput) {
  return request('/api/setting-management/emailing/send-test-email', {
    method: 'POST',
    data,
  });
}

export async function getTimezone() {
  return request<string>('/api/setting-management/timezone', {
    method: 'GET',
  });
}

export async function getTimezones() {
  return request<NameValue[]>('/api/setting-management/timezone/timezones', {
    method: 'GET',
  });
}

export async function updateTimezone(timezone: string) {
  return request('/api/setting-management/timezone', {
    method: 'POST',
    params: { timezone },
  });
}
