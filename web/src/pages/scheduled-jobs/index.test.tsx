import { render, screen, waitFor } from '@testing-library/react';
import { App as AntdApp } from 'antd';
import type React from 'react';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import * as service from './service';

// Mock 服务层：页面不直接依赖生成客户端，mock 这一层即可
vi.mock('./service', async () => {
  const actual = await vi.importActual<typeof import('./service')>('./service');
  return {
    ...actual,
    getScheduledJobs: vi.fn(),
    getJobTypes: vi.fn(),
    createScheduledJob: vi.fn(),
    updateScheduledJob: vi.fn(),
    deleteScheduledJob: vi.fn(),
    setJobEnabled: vi.fn(),
    triggerJob: vi.fn(),
    getExecutions: vi.fn(),
    previewCron: vi.fn(),
  };
});

// Mock ProComponents：保留语义化 testid，便于断言
vi.mock('@ant-design/pro-components', () => ({
  PageContainer: ({ children }: any) => (
    <div data-testid="page-container">{children}</div>
  ),
  ProTable: ({ columns, toolBarRender, request, params }: any) => {
    request?.({ current: 1, pageSize: 10, ...params }, {}, {});
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
  ModalForm: ({ children, title, trigger }: any) => (
    <div data-testid="modal-form" data-title={title}>
      {trigger}
      {children}
    </div>
  ),
  ProFormText: ({ label }: any) => <input aria-label={label} />,
  ProFormTextArea: ({ label }: any) => <textarea aria-label={label} />,
  ProFormSelect: ({ label }: any) => <select aria-label={label} />,
  ProFormSwitch: ({ label }: any) => (
    <input type="checkbox" aria-label={label} />
  ),
}));

const mockJobs = {
  items: [
    {
      id: 'job-1',
      name: 'audit-cleanup',
      jobType: 'AbpAdmin.AuditLogCleanup',
      cronExpression: '0 0 3 * * ?',
      isEnabled: true,
      lastRunTime: '2026-08-20T03:00:00Z',
      lastRunSuccess: true,
      nextRunTime: '2026-08-21T03:00:00Z',
    },
  ],
  totalCount: 1,
};

// 页面使用 App.useApp()（antd v6 无 <App> 包裹时 message 为空对象，错误分支会抛
// TypeError），与 notifications / identity 测试一致统一用 <AntdApp> 包裹
function renderPage(node: React.ReactElement) {
  return render(<AntdApp>{node}</AntdApp>);
}

describe('ScheduledJobsPage', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    vi.mocked(service.getScheduledJobs).mockResolvedValue(mockJobs as never);
    vi.mocked(service.getJobTypes).mockResolvedValue({
      items: [
        {
          jobType: 'AbpAdmin.AuditLogCleanup',
          displayName: '过期审计日志清理',
        },
      ],
    } as never);
  });

  it('挂载即请求作业列表', async () => {
    const { default: Page } = await import('./index');
    renderPage(<Page />);

    await waitFor(() => {
      expect(service.getScheduledJobs).toHaveBeenCalledWith(
        expect.objectContaining({ current: 1, pageSize: 10 }),
      );
    });
  });

  it('渲染规格要求的列', async () => {
    const { default: Page } = await import('./index');
    renderPage(<Page />);

    const columns = await screen.findAllByTestId('column');
    const titles = columns.map((c) => c.textContent);
    expect(titles).toContain('名称');
    expect(titles).toContain('作业类型');
    expect(titles).toContain('cron 表达式');
    expect(titles).toContain('启用');
    expect(titles).toContain('上次执行');
    expect(titles).toContain('上次结果');
    expect(titles).toContain('下次执行');
    expect(titles).toContain('操作');
  });

  it('工具栏有新建作业入口', async () => {
    const { default: Page } = await import('./index');
    renderPage(<Page />);

    const toolbar = await screen.findByTestId('toolbar');
    expect(toolbar.textContent).toContain('新建作业');
  });

  it('表单加载作业类型下拉数据', async () => {
    const { default: Page } = await import('./index');
    renderPage(<Page />);

    await waitFor(() => {
      expect(service.getJobTypes).toHaveBeenCalled();
    });
  });

  it('作业类型加载失败时给出错误提示（页面级单条，非每行表单一条）', async () => {
    vi.mocked(service.getJobTypes).mockRejectedValueOnce(new Error('boom'));
    const { default: Page } = await import('./index');
    renderPage(<Page />);

    expect(
      await screen.findByText('加载作业类型失败，请刷新页面重试'),
    ).toBeInTheDocument();
  });
});
