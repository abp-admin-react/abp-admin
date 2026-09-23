using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Dynamic.Core;
using System.Threading.Tasks;
using EasyAbp.NotificationService.NotificationInfos;
using EasyAbp.NotificationService.Notifications;
using EasyAbp.NotificationService.Permissions;
using Microsoft.AspNetCore.Authorization;
using Volo.Abp;
using Volo.Abp.Application.Dtos;
using Volo.Abp.Application.Services;
using Volo.Abp.Domain.Repositories;
using AbpAdmin.OperationLogs;

namespace AbpAdmin.Notifications;

/// <summary>
/// 管理侧通知服务（T3.5 第 11/12 步）。全部端点要 Notification.Manage——
/// 管理员看全量发送记录本来就该是高权限操作（模块自己的只读端点同样要求 Manage）。
/// </summary>
[Authorize(NotificationServicePermissions.Notification.Manage)]
public class NotificationManagementAppService : ApplicationService, INotificationManagementAppService
{
    /// <summary>默认排序：创建时间倒序。</summary>
    protected const string DefaultSorting = nameof(NotificationListItemDto.CreationTime) + " DESC";

    /// <summary>
    /// 排序白名单：Sorting 是 Dynamic LINQ（不拼 SQL、注入风险低），但任意属性名会直通到
    /// OrderBy 且非法值直接报错——白名单外一律回落默认排序。
    /// </summary>
    protected static readonly string[] AllowedSortingColumns =
    {
        nameof(NotificationListItemDto.CreationTime),
        nameof(NotificationListItemDto.UserName),
        nameof(NotificationListItemDto.NotificationMethod),
        nameof(NotificationListItemDto.Success)
    };
    private readonly INotificationRepository _notificationRepository;
    private readonly INotificationInfoRepository _notificationInfoRepository;
    private readonly IRepository<NotificationBroadcast, Guid> _broadcastRepository;
    private readonly INotificationDispatcher _notificationDispatcher;

    public NotificationManagementAppService(
        INotificationRepository notificationRepository,
        INotificationInfoRepository notificationInfoRepository,
        IRepository<NotificationBroadcast, Guid> broadcastRepository,
        INotificationDispatcher notificationDispatcher)
    {
        _notificationRepository = notificationRepository;
        _notificationInfoRepository = notificationInfoRepository;
        _broadcastRepository = broadcastRepository;
        _notificationDispatcher = notificationDispatcher;
    }

    public virtual async Task<PagedResultDto<NotificationListItemDto>> GetListAsync(GetNotificationListInput input)
    {
        var queryable = await _notificationRepository.GetQueryableAsync();

        queryable = queryable
            .WhereIf(!input.NotificationMethod.IsNullOrWhiteSpace(), n => n.NotificationMethod == input.NotificationMethod)
            .WhereIf(input.Success.HasValue, n => n.Success == input.Success)
            .WhereIf(!input.Success.HasValue && input.PendingOnly, n => n.Success == null)
            .WhereIf(!input.UserName.IsNullOrWhiteSpace(), n => n.UserName.Contains(input.UserName!))
            .WhereIf(input.CreationTimeStart.HasValue, n => n.CreationTime >= input.CreationTimeStart)
            .WhereIf(input.CreationTimeEnd.HasValue, n => n.CreationTime <= input.CreationTimeEnd)
            // 默认排除重试记录，避免重试把列表刷满（规格第 12 步）
            .WhereIf(!input.IncludeRetries, n => n.RetryForNotificationId == null);

        var totalCount = await AsyncExecuter.CountAsync(queryable);

        var sorting = input.Sorting.IsNullOrWhiteSpace() ? DefaultSorting : NormalizeSorting(input.Sorting!);
        var items = await AsyncExecuter.ToListAsync(
            queryable.OrderBy(sorting).Skip(input.SkipCount).Take(input.MaxResultCount));

        var pageIds = items.Select(x => x.Id).ToList();

        // 每行重试次数 + 最终状态（最后一次尝试的 Success）。两条查询后在内存合并。
        var retries = pageIds.Count == 0
            ? new List<Notification>()
            : await AsyncExecuter.ToListAsync(
                (await _notificationRepository.GetQueryableAsync())
                    .Where(n => n.RetryForNotificationId != null && pageIds.Contains(n.RetryForNotificationId.Value)));

        var retryGroups = retries.GroupBy(n => n.RetryForNotificationId!.Value)
            .ToDictionary(g => g.Key, g => g.ToList());

        return new PagedResultDto<NotificationListItemDto>(
            totalCount,
            items.Select(n => Map(n, retryGroups)).ToList());
    }

