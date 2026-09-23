using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using EasyAbp.NotificationService.NotificationInfos;
using EasyAbp.NotificationService.Notifications;
using Shouldly;
using Volo.Abp.BackgroundJobs;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Modularity;
using Volo.Abp.MultiTenancy;
using Xunit;

namespace AbpAdmin.Notifications;

/// <summary>
/// T3.5 INotificationDispatcher 门面测试：
/// 三个发送方法各自创建对应渠道的 Notification 记录；广播写批次记录并入队作业。
/// 发送本身由每通知一个的发送作业完成（测试环境 IsJobExecutionEnabled=false，手动执行）。
/// </summary>
public abstract class NotificationDispatcherTests<TStartupModule> : AbpAdminApplicationTestBase<TStartupModule>
    where TStartupModule : IAbpModule
{
    protected static readonly Guid AdminUserId = Guid.Parse("2e701e62-0953-4dd3-910b-dc6cc93ccb0d");

    private readonly INotificationDispatcher _dispatcher;
    private readonly INotificationRepository _notificationRepository;
    private readonly INotificationInfoRepository _notificationInfoRepository;
    private readonly IRepository<NotificationBroadcast, Guid> _broadcastRepository;
    private readonly IBackgroundJobRepository _backgroundJobRepository;

    protected NotificationDispatcherTests()
    {
        _dispatcher = GetRequiredService<INotificationDispatcher>();
        _notificationRepository = GetRequiredService<INotificationRepository>();
        _notificationInfoRepository = GetRequiredService<INotificationInfoRepository>();
        _broadcastRepository = GetRequiredService<IRepository<NotificationBroadcast, Guid>>();
        _backgroundJobRepository = GetRequiredService<IBackgroundJobRepository>();
    }

    [Fact]
    public async Task SendInAppAsync_Should_Create_InApp_Records()
    {
        var title = "ut-inapp-" + Guid.NewGuid().ToString("N")[..8];

        await WithUnitOfWorkAsync(async () =>
        {
            await _dispatcher.SendInAppAsync(new SendInAppNotificationInput
            {
                UserIds = new List<Guid> { AdminUserId },
                Title = title,
                Body = "正文"
            });
        });

        await WithUnitOfWorkAsync(async () =>
        {
            var notification = (await _notificationRepository.GetListAsync(
                n => n.UserId == AdminUserId && n.NotificationMethod == InAppNotificationConsts.NotificationMethod))
                .OrderByDescending(n => n.CreationTime)
                .First();
            notification.NotificationMethod.ShouldBe(InAppNotificationConsts.NotificationMethod);
            notification.Success.ShouldBeNull(); // 尚未发送（发送作业未执行）

            var info = await _notificationInfoRepository.GetAsync(notification.NotificationInfoId);
            info.GetInAppTitle().ShouldBe(title);
            info.GetInAppBody().ShouldBe("正文");
        });
    }

    [Fact]
    public async Task SendEmailAsync_Should_Create_Mailing_Records()
    {
        var subject = "ut-mail-" + Guid.NewGuid().ToString("N")[..8];

        await WithUnitOfWorkAsync(async () =>
        {
            await _dispatcher.SendEmailAsync(new SendEmailNotificationInput
            {
                UserIds = new List<Guid> { AdminUserId },
                Subject = subject,
                Body = "邮件正文"
            });
        });

        await WithUnitOfWorkAsync(async () =>
        {
            var notification = (await _notificationRepository.GetListAsync(
                n => n.UserId == AdminUserId && n.NotificationMethod == "Mailing"))
                .OrderByDescending(n => n.CreationTime)
                .First();
            var info = await _notificationInfoRepository.GetAsync(notification.NotificationInfoId);
            info.GetDataValue("Subject").ShouldBe(subject);
        });
    }

    [Fact]
    public async Task SendSmsAsync_Should_Create_Sms_Records_With_Properties()
    {
        await WithUnitOfWorkAsync(async () =>
        {
            await _dispatcher.SendSmsAsync(new SendSmsNotificationInput
            {
                UserIds = new List<Guid> { AdminUserId },
                Text = "{\"code\":\"123456\"}",
                Properties = new Dictionary<string, object> { ["TemplateCode"] = "SMS_123" }
            });
        });

        await WithUnitOfWorkAsync(async () =>
        {
            var notification = (await _notificationRepository.GetListAsync(
                n => n.UserId == AdminUserId && n.NotificationMethod == "Sms"))
                .OrderByDescending(n => n.CreationTime)
                .First();
            var info = await _notificationInfoRepository.GetAsync(notification.NotificationInfoId);
            info.GetDataValue("Text").ShouldBe("{\"code\":\"123456\"}");
            info.GetDataValue("JsonProperties")?.ToString().ShouldNotBeNull().ShouldContain("SMS_123");
        });
    }

    [Fact]
    public async Task BroadcastAsync_Should_Create_Broadcast_Record_And_Enqueue_Job()
    {
        Guid broadcastId = Guid.Empty;

        await WithUnitOfWorkAsync(async () =>
        {
            broadcastId = await _dispatcher.BroadcastAsync(new BroadcastNotificationInput
            {
                TargetType = NotificationBroadcastTargetTypes.All,
                NotificationMethods = new List<string> { InAppNotificationConsts.NotificationMethod },
                Title = "公告标题",
                Body = "公告正文"
            });
        });

        broadcastId.ShouldNotBe(Guid.Empty);

        await WithUnitOfWorkAsync(async () =>
        {
            var broadcast = await _broadcastRepository.GetAsync(broadcastId);
            broadcast.State.ShouldBe(NotificationBroadcastStates.Pending);
            broadcast.TotalCount.ShouldBeGreaterThan(0);

            var jobs = (await _backgroundJobRepository.GetListAsync())
                .Where(j => j.JobName.Contains(nameof(BroadcastNotificationJobArgs)))
                .ToList();
            jobs.ShouldNotBeEmpty();
        });
    }

    [Fact]
    public async Task SendInAppAsync_In_Tenant_Context_Should_Stamp_TenantId()
    {
        var tenantId = Guid.NewGuid();
        var title = "ut-tenant-" + Guid.NewGuid().ToString("N")[..8];

        // 租户上下文发送：记录必须带上租户 Id，且 host 上下文查不到（全局验收第 7 条）
        using (GetRequiredService<ICurrentTenant>().Change(tenantId))
        {
            await WithUnitOfWorkAsync(async () =>
            {
                await _dispatcher.SendInAppAsync(new SendInAppNotificationInput
                {
                    UserIds = new List<Guid> { AdminUserId },
                    Title = title,
                    Body = "租户内通知"
                });
            });

            await WithUnitOfWorkAsync(async () =>
            {
                var info = (await _notificationInfoRepository.GetListAsync())
                    .Where(i => i.GetInAppTitle() == title)
                    .ShouldHaveSingleItem();
                info.TenantId.ShouldBe(tenantId);

                var notification = await _notificationRepository.GetAsync(
                    n => n.NotificationInfoId == info.Id);
                notification.TenantId.ShouldBe(tenantId);
            });
        }

        // host 上下文不应看到该租户的记录
        await WithUnitOfWorkAsync(async () =>
        {
            var infos = await _notificationInfoRepository.GetListAsync();
            infos.ShouldNotContain(i => i.GetInAppTitle() == title);
        });
    }

    [Fact]
    public async Task SendInAppAsync_Should_Throw_BusinessException_When_UserIds_Empty()
    {
        // 空 UserIds 是业务校验错误（400），不是映射 500 的 ArgumentException
        var exception = await Should.ThrowAsync<Volo.Abp.BusinessException>(() =>
            WithUnitOfWorkAsync(async () =>
            {
                await _dispatcher.SendInAppAsync(new SendInAppNotificationInput
                {
                    UserIds = new List<Guid>(),
                    Title = "t",
                    Body = "b"
                });
            }));

        exception.Code.ShouldBe(AbpAdminDomainErrorCodes.Notifications.EmptyUserIds);
    }

    [Fact]
    public async Task BroadcastAsync_Should_Validate_Input()
    {
        await Assert.ThrowsAsync<Volo.Abp.BusinessException>(async () =>
        {
            await WithUnitOfWorkAsync(async () =>
            {
                await _dispatcher.BroadcastAsync(new BroadcastNotificationInput
                {
                    TargetType = "BadType",
                    NotificationMethods = new List<string> { InAppNotificationConsts.NotificationMethod },
                    Title = "t",
                    Body = "b"
                });
            });
        });

        await Assert.ThrowsAsync<Volo.Abp.BusinessException>(async () =>
        {
            await WithUnitOfWorkAsync(async () =>
            {
                await _dispatcher.BroadcastAsync(new BroadcastNotificationInput
                {
                    TargetType = NotificationBroadcastTargetTypes.Role,
                    TargetId = null, // Role 必须带 TargetId
                    NotificationMethods = new List<string> { InAppNotificationConsts.NotificationMethod },
                    Title = "t",
                    Body = "b"
                });
            });
        });
    }
}
