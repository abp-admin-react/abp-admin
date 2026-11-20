import { postGridQuery } from '@/abp/gridQuery';

vi.mock('@/abp/gridQuery', () => ({ postGridQuery: vi.fn() }));
vi.mock('ag-grid-react', () => import('@/../tests/gridTestDouble'));

import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { App } from 'antd';
import React from 'react';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { retryBackgroundJob } from '@/abp/proModules';
import BackgroundJobsPage from './index';

let rights: Record<string, boolean> = {};
vi.mock('@umijs/max', () => ({ useAccess: () => rights }));
vi.mock('@/abp/proModules', async (importOriginal) => ({
  ...(await importOriginal<typeof import('@/abp/proModules')>()),
  getBackgroundJobs: vi.fn(),
  retryBackgroundJob: vi.fn(),
  abandonBackgroundJob: vi.fn(),
  deleteBackgroundJob: vi.fn(),
}));
vi.mock('@ant-design/pro-components', () => ({
  PageContainer: ({ children }: any) => <div>{children}</div>,
}));
describe('后台任务操作权限', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    rights = {};
    vi.mocked(postGridQuery).mockResolvedValue({
      items: [
        { id: 'queued', jobName: 'queued-job', isAbandoned: false },
        { id: 'abandoned', jobName: 'abandoned-job', isAbandoned: true },
      ],
      totalCount: 2,
    } as any);
  });

  it('只读角色可查询队列与放弃的任务，不出现写操作', async () => {
    render(
      <App>
        <BackgroundJobsPage />
      </App>,
    );
    expect(await screen.findByText('queued-job')).toBeInTheDocument();
    expect(screen.getByText('abandoned-job')).toBeInTheDocument();
    for (const label of ['重试', '放弃', '删除'])
      expect(screen.queryByText(label)).not.toBeInTheDocument();
  });

  it('重试权限只为已放弃任务提供重新入队，不连带放弃或删除', async () => {
    rights = { canRetryBackgroundJobs: true };
    render(
      <App>
        <BackgroundJobsPage />
      </App>,
    );
    fireEvent.click(await screen.findByText('重试'));
    await waitFor(() =>
      expect(retryBackgroundJob).toHaveBeenCalledWith('abandoned'),
    );
    expect(screen.queryByText('放弃')).not.toBeInTheDocument();
    expect(screen.queryByText('删除')).not.toBeInTheDocument();
  });
});
