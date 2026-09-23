import { render, screen, waitFor } from '@testing-library/react';
import { App as AntdApp } from 'antd';
import type React from 'react';
import { beforeEach, describe, expect, it, vi } from 'vitest';

// 权限可变 mock：默认可查看，单个用例可覆盖
let mockAccess = { canViewServerMonitor: true };

vi.mock('@umijs/max', () => ({
  useAccess: () => mockAccess,
}));

// 页面只从 proModules 取 getServerMonitor；mock 服务层即可（与其他页面测试同一套路）
vi.mock('@/abp/proModules', () => ({
  getServerMonitor: vi.fn(),
}));

vi.mock('@ant-design/pro-components', () => ({
  PageContainer: ({ children }: any) => <div>{children}</div>,
}));

import { getServerMonitor } from '@/abp/proModules';

const baseInfo = {
  machineName: 'test-machine',
  osDescription: 'Linux 6.1',
  osArchitecture: 'X64',
  processArchitecture: 'X64',
  processorCount: 8,
  dotNetVersion: '9.0.0',
  processStartTimeUtc: '2026-09-21T00:00:00Z',
  uptimeSeconds: 3600,
  workingSetBytes: 1024 ** 3,
  privateMemoryBytes: 1024 ** 3,
  gcHeapSizeBytes: 100,
  gcTotalMemoryLimitBytes: 2 * 1024 ** 3,
  isServerGc: true,
  gen0Collections: 1,
  gen1Collections: 1,
  gen2Collections: 1,
  threadCount: 20,
  threadPoolAvailableWorkerThreads: 100,
  threadPoolMinWorkerThreads: 8,
  disks: [],
};

// 页面使用 App.useApp()（antd v6 无 <App> 包裹时 message 为空对象，错误分支会抛
// TypeError），与 notifications / identity 测试一致统一用 <AntdApp> 包裹
function renderPage(node: React.ReactElement) {
  return render(<AntdApp>{node}</AntdApp>);
}

function getCpuCard(): HTMLElement {
  const el = screen.getByText('进程 CPU').closest('.ant-card');
  expect(el).not.toBeNull();
  return el as HTMLElement;
}

describe('ServerMonitorPage', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    mockAccess = { canViewServerMonitor: true };
    vi.mocked(getServerMonitor).mockResolvedValue({
      ...baseInfo,
      processCpuUsagePercent: 12.5,
    } as never);
  });

  it('CPU 采样成功时显示数值与进度条，无警告', async () => {
    vi.mocked(getServerMonitor).mockResolvedValue({
      ...baseInfo,
      processCpuUsagePercent: 42.7,
    } as never);
    const { default: Page } = await import('./index');
    renderPage(<Page />);

    await waitFor(() => {
      expect(getCpuCard().textContent).toContain('42.7');
    });
    expect(getCpuCard().querySelectorAll('.ant-progress')).toHaveLength(1);
    expect(screen.queryByText(/CPU 采样失败/)).toBeNull();
  });

  it('CPU 采样失败（null）时显示警告且不渲染进度条，不误导为 0%', async () => {
    vi.mocked(getServerMonitor).mockResolvedValue({
      ...baseInfo,
      processCpuUsagePercent: null,
    } as never);
    const { default: Page } = await import('./index');
    renderPage(<Page />);

    expect(await screen.findByText(/CPU 采样失败/)).toBeInTheDocument();

    const cpuCard = getCpuCard();
    expect(cpuCard.textContent).toContain('-');
    expect(cpuCard.textContent).not.toContain('0.0');
    // 占位符「-」不带 % 后缀，也不渲染进度条（null ≠ 0% 空闲）
    expect(cpuCard.querySelectorAll('.ant-progress')).toHaveLength(0);
  });

  it('无权限时显示无权限提示，不发起请求', async () => {
    mockAccess = { canViewServerMonitor: false };
    const { default: Page } = await import('./index');
    renderPage(<Page />);

    expect(
      await screen.findByText('无权限查看服务监控。'),
    ).toBeInTheDocument();
    expect(getServerMonitor).not.toHaveBeenCalled();
  });
});