    public virtual async Task<NotificationDetailDto> GetAsync(Guid id)
    {
        var notification = await _notificationRepository.GetAsync(id);
        var info = await _notificationInfoRepository.GetAsync(notification.NotificationInfoId);

        // 尝试链：本记录若是重试，找到链根（模块的重试链只有一层——重试记录的重试仍指向原始记录）
        var rootId = notification.RetryForNotificationId ?? notification.Id;
        var chain = await AsyncExecuter.ToListAsync(
            (await _notificationRepository.GetQueryableAsync())
                .Where(n => n.Id == rootId || n.RetryForNotificationId == rootId)
                .OrderBy(n => n.CreationTime));

        var retryGroups = chain.Where(n => n.RetryForNotificationId != null)
            .GroupBy(n => n.RetryForNotificationId!.Value)
            .ToDictionary(g => g.Key, g => g.ToList());

        var detail = new NotificationDetailDto
        {
            RetryForNotificationId = notification.RetryForNotificationId,
            NotificationInfoProperties = info.ExtraProperties.ToDictionary(x => x.Key, x => x.Value),
            Attempts = chain.Select(n => new NotificationAttemptDto
            {
                Id = n.Id,
                Success = n.Success,
                CompletionTime = n.CompletionTime,
                FailureReason = n.FailureReason,
                CreationTime = n.CreationTime
            }).ToList()
        };
        CopyTo(notification, detail, retryGroups);
        return detail;
    }

    public virtual async Task RetryAsync(Guid id)
    {
        var original = await _notificationRepository.GetAsync(id);

        if (original.Success != false)
        {
            throw new BusinessException(AbpAdminDomainErrorCodes.Notifications.OnlyFailedNotificationCanRetry);
        }

        // Insert 触发的实体创建事件会让对应渠道的 NotificationCreationEventHandlerBase
        // 在 UoW 提交后入队发送作业——重试与首发走同一条发送管线。
        await _notificationRepository.InsertAsync(CreateRetryNotification(original), true);
    }

    /// <summary>
    /// 模块的重试模型：新建一条记录指向原记录，而不是原地改状态（RetryForNotificationId）。
    /// 若本条已是重试记录，新重试仍指向链根（链只有一层，便于"最终状态/重试次数"统计）。
    /// 模块没有提供创建重试记录的公开 API（3.9.0 反编译核实：该属性只有 protected setter，
    /// 无任何方法/构造函数可写），所以用一次反射。反射收拢在下面这个工厂里并钉住契约：
    /// 模块升级重命名属性时这里是显式异常（现有重试集成测试会先红），不是静默的运行时 NRE。
    /// </summary>
    protected virtual Notification CreateRetryNotification(Notification original)
    {
        var retry = new Notification(
            GuidGenerator.Create(),
            CurrentTenant.Id,
            original.UserId,
            original.UserName,
            original.NotificationInfoId,
            original.NotificationMethod);

        var retryProperty = typeof(Notification).GetProperty(nameof(Notification.RetryForNotificationId));
        if (retryProperty == null || !retryProperty.CanWrite)
        {
            throw new InvalidOperationException(
                "EasyAbp NotificationService 升级后 RetryForNotificationId 不可写，" +
                "请检查 NotificationManagementAppService.CreateRetryNotification 的反射契约。");
        }

        retryProperty.SetValue(retry, original.RetryForNotificationId ?? original.Id);
        return retry;
    }

