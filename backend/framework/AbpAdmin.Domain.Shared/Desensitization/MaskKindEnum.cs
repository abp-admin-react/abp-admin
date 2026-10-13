using System.ComponentModel;

namespace AbpAdmin.Desensitization;

/// <summary>
/// 脱敏规则类型。内置类型使用业界通用保留位数（对齐 ruoyi-vue-pro desensitize 组件的默认值）；
/// <see cref="Custom"/> 时走滑块参数（PrefixKeep/SuffixKeep/Replacer）或正则（Pattern/RegexReplacer）。
/// </summary>
public enum MaskKindEnum
{
    /// <summary>自定义：设置 Pattern 走正则替换；否则走 PrefixKeep/SuffixKeep 滑块。</summary>
    [Description("自定义")]
    Custom = 0,

    /// <summary>手机号：138****5678（前 3 后 4）。</summary>
    [Description("手机号")]
    Mobile = 1,

    /// <summary>邮箱：e****@gmail.com（本地部分首字符 + 域名保留）。</summary>
    [Description("邮箱")]
    Email = 2,

    /// <summary>身份证号：前 6（行政区）后 2（校验位邻位）。</summary>
    [Description("身份证号")]
    IdCard = 3,

    /// <summary>银行卡号：前 6（发卡行标识）后 2。</summary>
    [Description("银行卡号")]
    BankCard = 4,

    /// <summary>座机：前 4 后 2。</summary>
    [Description("座机")]
    FixedPhone = 5,

    /// <summary>中文姓名：刘**（前 1 后 0）。</summary>
    [Description("中文姓名")]
    ChineseName = 6,

    /// <summary>口令/密钥：全量替换。</summary>
    [Description("口令")]
    Password = 7,
}
