using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Volo.Abp.Sms;

namespace AbpAdmin.Sms;

/// <summary>
/// SmsMessage.Properties 的容错读取（T3.5）。
/// 属性值经分布式事件 JSON 序列化一圈后会变成 JsonElement 而不是 string，
/// 直接 `as string` 会静默拿到 null（EasyAbp 的 TencentCloudSmsSender 就有这个坑），这里统一处理。
/// </summary>
internal static class SmsMessagePropertyReader
{
    public static string? GetString(SmsMessage message, string key)
    {
        if (!message.Properties.TryGetValue(key, out var value) || value is null)
        {
            return null;
        }

        return value switch
        {
            string s => s,
            JsonElement je when je.ValueKind == JsonValueKind.String => je.GetString(),
            JsonElement je when je.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined => null,
            JsonElement je => je.GetRawText(),
            _ => value.ToString()
        };
    }

    public static string[]? GetStringArray(SmsMessage message, string key)
    {
        if (!message.Properties.TryGetValue(key, out var value) || value is null)
        {
            return null;
        }

        switch (value)
        {
            case string s:
                // JSON 字符串形式（"["a","b"]"）
                return JsonSerializer.Deserialize<string[]>(s);
            case IEnumerable<string> list:
                return list.ToArray();
            case JsonElement je when je.ValueKind == JsonValueKind.Array:
                return je.EnumerateArray().Select(x => x.GetString() ?? x.GetRawText()).ToArray();
            default:
                return JsonSerializer.Deserialize<string[]>(value.ToString()!);
        }
    }
}
