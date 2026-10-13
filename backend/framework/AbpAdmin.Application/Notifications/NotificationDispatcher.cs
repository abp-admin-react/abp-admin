using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using EasyAbp.NotificationService.Notifications;
using EasyAbp.NotificationService.Provider.Mailing;
using EasyAbp.NotificationService.Provider.Sms;
using Volo.Abp;
using Volo.Abp.BackgroundJobs;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.EventBus.Distributed;
using Volo.Abp.Guids;
using Volo.Abp.Identity;
using Volo.Abp.Json;
using Volo.Abp.Linq;
using Volo.Abp.MultiTenancy;
using Volo.Abp.Uow;

namespace AbpAdmin.Notifications;

/// <summary>
/// INotificationDispatcher 实现（T3.5）：ETO 构造与发布的唯一地点。
/// 不实现 IApplicationService——避免被 Host 的常规控制器配置暴露成 HTTP API；
/// UoW 用显式 Begin（requiresNew: false：有环境 UoW 就并入，没有就自己开）。
/// </summary>
public class NotificationDispatcher : INotificationDispatcher, ITransientDependency
{
    private readonly IDistributedEventBus _distributedEventBus;
    private readonly IJsonSerializer _jsonSerializer;
    private readonly ICurrentTenant _currentTenant;
    private readonly IGuidGenerator _guidGenerator;
    private readonly IRepository<NotificationBroadcast, Guid> _broadcastRepository;
    private readonly IRepository<IdentityUser, Guid> _userRepository;
    private readonly IBackgroundJobManager _backgroundJobManager;
    private readonly IAsyncQueryableExecuter _asyncExecuter;
    private readonly IUnitOfWorkManager _unitOfWorkManager;

    public NotificationDispatcher(
        IDistributedEventBus distributedEventBus,
        IJsonSerializer jsonSerializer,
        ICurrentTenant currentTenant,
        IGuidGenerator guidGenerator,
        IRepository<NotificationBroadcast, Guid> broadcastRepository,
        IRepository<IdentityUser, Guid> userRepository,
        IBackgroundJobManager backgroundJobManager,
        IAsyncQueryableExecuter asyncExecuter,
        IUnitOfWorkManager unitOfWorkManager)
    {
        _distributedEventBus = distributedEventBus;
        _jsonSerializer = jsonSerializer;
        _currentTenant = currentTenant;
        _guidGenerator = guidGenerator;
        _broadcastRepository = broadcastRepository;
        _userRepository = userRepository;
        _backgroundJobManager = backgroundJobManager;
        _asyncExecuter = asyncExecuter;
        _unitOfWorkManager = unitOfWorkManager;
    }

    public virtual async Task SendEmailAsync(SendEmailNotificationInput input)
    {
        Check.NotNull(input, nameof(input));
        CheckUserIds(input.UserIds);

        await _distributedEventBus.PublishAsync(
            new CreateEmailNotificationEto(_currentTenant.Id, input.UserIds, input.Subject, input.Body));
    }

    public virtual async Task SendSmsAsync(SendSmsNotificationInput input)
    {
        Check.NotNull(input, nameof(input));
        CheckUserIds(input.UserIds);

        await _distributedEventBus.PublishAsync(
            new CreateSmsNotificationEto(
                _currentTenant.Id,
                input.UserIds,
                input.Text,
                input.Properties ?? new Dictionary<string, object>(),
                _jsonSerializer));
    }

    public virtual async Task SendInAppAsync(SendInAppNotificationInput input)
    {
        Check.NotNull(input, nameof(input));
        CheckUserIds(input.UserIds);

        await _distributedEventBus.PublishAsync(
            new CreateInAppNotificationEto(_currentTenant.Id, input.UserIds, input.Title, input.Body));
    }

    /// <summary>
    /// 目标用户列表非空校验（走业务错误码映射 400，而不是映射 500 的 ArgumentException）。
    /// </summary>
    private static void CheckUserIds(List<Guid> userIds)
    {
        if (userIds.Count == 0)
        {
            throw new BusinessException(AbpAdminDomainErrorCodes.Notifications.EmptyUserIds)
                .WithData("ParamName", "UserIds");
        }
    }

