import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { App as AntdApp } from 'antd';
import type React from 'react';
import { beforeEach, describe, expect, it, vi } from 'vitest';

/**
 * 缓存监控页的交互契约（server-monitor 测试同一套路）：挂载自动扫第一页的口径
 * （恰好一次、带默认过滤词、memory/连接失败不扫）、infoError 告警渲染、
 * 刷新按钮清表重扫。SCAN 分页协议（合成游标）由后端测试钉住，这里只钉前端触发面。
 */

let mockAccess = { canManageCache: true };

vi.mock('@umijs/max', () => ({
  useAccess: () => mockAccess,
}));

const mockGetCacheMonitorInfo = vi.fn();
const mockGetCacheKeys = vi.fn();

vi.mock('@/abp/proModules', () => ({
  getCacheMonitorInfo: (...args: unknown[]) => mockGetCacheMonitorInfo(...args),
  getCacheKeys: (...args: unknown[]) => mockGetCacheKeys(...args),
  getCacheValue: vi.fn(),
  deleteCacheKey: vi.fn(),
}));

vi.mock('@ant-design/pro-components', () => ({
  PageContainer: ({ children }: any) => <div>{children}</div>,
}));

import CacheMonitorPage from './index';

const redisInfo = {
  backend: 'redis' as const,
  keyPrefix: '',
  totalKeys: 723,
  redisVersion: '8.6.7',
  usedMemoryBytes: 3354368,
  maxMemoryBytes: 0,
};

function renderPage() {
  return render(
    <AntdApp>
      <CacheMonitorPage />
    </AntdApp>,
  );
}

describe('CacheMonitorPage', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    mockAccess = { canManageCache: true };
    mockGetCacheKeys.mockResolvedValue({ keys: [], nextCursor: 0 });
  });

  it('挂载自动扫第一页：恰好一次、带默认过滤词 c:（keyPrefix 为空时）', async () => {
    mockGetCacheMonitorInfo.mockResolvedValue(redisInfo);

    renderPage();

    await waitFor(() => {
      expect(mockGetCacheKeys).toHaveBeenCalledTimes(1);
    });
    // 默认过滤词 c:（对齐输入框种子的 keyPrefix || 'c:'）；口径漂移（漏传 prefix
    // 变 [ct]:** 全量）会让"继续扫描"混入租户键——这里钉住首页口径
    expect(mockGetCacheKeys).toHaveBeenCalledWith({
      prefix: 'c:',
      cursor: 0,
    });
  });

  it('后端为 memory 时不自动扫描', async () => {
    mockGetCacheMonitorInfo.mockResolvedValue({
      ...redisInfo,
      backend: 'memory' as const,
    });

    renderPage();
    await waitFor(() => {
      expect(mockGetCacheMonitorInfo).toHaveBeenCalled();
    });
    expect(mockGetCacheKeys).not.toHaveBeenCalled();
  });

  it('连接失败（connectionError）时不自动扫描——红色告警已说明状况', async () => {
    mockGetCacheMonitorInfo.mockResolvedValue({
      ...redisInfo,
      connectionError: 'It was not possible to connect',
    });

    renderPage();
    await waitFor(() => {
      expect(mockGetCacheMonitorInfo).toHaveBeenCalled();
    });
    expect(mockGetCacheKeys).not.toHaveBeenCalled();
  });

  it('infoError 渲染为"连接正常"的降级告警，不显示误报的连接失败', async () => {
    mockGetCacheMonitorInfo.mockResolvedValue({
      ...redisInfo,
      infoError:
        'INFO：This operation is not available unless admin mode is enabled: INFO',
    });

    renderPage();

    await waitFor(() => {
      expect(
        screen.getByText('服务器信息不可用（连接正常，键浏览与操作不受影响）'),
      ).toBeInTheDocument();
    });
    expect(screen.queryByText('Redis 连接失败')).not.toBeInTheDocument();
  });

  it('刷新按钮：重新拉概览并按当前输入框的词重扫首页（清表后的"空表"不该像丢数据）', async () => {
    mockGetCacheMonitorInfo.mockResolvedValue(redisInfo);
    mockGetCacheKeys.mockResolvedValue({
      keys: [{ key: 'c:demo', type: 'hash', sizeBytes: 1, ttlSeconds: null }],
      nextCursor: 0,
    });

    renderPage();
    await waitFor(() => {
      expect(mockGetCacheKeys).toHaveBeenCalledTimes(1);
    });

    fireEvent.click(screen.getByRole('button', { name: '刷 新' }));

    await waitFor(() => {
      expect(mockGetCacheMonitorInfo).toHaveBeenCalledTimes(2);
      expect(mockGetCacheKeys).toHaveBeenCalledTimes(2);
    });
    // 刷新重扫沿用当前输入框的词（默认种子 c:），保持与首页同一口径
    expect(mockGetCacheKeys).toHaveBeenLastCalledWith({
      prefix: 'c:',
      cursor: 0,
    });
  });
});
