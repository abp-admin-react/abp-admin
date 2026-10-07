namespace AbpAdmin.Webhooks;

/// <summary>
/// 模块常量：表前缀 / 连接串名 / 迁移记账表。改名清单见模块注释（漏一处即静默冲突）。
/// </summary>
public static class AbpAdminWebhooksConsts
{
    public const string DbTablePrefix = "Wh";

    /// <summary>与框架同库（同库多上下文），连接串沿用 Default。</summary>
    public const string ConnectionStringName = "Default";

    /// <summary>本模块 EF Core 迁移记账表，与框架 __EFMigrationsHistory 两本账。</summary>
    public const string SchemaHistoryTable = "__AbpAdminWebhooks_EFMigrationsHistory";

    /// <summary>交付 HTTP 超时（秒）。收端无响应不应拖死后台作业队列。</summary>
    public const int DeliveryTimeoutSeconds = 10;

    /// <summary>单次交付的最大尝试次数（首次 + 重试）。</summary>
    public const int DeliveryMaxAttempts = 3;

    /// <summary>SendRecord.ResponseBody 落库上限（超出截断）。</summary>
    public const int MaxResponseBodyLength = 2000;
}
