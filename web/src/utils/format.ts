const numberFormatter = new Intl.NumberFormat('en-US');

/**
 * Format a number with thousand separators.
 * Replaces numeral(val).format('0,0')
 */
export const formatNumber = (val: number | string): string => {
  const parsed = Number(val);
  return Number.isFinite(parsed) ? numberFormatter.format(parsed) : '';
};

/**
 * Format a number as yuan currency string.
 * Replaces `¥ ${numeral(val).format('0,0')}`
 */
export const formatYuan = (val: number | string) => `¥ ${formatNumber(val)}`;

/**
 * 格式化重试等待时间（秒）为中文描述。
 * 例如：90 秒 → "1 分 30 秒"，3600 秒 → "1 小时"，5400 秒 → "1 小时 30 分钟"
 */
export function formatRetryAfter(seconds: number): string {
  if (seconds < 60) {
    return `${seconds} 秒`;
  }

  const minutes = Math.floor(seconds / 60);
  const remainingSeconds = seconds % 60;

  if (minutes < 60) {
    return remainingSeconds > 0
      ? `${minutes} 分 ${remainingSeconds} 秒`
      : `${minutes} 分钟`;
  }

  const hours = Math.floor(minutes / 60);
  const remainingMinutes = minutes % 60;

  if (hours < 24) {
    return remainingMinutes > 0
      ? `${hours} 小时 ${remainingMinutes} 分钟`
      : `${hours} 小时`;
  }

  const days = Math.floor(hours / 24);
  const remainingHours = hours % 24;

  return remainingHours > 0
    ? `${days} 天 ${remainingHours} 小时`
    : `${days} 天`;
}

// dateRange/日期输入只给到 YYYY-MM-DD：后端按 DateTime 精确比较，
// 结束日不补到 23:59:59 会把当天整段记录排除出结果（本轮审查统一的共享约定）。
export const toDayStart = (value?: string | null): string | undefined =>
  value && value.length === 10 ? `${value} 00:00:00` : (value ?? undefined);

export const toDayEnd = (value?: string | null): string | undefined =>
  value && value.length === 10 ? `${value} 23:59:59` : (value ?? undefined);
