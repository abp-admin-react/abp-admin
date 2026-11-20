vi.mock('ag-grid-react', () => import('@/../tests/gridTestDouble'));
vi.mock('@/abp/gridQuery', () => ({
  postGridQuery: (url: string, input: unknown) =>
    (url.includes('subscription') ? mocks.subscriptions : mocks.records)(input),
}));

import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { App } from 'antd';
import { beforeEach, expect, it, vi } from 'vitest';
import WebhooksPage from './index';

const mocks = vi.hoisted(() => ({
  access: vi.fn(),
  subscriptions: vi.fn(),
  records: vi.fn(),
}));

vi.mock('@umijs/max', () => ({ useAccess: mocks.access }));
vi.mock('@/abp/webhooks', async (importOriginal) => ({
  ...(await importOriginal<typeof import('@/abp/webhooks')>()),
  getWebhookSubscriptions: mocks.subscriptions,
  getWebhookSendRecords: mocks.records,
  createWebhookSubscription: vi.fn(),
  deleteWebhookSendRecord: vi.fn(),
  deleteWebhookSubscription: vi.fn(),
  resendWebhookSendRecord: vi.fn(),
  updateWebhookSubscription: vi.fn(),
}));
beforeEach(() => {
  vi.resetAllMocks();
  mocks.subscriptions.mockResolvedValue({ items: [], totalCount: 0 });
  mocks.records.mockResolvedValue({ items: [], totalCount: 0 });
});

it('独立发送记录权限默认打开记录，并能查询最终失败', async () => {
  mocks.access.mockReturnValue({ canManageWebhookSendRecords: true });
  render(
    <App>
      <WebhooksPage />
    </App>,
  );
  await waitFor(() => expect(mocks.records).toHaveBeenCalled());
  expect(screen.queryByRole('tab', { name: '订阅' })).toBeNull();
  expect(mocks.subscriptions).not.toHaveBeenCalled();
  fireEvent.change(screen.getByRole('combobox', { name: '筛选结果' }), {
    target: { value: 'choice:false' },
  });
  await waitFor(() =>
    expect(mocks.records).toHaveBeenLastCalledWith(
      expect.objectContaining({
        filters: [
          {
            field: 'succeeded',
            join: 'and',
            conditions: [{ op: 'eq', value: 'false' }],
          },
        ],
      }),
    ),
  );
});

it('仅订阅权限不展示或读取发送记录', async () => {
  mocks.access.mockReturnValue({ canManageWebhooks: true });
  render(
    <App>
      <WebhooksPage />
    </App>,
  );
  await waitFor(() => expect(mocks.subscriptions).toHaveBeenCalled());
  expect(screen.queryByRole('tab', { name: '发送记录' })).toBeNull();
  expect(mocks.records).not.toHaveBeenCalled();
});
