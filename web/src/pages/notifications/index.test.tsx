import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { render, screen, waitFor } from '@testing-library/react';
import { App as AntdApp } from 'antd';
import type React from 'react';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import NotificationDetailDrawer from './components/NotificationDetail';
import * as service from './service';

// Mock 服务层：页面不直接依赖生成客户端，mock 这一层即可
vi.mock('./service', async () => {
  const actual = await vi.importActual<typeof import('./service')>('./service');
  return {
    ...actual,
    getNotifications: vi.fn(),
    getNotificationDetail: vi.fn(),
    retryNotification: vi.fn(),
    broadcastNotification: vi.fn(),
    sendNotificationToUsers: vi.fn(),
    getBroadcast: vi.fn(),
  };
});

vi.mock('@/hooks/useDictionary', () => ({
  useDictionary: () => ({
    items: [],
    loading: false,
    valueEnum: {},
    options: [
      { label: '邮件', value: 'Mailing' },
      { label: '短信', value: 'Sms' },
      { label: '站内信', value: 'InApp' },
    ],
  }),
}));

// Mock ProComponents：保留语义化 testid，便于断言
vi.mock('@ant-design/pro-components', () => ({
  PageContainer: ({ children }: any) => (
    <div data-testid="page-container">{children}</div>
  ),
  ProTable: ({ columns, toolBarRender, request }: any) => {
    request?.({ current: 1, pageSize: 10 }, {}, {});
    return (
      <div data-testid="pro-table">
        <div data-testid="table-columns">
          {columns?.map((col: any, i: number) => (
            <div key={col.dataIndex ?? col.key ?? i} data-testid="column">
              {col.title}
            </div>
          ))}
        </div>
        {toolBarRender && <div data-testid="toolbar">{toolBarRender()}</div>}
      </div>
    );
  },
  ModalForm: ({ children, title }: any) => (
    <div data-testid="modal-form" data-title={title}>
      {children}
    </div>
  ),
  ProFormText: ({ label }: any) => <input aria-label={label} />,
  ProFormTextArea: ({ label }: any) => <textarea aria-label={label} />,
  ProFormSelect: ({ label }: any) => <select aria-label={label} />,
  ProFormRadio: {
    Group: ({ label }: any) => <fieldset aria-label={label} />,
  },
  ProFormCheckbox: {
    Group: ({ label }: any) => <fieldset aria-label={label} />,
  },
  ProFormDependency: ({ children }: any) => children({}),
}));

function renderWithClient(node: React.ReactElement) {
  const client = new QueryClient({
    defaultOptions: { queries: { retry: false } },
  });
  return render(
    <QueryClientProvider client={client}>
      <AntdApp>{node}</AntdApp>
    </QueryClientProvider>,
  );
}

const mockList = {
  items: [
    {
      id: 'n1',
      userId: 'u1',
      userName: 'admin',
      notificationInfoId: 'i1',
      notificationMethod: 'InApp',
      success: true,
      creationTime: '2026-08-20T10:00:00Z',
      retryCount: 0,
      finalSuccess: true,
    },
  ],
  totalCount: 1,
};

describe('NotificationsPage', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    vi.mocked(service.getNotifications).mockResolvedValue(mockList as never);
  });

  it('挂载即请求发送记录列表', async () => {
    const { default: Page } = await import('./index');
    renderWithClient(<Page />);

    await waitFor(() => {
      expect(service.getNotifications).toHaveBeenCalledWith(
        expect.objectContaining({ current: 1, pageSize: 10 }),
      );
    });
  });

  it('渲染规格要求的列（且没有"按内容搜索"输入框）', async () => {
    const { default: Page } = await import('./index');
    renderWithClient(<Page />);

    const columns = await screen.findAllByTestId('column');
    const titles = columns.map((c) => c.textContent);
    expect(titles).toContain('创建时间');
    expect(titles).toContain('渠道');
    expect(titles).toContain('接收人');
    expect(titles).toContain('状态');
    expect(titles).toContain('重试次数');
    expect(titles).toContain('操作');
    // 正文在 ExtraProperties 里，技术上不支持按内容搜索——不应出现该筛选
    expect(titles).not.toContain('内容');
    expect(titles).not.toContain('正文');
  });

  it('工具栏有发送入口', async () => {
    const { default: Page } = await import('./index');
    renderWithClient(<Page />);

    const toolbar = await screen.findByTestId('toolbar');
    expect(toolbar.textContent).toContain('发送通知 / 公告');
  });

  // T-模块7：antd v6 Timeline 从 items.children 改为 items.content，写错会静默渲染空尝试链
  it('详情抽屉渲染尝试链（Timeline content 契约回归）', async () => {
    vi.mocked(service.getNotificationDetail).mockResolvedValue({
      ...mockList.items[0],
      retryForNotificationId: null,
      notificationInfoProperties: { title: 'hi' },
      attempts: [
        {
          id: 'a1',
          success: true,
          creationTime: '2026-08-20T10:00:01Z',
          failureReason: null,
        },
        {
          id: 'a2',
          success: false,
          creationTime: '2026-08-20T10:01:01Z',
          failureReason: 'SMTP 连接超时',
        },
      ],
    } as never);

    renderWithClient(
      <NotificationDetailDrawer id="n1" open onClose={() => {}} />,
    );

    expect(await screen.findByText(/第 1 次/)).toBeInTheDocument();
    expect(screen.getByText(/第 2 次/)).toBeInTheDocument();
    expect(screen.getByText('SMTP 连接超时')).toBeInTheDocument();
  });
});