    public virtual Task<Guid> BroadcastAsync(BroadcastNotificationInput input)
    {
        return _notificationDispatcher.BroadcastAsync(input);
    }

    [OperationLog("通知管理", "发送通知", Success = "向用户 {{user(input.userIds)}} 发送了通知「{{input.title}}」")]
    public virtual async Task SendToUsersAsync(SendToUsersNotificationInput input)
    {
        foreach (var method in input.NotificationMethods)
        {
            switch (method)
            {
                case NotificationMethodConsts.Mailing:
                    await _notificationDispatcher.SendEmailAsync(new SendEmailNotificationInput
                    {
                        UserIds = input.UserIds,
                        Subject = input.Title,
                        Body = input.Body
                    });
                    break;
                case NotificationMethodConsts.Sms:
                    await _notificationDispatcher.SendSmsAsync(new SendSmsNotificationInput
                    {
                        UserIds = input.UserIds,
                        Text = input.SmsText ?? input.Body,
                        Properties = input.SmsProperties
                    });
                    break;
                case NotificationMethodConsts.InApp:
                    await _notificationDispatcher.SendInAppAsync(new SendInAppNotificationInput
                    {
                        UserIds = input.UserIds,
                        Title = input.Title,
                        Body = input.Body
                    });
                    break;
                default:
                    throw new BusinessException(AbpAdminDomainErrorCodes.Notifications.InvalidBroadcastMethods)
                        .WithData("Methods", method);
            }
        }
    }

    public virtual async Task<NotificationBroadcastDto> GetBroadcastAsync(Guid id)
    {
        var broadcast = await _broadcastRepository.GetAsync(id);
        return new NotificationBroadcastDto
        {
            Id = broadcast.Id,
            TargetType = broadcast.TargetType,
            TargetId = broadcast.TargetId,
            NotificationMethods = broadcast.NotificationMethods,
            Title = broadcast.Title,
            Body = broadcast.Body,
            TotalCount = broadcast.TotalCount,
            SentCount = broadcast.SentCount,
            FailedCount = broadcast.FailedCount,
            State = broadcast.State,
            CreationTime = broadcast.CreationTime,
            CompletionTime = broadcast.CompletionTime
        };
    }

    /// <summary>
    /// 校验 Dynamic LINQ 排序串（"列名 [ASC|DESC]"）：列名不在白名单或方向非法时回落默认排序。
    /// </summary>
    protected virtual string NormalizeSorting(string sorting)
    {
        var parts = sorting.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var column = AllowedSortingColumns.FirstOrDefault(c =>
            string.Equals(c, parts[0], StringComparison.OrdinalIgnoreCase));
        if (column == null)
        {
            return DefaultSorting;
        }

        return parts.Length == 1
            ? column
            : string.Equals(parts[1], "DESC", StringComparison.OrdinalIgnoreCase) ? column + " DESC" : column + " ASC";
    }

    private static NotificationListItemDto Map(Notification n, Dictionary<Guid, List<Notification>> retryGroups)
    {
        var dto = new NotificationListItemDto();
        CopyTo(n, dto, retryGroups);
        return dto;
    }

    private static void CopyTo(Notification n, NotificationListItemDto dto, Dictionary<Guid, List<Notification>> retryGroups)
    {
        dto.Id = n.Id;
        dto.UserId = n.UserId;
        dto.UserName = n.UserName;
        dto.NotificationInfoId = n.NotificationInfoId;
        dto.NotificationMethod = n.NotificationMethod;
        dto.Success = n.Success;
        dto.CompletionTime = n.CompletionTime;
        dto.FailureReason = n.FailureReason;
        dto.CreationTime = n.CreationTime;

        if (retryGroups.TryGetValue(n.Id, out var retryList) && retryList.Count > 0)
        {
            dto.RetryCount = retryList.Count;
            dto.FinalSuccess = retryList.MaxBy(r => r.CreationTime)!.Success;
        }
        else
        {
            dto.RetryCount = 0;
            dto.FinalSuccess = n.Success;
        }
    }
}
