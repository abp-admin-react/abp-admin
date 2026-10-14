import dayjs, { type Dayjs } from 'dayjs';
import timezone from 'dayjs/plugin/timezone';
import utc from 'dayjs/plugin/utc';

dayjs.extend(utc);
dayjs.extend(timezone);

/** Browser local IANA zone (never hardcoded). */
export function userTimeZone(): string {
  return Intl.DateTimeFormat().resolvedOptions().timeZone || dayjs.tz.guess();
}

/** API DateTime is UTC; values without offset are treated as UTC (Z). */
export function parseApiDate(iso?: string | null): Dayjs | null {
  if (!iso) return null;
  const s = String(iso).trim();
  if (!s) return null;
  const hasZone = /([zZ]|[+-]\d{2}:?\d{2})$/.test(s);
  const d = hasZone ? dayjs(s) : dayjs.utc(s);
  return d.isValid() ? d : null;
}

/** Format an API instant in the user's local timezone, with offset label. */
export function formatForDisplay(iso?: string | null, fmt = 'YYYY-MM-DD HH:mm:ss'): string {
  const d = parseApiDate(iso);
  if (!d) return '-';
  return d.tz(userTimeZone()).format(fmt + ' [UTC]Z');
}

/** Local-day range (user TZ) → UTC ISO bounds [start, end) for query params. */
export function rangeToUtcBounds(
  startLocalDay: Dayjs | string,
  endLocalDay: Dayjs | string,
): { startUtc: string; endUtc: string } {
  const tz = userTimeZone();
  const s = dayjs.tz(dayjs(startLocalDay).format('YYYY-MM-DD'), tz).startOf('day');
  const e = dayjs.tz(dayjs(endLocalDay).format('YYYY-MM-DD'), tz).add(1, 'day').startOf('day');
  return { startUtc: s.utc().toISOString(), endUtc: e.utc().toISOString() };
}

/** Today / this week in the user's timezone (as local Dayjs days). */
export function todayLocal(): [Dayjs, Dayjs] {
  const n = dayjs().tz(userTimeZone());
  return [n.startOf('day'), n.startOf('day')];
}
export function thisWeekLocal(): [Dayjs, Dayjs] {
  const n = dayjs().tz(userTimeZone());
  return [n.startOf('week'), n.endOf('week').startOf('day')];
}

/** ProTable column render helper for API instants. */
export const renderApiTime = (_: unknown, r: Record<string, any>, key: string) =>
  formatForDisplay(r?.[key]);

export default dayjs;