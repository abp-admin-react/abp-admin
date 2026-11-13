import { describe, expect, it } from 'vitest';
import { formatServerZoneLabel, formatWebVersion } from './runtimeInfo';

describe('formatWebVersion', () => {
  it('空版本显示 unknown，提交号取前 7 位', () => {
    expect(formatWebVersion(undefined, undefined)).toBe('unknown');
    expect(formatWebVersion('  ', 'abcdef1234567')).toBe('unknown（abcdef1）');
    expect(formatWebVersion('6.0.3', 'abcdef1234567')).toBe('6.0.3（abcdef1）');
    expect(formatWebVersion('6.0.3', '  ')).toBe('6.0.3');
  });
});

describe('formatServerZoneLabel', () => {
  it('写出 IANA、UTC 偏移，以及去掉重复偏移后的显示名', () => {
    const label = formatServerZoneLabel({
      timeZoneId: 'China Standard Time',
      ianaTimeZoneId: 'Asia/Shanghai',
      timeZoneDisplayName: '(UTC+08:00) 北京，重庆，香港特别行政区，乌鲁木齐',
      utcOffset: '+08:00',
    });
    expect(label?.text).toBe(
      'Asia/Shanghai，UTC+08:00，北京，重庆，香港特别行政区，乌鲁木齐',
    );
    expect(label?.title).toBe(
      '(UTC+08:00) 北京，重庆，香港特别行政区，乌鲁木齐',
    );
  });

  it('UTC 不重复堆显示名', () => {
    expect(
      formatServerZoneLabel({
        timeZoneId: 'UTC',
        ianaTimeZoneId: 'UTC',
        timeZoneDisplayName: 'UTC',
        utcOffset: '+00:00',
      })?.text,
    ).toBe('UTC，UTC+00:00');
  });

  it('负偏移跨时区时保留负号', () => {
    expect(
      formatServerZoneLabel({
        timeZoneId: 'Eastern Standard Time',
        ianaTimeZoneId: 'America/New_York',
        timeZoneDisplayName: '(UTC-05:00) 东部时间（美国和加拿大）',
        utcOffset: '-05:00',
      })?.text,
    ).toBe('America/New_York，UTC-05:00，东部时间（美国和加拿大）');
  });

  it('偏移非法时不编造时区', () => {
    expect(
      formatServerZoneLabel({
        timeZoneId: 'UTC',
        ianaTimeZoneId: null,
        timeZoneDisplayName: 'UTC',
        utcOffset: 'GMT+8',
      }),
    ).toBeNull();
  });
});
