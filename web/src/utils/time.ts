import dayjs, { type Dayjs } from 'dayjs';
import timezone from 'dayjs/plugin/timezone';
import utc from 'dayjs/plugin/utc';

dayjs.extend(utc);
dayjs.extend(timezone);

/**
 * 财务/审计等接口时间约定（与后端 Clock.Kind=Utc 对齐）：
 * - 接口 DateTime **一律按 UTC 瞬间**理解；带 `Z` 或显式偏移的按字面解析；
 *   **无时区后缀的字符串也当作 UTC**（不当本地），避免再差 8 小时。
 * - 展示：转到用户浏览器 IANA 时区，后缀带 `[UTC]±偏移` 便于对账。
 * - 筛选：用户选的「本地日」先转成 UTC 半开区间 `[startUtc, endUtc)` 再传 query。
 * 页面不要自己 dayjs(iso) 当本地；统一走本文件。
 */

/** 浏览器本地 IANA 时区；禁止写死 Asia/Shanghai。 */
export function userTimeZone(): string {
  return Intl.DateTimeFormat().resolvedOptions().timeZone || dayjs.tz.guess();
}

/**
 * 解析接口瞬间。无偏移 → utc；非法 → null。
 * 不变量：返回值内部是 UTC 瞬间，展示前再 `.tz(userTimeZone())`。
 */
export function parseApiDate(iso?: string | null): Dayjs | null {
  if (!iso) return null;
  const s = String(iso).trim();
  if (!s) return null;
  const hasZone = /([zZ]|[+-]\d{2}:?\d{2})$/.test(s);
  const d = hasZone ? dayjs(s) : dayjs.utc(s);
  return d.isValid() ? d : null;
}

/** 接口瞬间 → 用户本地可读串（默认到秒 + UTC 偏移标签）。空/非法显示 `-`。 */
export function formatForDisplay(iso?: string | null, fmt = 'YYYY-MM-DD HH:mm:ss'): string {
  const d = parseApiDate(iso);
  if (!d) return '-';
  return d.tz(userTimeZone()).format(fmt + ' [UTC]Z');
}

/**
 * 本地日范围 → UTC ISO 查询边界（半开区间）。
 * end 为选中结束日的「次日 0 点」UTC，避免漏掉结束日当天数据。
 */
export function rangeToUtcBounds(
  startLocalDay: Dayjs | string,
  endLocalDay: Dayjs | string,
): { startUtc: string; endUtc: string } {
  const tz = userTimeZone();
  const s = dayjs.tz(dayjs(startLocalDay).format('YYYY-MM-DD'), tz).startOf('day');
  const e = dayjs.tz(dayjs(endLocalDay).format('YYYY-MM-DD'), tz).add(1, 'day').startOf('day');
  return { startUtc: s.utc().toISOString(), endUtc: e.utc().toISOString() };
}

/** 用户时区下的「今天」起止（同一天，供日期控件默认值）。 */
export function todayLocal(): [Dayjs, Dayjs] {
  const n = dayjs().tz(userTimeZone());
  return [n.startOf('day'), n.startOf('day')];
}

/** 用户时区下的本周（周起始随 dayjs locale）。 */
export function thisWeekLocal(): [Dayjs, Dayjs] {
  const n = dayjs().tz(userTimeZone());
  return [n.startOf('week'), n.endOf('week').startOf('day')];
}

/** ProTable 列渲染：从行对象取 key 再 formatForDisplay。 */
export const renderApiTime = (_: unknown, r: Record<string, any>, key: string) =>
  formatForDisplay(r?.[key]);

export default dayjs;
