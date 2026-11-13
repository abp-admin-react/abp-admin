import { describe, expect, it } from 'vitest';
import {
  formatBrowserWallClock,
  formatServerWallClock,
  parseUtcOffsetMinutes,
} from './time';

describe('服务器墙上时间', () => {
  const instant = Date.UTC(2026, 10, 13, 2, 34, 5);

  it('按偏移换算，不看运行测试的机器时区', () => {
    expect(parseUtcOffsetMinutes('+08:00')).toBe(480);
    expect(parseUtcOffsetMinutes('-05:30')).toBe(-(5 * 60 + 30));
    expect(parseUtcOffsetMinutes('+00:00')).toBe(0);
    expect(parseUtcOffsetMinutes('Asia/Shanghai')).toBeNull();
    expect(formatServerWallClock(instant, '+08:00')).toBe(
      '2026-11-13 10:34:05',
    );
    expect(formatServerWallClock(instant, '-05:00')).toBe(
      '2026-11-12 21:34:05',
    );
    expect(formatServerWallClock(instant + 1000, '+08:00')).toBe(
      '2026-11-13 10:34:06',
    );
  });

  it('非法偏移或非法瞬间返回 null', () => {
    expect(formatServerWallClock(instant, 'UTC+8')).toBeNull();
    expect(formatServerWallClock(Number.NaN, '+08:00')).toBeNull();
  });

  it('本机时间按传入的浏览器时区换算，并写明偏移', () => {
    expect(formatBrowserWallClock(instant, 'Asia/Shanghai')).toBe(
      '2026-11-13 10:34:05（Asia/Shanghai，UTC+08:00）',
    );
    expect(formatBrowserWallClock(instant, 'UTC')).toBe(
      '2026-11-13 02:34:05（UTC，UTC+00:00）',
    );
    expect(formatBrowserWallClock(Number.NaN, 'UTC')).toBeNull();
  });
});
