using System;

namespace AbpAdmin.RateLimiting;

/// <summary>
/// 默认格式化器，产出中文描述（如「每 1 小时最多 3 次」）。
/// </summary>
public class DefaultOperationRateLimitingFormatter : IOperationRateLimitingFormatter
{
    public string FormatWindowDescription(TimeSpan duration, int maxCount)
    {
        var durationText = FormatDuration(duration);
        return $"每 {durationText}最多 {maxCount} 次";
    }

    private static string FormatDuration(TimeSpan duration)
    {
        if (duration.TotalDays >= 1)
        {
            var days = (int)duration.TotalDays;
            return days == 1 ? "1 天" : $"{days} 天";
        }
        if (duration.TotalHours >= 1)
        {
            var hours = (int)duration.TotalHours;
            return hours == 1 ? "1 小时" : $"{hours} 小时";
        }
        if (duration.TotalMinutes >= 1)
        {
            var minutes = (int)duration.TotalMinutes;
            return minutes == 1 ? "1 分钟" : $"{minutes} 分钟";
        }
        var seconds = (int)duration.TotalSeconds;
        return seconds == 1 ? "1 秒" : $"{seconds} 秒";
    }
}
