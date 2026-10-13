namespace AbpAdmin.OperationLogs;

/// <summary>操作日志实体/DTO 字段长度约束。</summary>
public static class OperationLogConsts
{
    public const int MaxTypeLength = 64;
    public const int MaxSubTypeLength = 128;
    public const int MaxBizIdLength = 64;
    public const int MaxActionLength = 2048;
    public const int MaxExtraLength = 2048;
    public const int MaxErrorMessageLength = 1024;
    public const int MaxRequestMethodLength = 8;
    public const int MaxRequestUrlLength = 512;
    public const int MaxClientIpAddressLength = 64;
    public const int MaxUserAgentLength = 512;
    public const int MaxUserNameLength = 64;

    /// <summary>ABP 生成的 CorrelationId 为 32 位 Guid("N")；透传外部 X-Correlation-Id 头时可能更长，统一截断。</summary>
    public const int MaxCorrelationIdLength = 64;
}
