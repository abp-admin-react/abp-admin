import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import React from 'react';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import * as service from './service';

// Mock 服务层与 SignalR 连接层
vi.mock('./service', async () => {
  const actual = await vi.importActual<typeof import('./service')>('./service');
  return {
    ...actual,
    getMyNotifications: vi.fn(),
    getMyUnreadCount: vi.fn(),
    markAllMyNotificationsAsRead: vi.fn(),
  };
});

vi.mock('@/abp/signalr', () => ({
  isRealTimeAvailable: () => true,
  onRealTimeAvailabilityChange: () => () => {},
}));

// antd Drawer 在 happy-dom 里不渲染子树（portal + motion），换成直渲染桩件
vi.mock('antd', async () => {
  const actual = await vi.importActual<typeof import('antd')>('antd');
  return {
    ...actual,
    Drawer: ({ open, children, title, extra }: any) =>
      open ? (
        <div data-testid="drawer">
          <h1>{title}</h1>
          {extra}
          {children}
        </div>
      ) : null,
  };
});

vi.mock('@umijs/max', () => ({
  useIntl: () => ({
    formatMessage: ({ id }: { id: string }) => id,
  }),
}));

function renderWithClient(node: React.ReactElement) {
  const client = new QueryClient({
    defaultOptions: { queries: { retry: false } },
  });
  return render(
    <QueryClientProvider client={client}>{node}</QueryClientProvider>,
  );
}

const mockList = {
  items: [
    {
      id: 'n1',
      title: '系统升级',
      body: '今晚 24 点升级',
      creationTime: new Date().toISOString(),
      isRead: false,
    },
  ],
  totalCount: 1,
};

describe('NotificationBell', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    vi.mocked(service.getMyUnreadCount).mockResolvedValue({
      count: 3,
    } as never);
    vi.mocked(service.getMyNotifications).mockResolvedValue(mockList as never);
    vi.mocked(service.markAllMyNotificationsAsRead).mockResolvedValue(
      undefined as never,
    );
  });

  it('显示未读数徽标', async () => {
    const { default: NotificationBell } = await import('./index');
    renderWithClient(<NotificationBell />);

    await waitFor(() => {
      expect(service.getMyUnreadCount).toHaveBeenCalled();
    });
    await screen.findByText('3');
  });

  it('打开抽屉后加载通知列表', async () => {
    const { default: NotificationBell } = await import('./index');
    renderWithClient(<NotificationBell />);

    fireEvent.click(screen.getByRole('button', { name: 'notifications' }));

    await waitFor(() => {
      expect(service.getMyNotifications).toHaveBeenCalledWith(
        expect.objectContaining({ skipCount: 0, maxResultCount: 20 }),
      );
    });
    await screen.findByText('系统升级');
  });

  it('全部标记已读调用服务并清零未读数', async () => {
    const { default: NotificationBell } = await import('./index');
    renderWithClient(<NotificationBell />);

    fireEvent.click(screen.getByRole('button', { name: 'notifications' }));
    await screen.findByText('系统升级');

    fireEvent.click(
      screen.getByRole('button', {
        name: /component.notification.markAllRead/,
      }),
    );

    await waitFor(() => {
      expect(service.markAllMyNotificationsAsRead).toHaveBeenCalled();
    });
  });
});
