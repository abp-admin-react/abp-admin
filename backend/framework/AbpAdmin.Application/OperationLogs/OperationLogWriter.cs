using System;
using System.Threading;
using System.Threading.Tasks;
using AbpAdmin.OperationLogs;
using Microsoft.Extensions.Logging;
using Volo.Abp;
using Volo.Abp.Guids;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.MultiTenancy;
using Volo.Abp.Timing;
using Volo.Abp.Uow;
using Volo.Abp.Users;

namespace AbpAdmin.OperationLogs;

/// <summary>待落库的操作日志（由 OperationLogActionFilter 或手动流程组装）。</summary>
public class OperationLogEntry
{
    public string Type { get; init; } = default!;

    public string SubType { get; init; } = default!;

    public string? BizId { get; init; }

    public string? Action { get; init; }

    public string? Extra { get; init; }

    public bool Success { get; init; }

    public string? ErrorMessage { get; init; }

    public string? RequestMethod { get; init; }

    public string? RequestUrl { get; init; }

    public string? ClientIpAddress { get; init; }

    public string? UserAgent { get; init; }

    /// <summary>请求关联 ID（与 ABP AuditLog/SecurityLog 同源，ICorrelationIdProvider.Get()）。</summary>
    public string? CorrelationId { get; init; }

    public int Duration { get; init; }
}

public interface IOperationLogWriter
{
    /// <summary>
    /// 落库一条操作日志。独立 UoW（requiresNew）写入：
    /// 成功日志在业务事务提交后调用不会互相干扰；失败日志在业务回滚后仍可持久化。
    /// </summary>
    Task WriteAsync(OperationLogEntry entry, CancellationToken cancellationToken = default);
}

public class OperationLogWriter : IOperationLogWriter, ITransientDependency
{
    private readonly IRepository<OperationLog, Guid> _repository;
    private readonly IUnitOfWorkManager _unitOfWorkManager;
    private readonly ICurrentUser _currentUser;
    private readonly ICurrentTenant _currentTenant;
    private readonly IGuidGenerator _guidGenerator;
    private readonly IClock _clock;
    private readonly ILogger<OperationLogWriter> _logger;

    public OperationLogWriter(
        IRepository<OperationLog, Guid> repository,
        IUnitOfWorkManager unitOfWorkManager,
        ICurrentUser currentUser,
        ICurrentTenant currentTenant,
        IGuidGenerator guidGenerator,
        IClock clock,
        ILogger<OperationLogWriter> logger)
    {
        _repository = repository;
        _unitOfWorkManager = unitOfWorkManager;
        _currentUser = currentUser;
        _currentTenant = currentTenant;
        _guidGenerator = guidGenerator;
        _clock = clock;
        _logger = logger;
    }

    public virtual async Task WriteAsync(OperationLogEntry entry, CancellationToken cancellationToken = default)
    {
        try
        {
            using var uow = _unitOfWorkManager.Begin(requiresNew: true, isTransactional: false);
            await _repository.InsertAsync(
                new OperationLog(
                    _guidGenerator.Create(),
                    // Type/SubType 同样要截断：SubType 是模板渲染产物（如 "删除 {{file.fileName}}"），
                    // 长度不可控，超限会让实体构造器抛异常、整条日志在 catch 里静默消失。
                    // 必填字段经 RequiredOrUnknown 兜底：Normalize 对 null/empty 原样放行，
                    // 而实体构造器 NotNullOrWhiteSpace 对 null/空白都抛异常，仅 ?? 挡不住空串/纯空白
                    RequiredOrUnknown(entry.Type, OperationLogConsts.MaxTypeLength),
                    RequiredOrUnknown(entry.SubType, OperationLogConsts.MaxSubTypeLength),
                    _currentUser.Id,
                    _currentUser.UserName,
                    _currentTenant.Id,
                    Normalize(entry.BizId, OperationLogConsts.MaxBizIdLength),
                    Normalize(entry.Action, OperationLogConsts.MaxActionLength),
                    Normalize(entry.Extra, OperationLogConsts.MaxExtraLength),
                    entry.Success,
                    Normalize(entry.ErrorMessage, OperationLogConsts.MaxErrorMessageLength),
                    Normalize(entry.RequestMethod, OperationLogConsts.MaxRequestMethodLength),
                    Normalize(entry.RequestUrl, OperationLogConsts.MaxRequestUrlLength),
                    Normalize(entry.ClientIpAddress, OperationLogConsts.MaxClientIpAddressLength),
                    Normalize(entry.UserAgent, OperationLogConsts.MaxUserAgentLength),
                    Normalize(entry.CorrelationId, OperationLogConsts.MaxCorrelationIdLength),
                    entry.Duration,
                    _clock.Now),
                autoSave: true,
                cancellationToken: cancellationToken);
            await uow.CompleteAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            // 日志基建故障绝不向上传染业务流程。
            // （filter 侧还有一层 net 兜底；此处保留是因为本接口也是公共 API，
            // 后台作业等手动调用方没有外层兜底。）
            _logger.LogWarning(ex, "操作日志落库失败：{Type}-{SubType}", entry.Type, entry.SubType);
        }
    }

    /// <summary>
    /// 落库前规整自由文本字段（防两类蓄意/意外丢日志与伪造）：
    /// 1) 截断——模板渲染值（如 {{filter}}、{{file.fileName}}）与异常消息长度不可控，
    ///    实体构造器的 Check.Length 超限会抛异常，不截断等于放行「超长输入让审计记录消失」；
    /// 2) 清洗控制字符——换行/制表符会在前端 pre-wrap 渲染和未来的文本导出里伪造额外日志行。
    /// </summary>
    private static string? Normalize(string? value, int maxLength)
    {
        if (string.IsNullOrEmpty(value))
        {
            return value;
        }

        // CRLF 先归一为单字符，避免折叠出两个连续空格
        value = value.Replace("\r\n", "\n");
        var normalized = new char[value.Length];
        var length = 0;
        foreach (var ch in value)
        {
            if (length >= maxLength - 1)
            {
                normalized[length++] = '…';
                break;
            }

            // 控制字符（含 \r\n\t）折叠为空格
            normalized[length++] = char.IsControl(ch) ? ' ' : ch;
        }

        return new string(normalized, 0, length);
    }

    /// <summary>
    /// 必填字段的兜底：实体构造器 NotNullOrWhiteSpace 对 null/空串/纯空白都抛异常，
    /// 任一形态漏过都会让整条日志在 catch 里静默消失——统一落为占位值保住记录。
    /// </summary>
    private static string RequiredOrUnknown(string? value, int maxLength)
    {
        var normalized = Normalize(value, maxLength);
        return string.IsNullOrWhiteSpace(normalized) ? "unknown" : normalized;
    }
}
