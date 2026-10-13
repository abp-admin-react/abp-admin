using System;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AbpAdmin.Desensitization;

/// <summary>
/// string 属性的脱敏 converter（对标 ruoyi 的 StringDesensitizeSerializer）。
/// 只作用于响应序列化：入参反序列化原样透传——客户端提交的是它自己持有的明文。
/// 每个「命中规则的属性」各挂一个实例（规则随实例携带），由 STJ 的类型元数据缓存。
/// </summary>
public sealed class MaskedStringConverter : JsonConverter<string?>
{
    private readonly MaskedAttribute _spec;

    public MaskedStringConverter(MaskedAttribute spec)
    {
        _spec = spec;
    }

    public override string? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        return reader.TokenType == JsonTokenType.Null ? null : reader.GetString();
    }

    public override void Write(Utf8JsonWriter writer, string? value, JsonSerializerOptions options)
    {
        if (value == null)
        {
            writer.WriteNullValue();
            return;
        }

        if (!MaskingPermissionGate.Current?.IsPlaintextAllowed(_spec.PlaintextPermission) ?? true)
        {
            value = StringMasker.Mask(value, _spec);
        }

        writer.WriteStringValue(value);
    }
}
