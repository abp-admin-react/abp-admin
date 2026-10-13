namespace AbpAdmin.AuditLogs;

/// <summary>
/// 审计日志「已处理」标记的字段约束与扩展属性名。
/// 处理状态不建侧表：经 ABP Entity Extensions（AbpAdminEfCoreEntityExtensionMappings）
/// 映射为 AbpAuditLogs 表上的真实列（先例：Tenant 的 ActivationState 三列），
/// 「仅未处理」筛选因此是单表条件（对标 ruoyi ApiErrorLog 行内 process 列形态）。
/// </summary>
public static class AuditLogHandleConsts
{
    /// <summary>处理时间（null = 未处理，未处理筛选的唯一锚点）。</summary>
    public const string HandledAtPropertyName = "HandledAt";

    /// <summary>处理人用户 Id。</summary>
    public const string HandledByUserIdPropertyName = "HandledByUserId";

    /// <summary>处理人用户名快照（展示用，避免按页 join 用户表）。</summary>
    public const string HandledByNamePropertyName = "HandledByName";

    /// <summary>处置备注（结论/根因/工单号等，可选）。</summary>
    public const string HandledNotePropertyName = "HandledNote";

    /// <summary>对齐 ABP IdentityUserConsts.MaxUserNameLength（256）：合法长用户名不该让标记处理 500。</summary>
    public const int MaxHandledByNameLength = 256;

    public const int MaxNoteLength = 512;
}
