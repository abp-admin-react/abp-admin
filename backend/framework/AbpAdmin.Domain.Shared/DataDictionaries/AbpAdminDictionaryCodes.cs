namespace AbpAdmin.DataDictionaries;

/// <summary>
/// 手写字典的编码常量（T3.4 第 5 步）。
/// 对应 C# 枚举的字典不在此列——它们的编码由枚举类型名去掉 Enum 后缀生成，
/// 由 EnumDataDictionarySyncDataSeedContributor 自动物化，不要手写种子。
/// 这里每条都属于「没有对应 C# 枚举、纯配置/翻译表」性质，全部以 IsStatic = true 写入。
/// </summary>
public static class AbpAdminDictionaryCodes
{
    /// <summary>性别（Unknown/Male/Female）。纯配置。</summary>
    public const string Gender = "Gender";

    /// <summary>启用状态（Enabled/Disabled）。纯配置。</summary>
    public const string EnabledStatus = "EnabledStatus";

    /// <summary>通知渠道（Email/Sms/InSite，对齐 T3.5 的渠道标识）。翻译表，值集合由后端渠道实现决定。</summary>
    public const string NotificationMethod = "NotificationMethod";

    /// <summary>审计日志 HTTP 方法筛选（GET/POST/PUT/DELETE/PATCH）。翻译表。</summary>
    public const string AuditLogHttpMethod = "AuditLogHttpMethod";

    /// <summary>定时作业执行结果（"true"/"false"，对齐 T3.3 ScheduledJobExecution.Success 的 JSON 表示）。翻译表。</summary>
    public const string ScheduledJobResult = "ScheduledJobResult";

    /// <summary>GDPR 请求状态（Pending/Ready，T2.4 由 ReadyTime 推导，无独立状态字段）。翻译表。</summary>
    public const string GdprRequestState = "GdprRequestState";

    /// <summary>全部手写字典编码。枚举同步种子按「不在此集合且不在当前枚举集合」识别并删除已消失的枚举字典。</summary>
    public static readonly string[] All =
    [
        Gender, EnabledStatus, NotificationMethod, AuditLogHttpMethod, ScheduledJobResult, GdprRequestState
    ];

    /// <summary>EnabledStatus 字典的项编码。后端代码（如导出渲染）引用时用这个常量，不要散落字面量。</summary>
    public static class EnabledStatusItems
    {
        public const string Enabled = "Enabled";
        public const string Disabled = "Disabled";
    }
}
