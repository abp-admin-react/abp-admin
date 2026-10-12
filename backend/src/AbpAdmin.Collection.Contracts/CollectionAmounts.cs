using System;
using System.Globalization;

namespace AbpAdmin.Collection.Contracts;

/// <summary>
/// 金额字符串助手（计划第 2 节）：API 和事件里的金额都是十进制字符串，稳定币 18 位小数
/// 不用 JSON 数字；业务代码不直接 decimal.Parse——解析和格式化收口在这里。
/// 进程内事件不经过 JSON，字段同样用十进制字符串，避免两套类型。
/// </summary>
public static class CollectionAmounts
{
    /// <summary>稳定币金额的最大小数位（numeric(28,18)）。</summary>
    public const int Scale = 18;

    /// <summary>
    /// 解析十进制字符串。不变文化、允许前后空白、不允许千分位分隔符；
    /// 小数位超过 <see cref="Scale"/> 时按银行家舍入到 18 位。
    /// </summary>
    public static bool TryParse(string? text, out decimal value)
    {
        value = 0m;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        text = text.Trim();
        if (!decimal.TryParse(text, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint,
                CultureInfo.InvariantCulture, out var parsed))
        {
            return false;
        }

        value = decimal.Round(parsed, Scale, MidpointRounding.ToEven);
        return true;
    }

    /// <summary>
    /// 解析并抛业务异常 <see cref="CollectionErrorCodes.InvalidAmount"/>（带原文）。
    /// </summary>
    public static decimal Parse(string? text)
    {
        if (!TryParse(text, out var value))
        {
            throw new Volo.Abp.BusinessException(CollectionErrorCodes.InvalidAmount)
                .WithData("Amount", text ?? "(null)");
        }

        return value;
    }

    /// <summary>
    /// 格式化为不变文化十进制字符串，去掉多余的前导零与尾随零（"1.100" → "1.1"，"0.000" → "0"）。
    /// </summary>
    public static string Format(decimal value)
    {
        value = decimal.Round(value, Scale, MidpointRounding.ToEven);
        var text = value.ToString("0." + new string('#', Scale), CultureInfo.InvariantCulture);
        return text;
    }
}
