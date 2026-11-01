using System.Linq;
using System.Text.RegularExpressions;

namespace AbpAdmin.Desensitization;

/// <summary>
/// 字符串脱敏算法（对标 ruoyi-vue-pro 的 slider/regex 两类 DesensitizationHandler）。
/// 纯函数无状态，可被序列化管线、Excel 导出、日志输出等任意场景复用。
/// </summary>
public static class StringMasker
{
    /// <summary>
    /// 按规则脱敏。null/空串原样返回（序列化端再决定写 null 还是空串）。
    /// </summary>
    public static string? Mask(string? origin, MaskedAttribute spec)
    {
        if (string.IsNullOrEmpty(origin))
        {
            return origin;
        }

        return spec.Kind switch
        {
            MaskKindEnum.Mobile => Slider(origin, 3, 4, "*"),
            MaskKindEnum.Email => Email(origin),
            MaskKindEnum.IdCard => Slider(origin, 6, 2, "*"),
            MaskKindEnum.BankCard => Slider(origin, 6, 2, "*"),
            MaskKindEnum.FixedPhone => Slider(origin, 4, 2, "*"),
            MaskKindEnum.ChineseName => Slider(origin, 1, 0, "*"),
            MaskKindEnum.Password => Slider(origin, 0, 0, "*"),
            _ => MaskCustom(origin, spec),
        };
    }

    private static string MaskCustom(string origin, MaskedAttribute spec)
    {
        if (!string.IsNullOrEmpty(spec.Pattern))
        {
            return Regex.Replace(origin, spec.Pattern, string.IsNullOrEmpty(spec.RegexReplacer) ? "******" : spec.RegexReplacer);
        }

        return Slider(origin, spec.PrefixKeep, spec.SuffixKeep, spec.Replacer);
    }

    /// <summary>
    /// 滑块脱敏：保留前后缀明文，中间逐字符替换。
    /// 明文长度之和不小于原文时整串替换（对齐 ruoyi 行为，避免短串泄露比例更高的原文）。
    /// </summary>
    public static string Slider(string origin, int prefixKeep, int suffixKeep, string replacer)
    {
        if (string.IsNullOrEmpty(origin))
        {
            return origin;
        }

        replacer = string.IsNullOrEmpty(replacer) ? "*" : replacer;

        var interval = origin.Length - prefixKeep - suffixKeep;
        if (interval <= 0)
        {
            return BuildReplacer(replacer, origin.Length);
        }

        return origin[..prefixKeep]
            + BuildReplacer(replacer, interval)
            + origin[(prefixKeep + interval)..];
    }

    /// <summary>
    /// 邮箱脱敏：本地部分保留首字符，其余折叠为固定 4 个 *；域名保留。
    /// 无 @ 的字符串退化为滑块（前 1 后 0）。
    /// </summary>
    public static string Email(string origin)
    {
        var atIndex = origin.IndexOf('@');
        if (atIndex <= 0)
        {
            return Slider(origin, 1, 0, "*");
        }

        var local = origin[..atIndex];
        return local[0] + "****" + origin[atIndex..];
    }

    private static string BuildReplacer(string replacer, int length)
    {
        if (replacer.Length == 1)
        {
            return new string(replacer[0], length);
        }

        // 多字符替换符按 ruoyi 语义重复 n 次
        return string.Concat(Enumerable.Repeat(replacer, length));
    }
}
