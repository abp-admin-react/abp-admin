using System;
using System.Linq;
using System.Threading.Tasks;
using Volo.Abp.AuditLogging;
using Volo.Abp.DependencyInjection;
using Volo.Abp.EventBus.Distributed;
using Volo.Abp.Identity;
using Volo.Abp.Json;
using Volo.Abp.Uow;

namespace AbpAdmin.Gdpr.Providers;

/// <summary>
/// AuditLogging 模块 GDPR 数据贡献者。
/// 贡献该用户的审计日志与安全日志摘要（各取最近 100 条，避免 ZIP 过大）。
/// </summary>
public class AuditLoggingGdprDataProvider :
    IDistributedEventHandler<GdprUserDataRequestedEto>,
    ITransientDependency
{
    public const string ProviderName = "AuditLogging";

    private const int MaxRecordCount = 100;

    private readonly IAuditLogRepository _auditLogRepository;
    private readonly IIdentitySecurityLogRepository _securityLogRepository;
    private readonly IIdentityUserRepository _userRepository;
    private readonly IDistributedEventBus _distributedEventBus;
    private readonly IJsonSerializer _jsonSerializer;

    public AuditLoggingGdprDataProvider(
        IAuditLogRepository auditLogRepository,
        IIdentitySecurityLogRepository securityLogRepository,
        IIdentityUserRepository userRepository,
        IDistributedEventBus distributedEventBus,
        IJsonSerializer jsonSerializer)
    {
        _auditLogRepository = auditLogRepository;
        _securityLogRepository = securityLogRepository;
        _userRepository = userRepository;
        _distributedEventBus = distributedEventBus;
        _jsonSerializer = jsonSerializer;
    }

    [UnitOfWork]
    public virtual async Task HandleEventAsync(GdprUserDataRequestedEto eventData)
    {
        var user = await _userRepository.FindAsync(eventData.UserId);
        if (user == null)
        {
            return;
        }

        // 两个仓储的查询接口都只支持按 userName 过滤，不支持 userId
        var auditLogs = await _auditLogRepository.GetListAsync(
            sorting: nameof(AuditLog.ExecutionTime) + " DESC",
            maxResultCount: MaxRecordCount,
            skipCount: 0,
            userName: user.UserName);

        var securityLogs = await _securityLogRepository.GetListAsync(
            sorting: nameof(IdentitySecurityLog.CreationTime) + " DESC",
            maxResultCount: MaxRecordCount,
            skipCount: 0,
            userName: user.UserName);

        var payload = new
        {
            AuditLogs = auditLogs.Select(x => new
            {
                x.ExecutionTime,
                x.ApplicationName,
                x.HttpMethod,
                x.Url,
                x.HttpStatusCode,
                x.ClientIpAddress,
                x.CorrelationId
            }),
            SecurityLogs = securityLogs.Select(x => new
            {
                x.CreationTime,
                x.Action,
                x.Identity,
                x.ClientIpAddress,
                x.BrowserInfo
            })
        };

        await _distributedEventBus.PublishAsync(new GdprUserDataPreparedEto
        {
            TenantId = eventData.TenantId,
            RequestId = eventData.RequestId,
            UserId = eventData.UserId,
            Provider = ProviderName,
            Data = _jsonSerializer.Serialize(payload)
        });
    }
}
