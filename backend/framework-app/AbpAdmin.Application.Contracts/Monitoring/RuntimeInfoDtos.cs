using System;
using System.Threading.Tasks;
using Volo.Abp.Application.Services;

namespace AbpAdmin.Monitoring;

public interface IRuntimeInfoAppService : IApplicationService
{
    /// <summary>
    /// 当前进程的后端版本，以及操作系统本地时钟和时区。
    /// 登录后顶栏读取；不含配置、密钥或机器名。
    /// </summary>
    Task<RuntimeInfoDto> GetAsync();
}

public class RuntimeInfoDto
{
    /// <summary>后端程序集 InformationalVersion（common.props 的 Version；SDK 带源码修订时含 +提交号）。</summary>
    public string ApiVersion { get; set; } = default!;

    /// <summary>服务器操作系统本地时间，带偏移。展示不要依赖这个字段的 JSON 偏移，以 <see cref="UtcOffset"/> 为准。</summary>
    public DateTimeOffset ServerTime { get; set; }

    /// <summary>同一瞬间的 Unix 毫秒。前端用它走时，避免日期转换器把偏移收成 Z 之后再按浏览器时区重算。</summary>
    public long ServerUnixTimeMilliseconds { get; set; }

    /// <summary>操作系统时区 ID。Windows 多为 China Standard Time，Linux 多为 IANA。</summary>
    public string TimeZoneId { get; set; } = default!;

    /// <summary>能转换成 IANA 时给出，否则为 null。Id 本身已是 IANA 时与 <see cref="TimeZoneId"/> 相同。</summary>
    public string? IanaTimeZoneId { get; set; }

    /// <summary>操作系统本地化的时区显示名，可能已含 UTC 偏移和城市名。</summary>
    public string TimeZoneDisplayName { get; set; } = default!;

    /// <summary>当前 UTC 偏移，形如 +08:00 或 -05:30。这是墙上时间的时区，不是浏览器时区。</summary>
    public string UtcOffset { get; set; } = default!;
}
