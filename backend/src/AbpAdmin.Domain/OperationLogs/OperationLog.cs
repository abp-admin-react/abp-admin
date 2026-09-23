using System;
using Volo.Abp;
using Volo.Abp.Domain.Entities;
using Volo.Abp.MultiTenancy;

namespace AbpAdmin.OperationLogs;

/// <summary>
/// 语义化操作日志（对标 ruoyi-vue-pro system_operate_log / mzt-biz-log）。
/// 与 ABP AuditLogging 的分工：审计日志记录技术事实（HTTP 调用、实体属性变更），
/// 本表记录业务语义（「谁把哪个租户停用了」），供业务人员直接阅读。
/// 追加-only：不走软删/修改，也没有聚合行为，因此继承 Entity 而非 Audited 聚合根。
/// </summary>
public class OperationLog : Entity<Guid>, IMultiTenant
{
    public virtual Guid? TenantId { get; protected set; }

    /// <summary>操作人快照。用户后续改名/删除不影响历史日志可读性。</summary>
    public virtual Guid? UserId { get; protected set; }

    /// <summary>操作人用户名快照；client_credentials 等无用户场景为 null。</summary>
    public virtual string? UserName { get; protected set; }

    /// <summary>操作模块，如「身份管理」「租户管理」。</summary>
    public virtual string Type { get; protected set; } = default!;

    /// <summary>操作名，如「锁定用户」「应用租户套餐」。</summary>
    public virtual string SubType { get; protected set; } = default!;

    /// <summary>业务对象编号（渲染后的模板值，字符串以兼容各类主键）。</summary>
    public virtual string? BizId { get; protected set; }

    /// <summary>渲染后的操作明细文案，如「将租户 acme 的套餐变更为标准版」。</summary>
    public virtual string? Action { get; protected set; }

    /// <summary>附加数据（JSON 字符串），复杂操作留档用。</summary>
    public virtual string? Extra { get; protected set; }

    public virtual bool Success { get; protected set; }

    public virtual string? ErrorMessage { get; protected set; }

    public virtual string? RequestMethod { get; protected set; }

    public virtual string? RequestUrl { get; protected set; }

    public virtual string? ClientIpAddress { get; protected set; }

    public virtual string? UserAgent { get; protected set; }

    /// <summary>
    /// 请求关联 ID（ABP ICorrelationIdProvider，与 AuditLog/SecurityLog/Serilog 同源）：
    /// 一次请求里的操作日志、审计日志、应用日志可用它串联，支持从 X-Correlation-Id 头透传。
    /// </summary>
    public virtual string? CorrelationId { get; protected set; }

    /// <summary>耗时（毫秒）。</summary>
    public virtual int Duration { get; protected set; }

    public virtual DateTime ExecutionTime { get; protected set; }

    protected OperationLog() { }

    public OperationLog(
        Guid id,
        string type,
        string subType,
        Guid? userId,
        string? userName,
        Guid? tenantId,
        string? bizId,
        string? action,
        string? extra,
        bool success,
        string? errorMessage,
        string? requestMethod,
        string? requestUrl,
        string? clientIpAddress,
        string? userAgent,
        string? correlationId,
        int duration,
        // 时钟由调用方（IClock）提供：默认值会造成本地/UTC 混写
        DateTime executionTime)
        : base(id)
    {
        Type = Check.NotNullOrWhiteSpace(type, nameof(type), maxLength: OperationLogConsts.MaxTypeLength);
        SubType = Check.NotNullOrWhiteSpace(subType, nameof(subType), maxLength: OperationLogConsts.MaxSubTypeLength);
        UserId = userId;
        UserName = Check.Length(userName, nameof(userName), OperationLogConsts.MaxUserNameLength);
        TenantId = tenantId;
        BizId = Check.Length(bizId, nameof(bizId), OperationLogConsts.MaxBizIdLength);
        Action = Check.Length(action, nameof(action), OperationLogConsts.MaxActionLength);
        Extra = Check.Length(extra, nameof(extra), OperationLogConsts.MaxExtraLength);
        Success = success;
        ErrorMessage = Check.Length(errorMessage, nameof(errorMessage), OperationLogConsts.MaxErrorMessageLength);
        RequestMethod = Check.Length(requestMethod, nameof(requestMethod), OperationLogConsts.MaxRequestMethodLength);
        RequestUrl = Check.Length(requestUrl, nameof(requestUrl), OperationLogConsts.MaxRequestUrlLength);
        ClientIpAddress = Check.Length(clientIpAddress, nameof(clientIpAddress), OperationLogConsts.MaxClientIpAddressLength);
        UserAgent = Check.Length(userAgent, nameof(userAgent), OperationLogConsts.MaxUserAgentLength);
        CorrelationId = Check.Length(correlationId, nameof(correlationId), OperationLogConsts.MaxCorrelationIdLength);
        Duration = duration;
        ExecutionTime = executionTime;
    }
}
