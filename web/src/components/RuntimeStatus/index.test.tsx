import { render, screen, waitFor } from '@testing-library/react';
import { App as AntdApp } from 'antd';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';

vi.mock('@umijs/max', () => ({
  request: vi.fn(),
}));

import { request } from '@umijs/max';
import RuntimeStatus from './index';

const runtime = {
  apiVersion: '1.0.0+abc1234',
  serverTime: '2026-11-13T10:34:05+08:00',
  serverUnixTimeMilliseconds: Date.UTC(2026, 10, 13, 2, 34, 5),
  timeZoneId: 'China Standard Time',
  ianaTimeZoneId: 'Asia/Shanghai',
  timeZoneDisplayName: '(UTC+08:00) 北京，重庆，香港特别行政区，乌鲁木齐',
  utcOffset: '+08:00',
};

describe('RuntimeStatus', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    vi.useFakeTimers({
      toFake: ['Date'],
      now: Date.UTC(2026, 10, 13, 2, 34, 5),
    });
  });

  afterEach(() => {
    vi.useRealTimers();
  });

  it('分开显示后端版本和前端版本，并标出服务器时区', async () => {
    vi.mocked(request).mockResolvedValue(runtime);
    render(
      <AntdApp>
        <RuntimeStatus />
      </AntdApp>,
    );

    await waitFor(() => {
      expect(screen.getByTestId('runtime-status').textContent).toContain(
        '后端 1.0.0+abc1234',
      );
    });
    const text = screen.getByTestId('runtime-status').textContent ?? '';
    expect(text).toContain('前端 ');
    expect(text).not.toContain('后端 6.');
    expect(text).toContain('服务器时间');
    expect(text).toContain('2026-11-13 10:34:05');
    expect(text).toMatch(/本机时间\s*\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2}（/);
    expect(text).toContain('Asia/Shanghai，UTC+08:00');
    expect(text).toContain('北京，重庆，香港特别行政区，乌鲁木齐');
  });

  it('接口失败时不改用浏览器时间', async () => {
    vi.mocked(request).mockRejectedValue(new Error('down'));
    render(
      <AntdApp>
        <RuntimeStatus />
      </AntdApp>,
    );

    await waitFor(() => {
      expect(screen.getByTestId('runtime-status').textContent).toContain(
        '后端 不可用',
      );
    });
    const text = screen.getByTestId('runtime-status').textContent ?? '';
    expect(text).toContain('前端 ');
    expect(text).toMatch(/服务器时间\s*不可用/);
    expect(text).not.toMatch(/服务器时间\s*\d{4}-/);
    expect(text).toMatch(/本机时间\s*\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2}（/);
  });
});
