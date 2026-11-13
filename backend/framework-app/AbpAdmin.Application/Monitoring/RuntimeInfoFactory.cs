using System;
using System.Reflection;

namespace AbpAdmin.Monitoring;

/// <summary>
/// 把程序集版本和操作系统时区收成 <see cref="RuntimeInfoDto"/>。
/// 用机器本地时钟，不是 ABP Clock（全站 Kind=Utc）。顶栏要标的是「这台主机现在几点、哪个时区」。
/// </summary>
public static class RuntimeInfoFactory
{
    public static RuntimeInfoDto Create(Assembly assembly, DateTimeOffset instant, TimeZoneInfo zone)
    {
        var local = TimeZoneInfo.ConvertTime(instant, zone);
        return new RuntimeInfoDto
        {
            ApiVersion = ReadVersion(assembly),
            ServerTime = local,
            ServerUnixTimeMilliseconds = instant.ToUnixTimeMilliseconds(),
            TimeZoneId = zone.Id,
            IanaTimeZoneId = ResolveIanaId(zone),
            TimeZoneDisplayName = zone.DisplayName,
            UtcOffset = FormatOffset(local.Offset),
        };
    }

    public static string ReadVersion(Assembly assembly)
    {
        var informational = assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion;
        if (!string.IsNullOrWhiteSpace(informational))
        {
            return informational;
        }

        var version = assembly.GetName().Version;
        return version == null ? "unknown" : version.ToString(3);
    }

    public static string FormatOffset(TimeSpan offset)
    {
        var sign = offset < TimeSpan.Zero ? "-" : "+";
        var abs = offset.Duration();
        return $"{sign}{abs.Hours:00}:{abs.Minutes:00}";
    }

    public static string? ResolveIanaId(TimeZoneInfo zone)
    {
        if (zone.HasIanaId)
        {
            return zone.Id;
        }

        return TimeZoneInfo.TryConvertWindowsIdToIanaId(zone.Id, out var iana) ? iana : null;
    }
}
