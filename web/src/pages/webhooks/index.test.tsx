import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { App } from 'antd';
import { useEffect } from 'react';
import { beforeEach, expect, it, vi } from 'vitest';
import WebhooksPage from './index';

const mocks = vi.hoisted(() => ({
  access: vi.fn(),
  subscriptions: vi.fn(),
  records: vi.fn(),
}));

vi.mock('@umijs/max', () => ({ useAccess: mocks.access }));
vi.mock('@/abp/webhooks', () => ({
  getWebhookSubscriptions: mocks.subscriptions,
  getWebhookSendRecords: mocks.records,
  createWebhookSubscription: vi.fn(),
  deleteWebhookSendRecord: vi.fn(),
  deleteWebhookSubscription: vi.fn(),
  resendWebhookSendRecord: vi.fn(),
  updateWebhookSubscription: vi.fn(),
}));
vi.mock('@/components/AutoHeightProTable', () => ({
  default: ({ columns, request }: {
    columns: { dataIndex?: string; filters?: { text: string; value: string }[] }[];
    request: (params: object, sorter: object, filters: object) => Promise<unknown>;
  }) => {
    useEffect(() => { void request({}, {}, {}); }, [request]);
    return columns.find(({ dataIndex }) => dataIndex === 'succeeded')?.filters?.map(({ text, value }) => (
      <button key={value} type="button" onClick={() => void request({}, {}, { succeeded: [value] })}>
        {text}
      </button>
    ));
  },
}));

beforeEach(() => {
  vi.resetAllMocks();
  mocks.subscriptions.mockResolvedValue({ items: [], totalCount: 0 });
  mocks.records.mockResolvedValue({ items: [], totalCount: 0 });
});

it('独立发送记录权限默认打开记录，并能查询最终失败', async () => {
  mocks.access.mockReturnValue({ canManageWebhookSendRecords: true });
  render(<App><WebhooksPage /></App>);
  await waitFor(() => expect(mocks.records).toHaveBeenCalled());
  expect(screen.queryByRole('tab', { name: '订阅' })).toBeNull();
  expect(mocks.subscriptions).not.toHaveBeenCalled();
  fireEvent.click(screen.getByRole('button', { name: '失败' }));
  expect(mocks.records).toHaveBeenLastCalledWith(expect.objectContaining({ succeeded: false }));
});

it('仅订阅权限不展示或读取发送记录', async () => {
  mocks.access.mockReturnValue({ canManageWebhooks: true });
  render(<App><WebhooksPage /></App>);
  await waitFor(() => expect(mocks.subscriptions).toHaveBeenCalled());
  expect(screen.queryByRole('tab', { name: '发送记录' })).toBeNull();
  expect(mocks.records).not.toHaveBeenCalled();
});
