/**
 * 登录后顶栏用的运行信息。
 *
 * 后端版本：GET /api/app/runtime-info（宿主程序集 InformationalVersion，改 backend/common.props 的 Version）。
 * 前端版本：当前页面打进包的 web/package.json，不是 C# 版本。
 * 服务器时钟：Unix 毫秒 + 操作系统 UTC 偏移走时，不转成浏览器时区。
 */
import { request } from '@umijs/max';
import { formatCompactUtcOffset, parseUtcOffsetMinutes } from '@/utils/time';

export type RuntimeInfoDto = {
  apiVersion: string;
  serverTime: string;
  serverUnixTimeMilliseconds: number;
  timeZoneId: string;
  ianaTimeZoneId: string | null;
  timeZoneDisplayName: string;
  utcOffset: string;
};

export async function getRuntimeInfo() {
  return request<RuntimeInfoDto>('/api/app/runtime-info', {
    method: 'GET',
    skipErrorHandler: true,
  });
}

export function formatWebVersion(
  version: string | undefined,
  commit: string | undefined,
): string {
  const base = version?.trim() ? version.trim() : 'unknown';
  const short = commit?.trim().slice(0, 7);
  return short ? `${base}（${short}）` : base;
}

/** 当前页面包的版本。define 进包的是 web/package.json，不是后端程序集。 */
export function readWebVersion(): string {
  let version: string | undefined;
  try {
    version = typeof __APP_VERSION__ === 'string' ? __APP_VERSION__ : undefined;
  } catch {
    version = undefined;
  }
  let commit: string | undefined;
  try {
    commit =
      typeof process.env.COMMIT_HASH === 'string'
        ? process.env.COMMIT_HASH
        : undefined;
  } catch {
    commit = undefined;
  }
  return formatWebVersion(version, commit);
}

/** 顶栏显示简短 UTC 偏移，悬停显示服务器时区详情。 */
export function formatServerZoneLabel(
  info: Pick<
    RuntimeInfoDto,
    'timeZoneId' | 'ianaTimeZoneId' | 'timeZoneDisplayName' | 'utcOffset'
  >,
): { text: string; title: string } | null {
  const minutes = parseUtcOffsetMinutes(info.utcOffset);
  if (minutes == null) return null;
  const sign = minutes < 0 ? '-' : '+';
  const abs = Math.abs(minutes);
  const offset = `UTC${sign}${String(Math.floor(abs / 60)).padStart(2, '0')}:${String(abs % 60).padStart(2, '0')}`;
  const id = info.timeZoneId.trim();
  const iana = (info.ianaTimeZoneId ?? '').trim();
  const primary = iana || id;
  if (!primary) return null;
  const title = info.timeZoneDisplayName.trim() || `${primary}，${offset}`;
  return { text: formatCompactUtcOffset(minutes), title };
}
