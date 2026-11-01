using System.Globalization;
using System.Threading.Tasks;
using Volo.Abp.Settings;

namespace AbpAdmin.Collection.Settings;

/// <summary>
/// worker 每轮热读设置的解析助手（与 WithdrawBroadcastManager 私有同款口径抽出共用）：
/// 不变文化解析 + 非法/越界回退默认值（间隔/阈值类 fail-open 到安全默认）；
/// 布尔开关走 <see cref="SettingProviderExtensions.IsTrueAsync"/>（垃圾值 = false，fail-closed）。
/// </summary>
public static class ChainSettingReader
{
    /// <summary>正整数（v &gt; 0）；空/垃圾/非正回退默认。</summary>
    public static async Task<int> GetIntAsync(this ISettingProvider settings, string key, int fallback)
    {
        var raw = await settings.GetOrNullAsync(key);
        return int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) && v > 0
            ? v
            : fallback;
    }

    /// <summary>非负十进制（v ≥ 0）；空/垃圾/负回退默认。</summary>
    public static async Task<decimal> GetDecimalAsync(this ISettingProvider settings, string key, decimal fallback)
    {
        var raw = await settings.GetOrNullAsync(key);
        return decimal.TryParse(raw, NumberStyles.Number, CultureInfo.InvariantCulture, out var v) && v >= 0
            ? v
            : fallback;
    }

    /// <summary>
    /// 可选十进制上限（如 SweepRoundLimit）：空/垃圾/0/负 → null = 不限
    /// （0 上限无业务意义，视同未配置；与旧 GetValue&lt;decimal?&gt; 配置语义一致）。
    /// </summary>
    public static async Task<decimal?> GetOptionalPositiveDecimalAsync(this ISettingProvider settings, string key)
    {
        var raw = await settings.GetOrNullAsync(key);
        return decimal.TryParse(raw, NumberStyles.Number, CultureInfo.InvariantCulture, out var v) && v > 0
            ? v
            : null;
    }
}
