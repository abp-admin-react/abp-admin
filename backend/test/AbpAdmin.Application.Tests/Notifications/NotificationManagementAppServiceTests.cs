using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using EasyAbp.NotificationService.Notifications;
using Shouldly;
using Volo.Abp;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Modularity;
using Volo.Abp.Timing;
using Xunit;

namespace AbpAdmin.Notifications;

/// <summary>
/// T3.5 管理侧服务测试：默认列表排除重试记录、重试端点创建指向原记录的新通知、
/// 未失败不能重试。
/// </summary>
public abstract class NotificationManagementAppServiceTests<TStartupModule> : AbpAdminApplicationTestBase<TStartupModule>
    where TStartupModule : IAbpModule
{
    protected static readonly Guid AdminUserId = Guid.Parse("2e701e62-0953-4dd3-910b-dc6cc93ccb0d");

    private readonly INotificationManagementAppService _managementAppService;
    private readonly INotificationRepository _notificationRepository;
    private readonly INotificationDispatcher _dispatcher;
    private readonly IClock _clock;

    protected NotificationManagementAppServiceTests()
    {
        _managementAppService = GetRequiredService<INotificationManagementAppService>();
        _notificationRepository = GetRequiredService<INotificationRepository>();
        _dispatcher = GetRequiredService<INotificationDispatcher>();
        _clock = GetRequiredService<IClock>();
    }

    /// <summary>造一条已失败的站内信记录，返回其 Id。</summary>
    private async Task<Guid> CreateFailedNotificationAsync()
    {
        var title = "ut-mgmt-" + Guid.NewGuid().ToString("N")[..8];
        await WithUnitOfWorkAsync(async () =>
        {
            await _dispatcher.SendInAppAsync(new SendInAppNotificationInput
            {
                UserIds = new List<Guid> { AdminUserId },
                Title = title,
                Body = "正文"
            });
        });

        return await WithUnitOfWorkAsync(async () =>
        {
            var notification = (await _notificationRepository.GetListAsync(
                    n => n.NotificationMethod == InAppNotificationConsts.NotificationMethod))
                .OrderByDescending(n => n.CreationTime)
                .First();
            notification.SetResult(_clock, false, "ut-failure");
            await _notificationRepository.UpdateAsync(notification);
            return notification.Id;
        });
    }

    [Fact]
    public async Task RetryAsync_Should_Create_Retry_Record_Pointing_To_Original()
    {
        var originalId = await CreateFailedNotificationAsync();

        await WithUnitOfWorkAsync(async () => await _managementAppService.RetryAsync(originalId));

        await WithUnitOfWorkAsync(async () =>
        {
            var retry = (await _notificationRepository.GetListAsync(n => n.RetryForNotificationId == originalId))
                .ShouldHaveSingleItem();
            retry.Success.ShouldBeNull();
            retry.NotificationMethod.ShouldBe(InAppNotificationConsts.NotificationMethod);

            var original = await _notificationRepository.GetAsync(originalId);
            retry.NotificationInfoId.ShouldBe(original.NotificationInfoId);
            retry.UserId.ShouldBe(original.UserId);
        });
    }

    [Fact]
    public async Task RetryAsync_Should_Reject_NonFailed_Notification()
    {
        var title = "ut-pending-" + Guid.NewGuid().ToString("N")[..8];
        await WithUnitOfWorkAsync(async () =>
        {
            await _dispatcher.SendInAppAsync(new SendInAppNotificationInput
            {
                UserIds = new List<Guid> { AdminUserId },
                Title = title,
                Body = "正文"
            });
        });

        var pendingId = await WithUnitOfWorkAsync(async () =>
            (await _notificationRepository.GetListAsync(n => n.NotificationMethod == InAppNotificationConsts.NotificationMethod))
                .OrderByDescending(n => n.CreationTime)
                .First().Id);

        // Success == null（待发送）也不能重试——只有明确失败的可以
        await Assert.ThrowsAsync<BusinessException>(async () =>
            await WithUnitOfWorkAsync(async () => await _managementAppService.RetryAsync(pendingId)));
    }

    [Fact]
    public async Task GetList_Should_Exclude_Retries_By_Default_And_Include_On_Demand()
    {
        var originalId = await CreateFailedNotificationAsync();
        await WithUnitOfWorkAsync(async () => await _managementAppService.RetryAsync(originalId));

        var retryId = await WithUnitOfWorkAsync(async () =>
            (await _notificationRepository.GetListAsync(n => n.RetryForNotificationId == originalId))
                .Single().Id);

        var defaultList = await WithUnitOfWorkAsync(async () =>
            await _managementAppService.GetListAsync(new GetNotificationListInput
            {
                MaxResultCount = 100,
                NotificationMethod = InAppNotificationConsts.NotificationMethod
            }));

        // 原始记录在，重试记录不在
        defaultList.Items.ShouldNotContain(x => x.Id == retryId);
        var original = defaultList.Items.FirstOrDefault(x => x.Id == originalId);
        original.ShouldNotBeNull();
        original.RetryCount.ShouldBe(1);
        original.FinalSuccess.ShouldBeNull(); // 最后一次尝试是刚建的重试记录（未发送）

        var withRetries = await WithUnitOfWorkAsync(async () =>
            await _managementAppService.GetListAsync(new GetNotificationListInput
            {
                MaxResultCount = 100,
                NotificationMethod = InAppNotificationConsts.NotificationMethod,
                IncludeRetries = true
            }));

        withRetries.Items.ShouldContain(x => x.Id == retryId);
        withRetries.TotalCount.ShouldBe(defaultList.TotalCount + 1);
    }

    [Fact]
    public async Task GetAsync_Should_Return_Full_Attempt_Chain()
    {
        var originalId = await CreateFailedNotificationAsync();
        await WithUnitOfWorkAsync(async () => await _managementAppService.RetryAsync(originalId));

        var detail = await WithUnitOfWorkAsync(async () => await _managementAppService.GetAsync(originalId));

        detail.Attempts.Count.ShouldBe(2);
        detail.Attempts[0].Id.ShouldBe(originalId);
        detail.Attempts[0].Success.ShouldBe(false);
        detail.Attempts[1].Success.ShouldBeNull();
        detail.NotificationInfoProperties.ShouldNotBeNull();
        detail.NotificationInfoProperties!.ContainsKey(InAppNotificationConsts.TitlePropertyName).ShouldBeTrue();
    }
}