    public virtual async Task<Guid> BroadcastAsync(BroadcastNotificationInput input)
    {
        Check.NotNull(input, nameof(input));
        ValidateBroadcastInput(input);

        using var uow = _unitOfWorkManager.Begin(requiresNew: false, isTransactional: true);

        var totalCount = await CountTargetUsersAsync(input);

        var broadcast = new NotificationBroadcast(
            _guidGenerator.Create(),
            _currentTenant.Id,
            input.TargetType,
            input.TargetId,
            string.Join(',', input.NotificationMethods),
            input.Title,
            input.Body,
            input.SmsText,
            input.SmsProperties == null ? null : _jsonSerializer.Serialize(input.SmsProperties),
            totalCount);

        await _broadcastRepository.InsertAsync(broadcast, true);

        // 入队第一个批次作业；之后由作业自入队下一批
        await _backgroundJobManager.EnqueueAsync(
            new BroadcastNotificationJobArgs(_currentTenant.Id, broadcast.Id));

        await uow.CompleteAsync();

        return broadcast.Id;
    }

    // ========== 批量发布（仅供 BroadcastNotificationJob 调用，接口形态见 INotificationDispatcher） ==========
    // 全代码库只有本文件构造 ETO（验收 grep 据此成立）：
    // rg "CreateSmsNotificationEto|CreateEmailNotificationEto|CreateNotificationEto" src/ --glob '!**/NotificationDispatcher.cs'

    public virtual async Task SendEmailBatchAsync(Guid? tenantId, List<NotificationUserInfoModel> users, string subject, string body)
    {
        await _distributedEventBus.PublishAsync(new CreateEmailNotificationEto(tenantId, users, subject, body));
    }

    public virtual async Task SendSmsBatchAsync(Guid? tenantId, List<NotificationUserInfoModel> users, string text, IDictionary<string, object> properties)
    {
        await _distributedEventBus.PublishAsync(new CreateSmsNotificationEto(tenantId, users, text, properties, _jsonSerializer));
    }

    public virtual async Task SendInAppBatchAsync(Guid? tenantId, List<NotificationUserInfoModel> users, string title, string body)
    {
        await _distributedEventBus.PublishAsync(new CreateInAppNotificationEto(tenantId, users, title, body));
    }

    protected virtual void ValidateBroadcastInput(BroadcastNotificationInput input)
    {
        var validTargetTypes = new[]
        {
            NotificationBroadcastTargetTypes.All,
            NotificationBroadcastTargetTypes.Role,
            NotificationBroadcastTargetTypes.OrganizationUnit
        };
        if (!validTargetTypes.Contains(input.TargetType))
        {
            throw new BusinessException(AbpAdminDomainErrorCodes.Notifications.InvalidBroadcastTarget)
                .WithData("TargetType", input.TargetType);
        }

        if (input.TargetType != NotificationBroadcastTargetTypes.All && input.TargetId == null)
        {
            throw new BusinessException(AbpAdminDomainErrorCodes.Notifications.InvalidBroadcastTarget)
                .WithData("TargetType", input.TargetType);
        }

        var validMethods = new[] { NotificationMethodConsts.Mailing, NotificationMethodConsts.Sms, NotificationMethodConsts.InApp };
        if (input.NotificationMethods.Count == 0 || input.NotificationMethods.Any(m => !validMethods.Contains(m)))
        {
            throw new BusinessException(AbpAdminDomainErrorCodes.Notifications.InvalidBroadcastMethods)
                .WithData("Methods", string.Join(',', input.NotificationMethods));
        }
    }

    /// <summary>创建时 COUNT 一次目标用户总数（供进度展示）；分页游标走 keyset，见 BroadcastNotificationJob。</summary>
    protected virtual async Task<int> CountTargetUsersAsync(BroadcastNotificationInput input)
    {
        var queryable = await _userRepository.GetQueryableAsync();

        // 目标过滤与 BroadcastNotificationJob.GetTargetUserPageAsync 共用同一构建器，
        // 计数与实际 fan-out 口径必须一致（NotificationTargetUserQueryExtensions）
        queryable = queryable.ApplyBroadcastTarget(input.TargetType, input.TargetId);

        return await _asyncExecuter.CountAsync(queryable);
    }
}
