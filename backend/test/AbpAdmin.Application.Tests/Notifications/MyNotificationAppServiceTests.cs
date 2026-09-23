using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using EasyAbp.NotificationService.Notifications;
using Shouldly;
using Volo.Abp.Data;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Identity;
using Volo.Abp.Modularity;
using Volo.Abp.Security.Claims;
using Volo.Abp.Users;
using Xunit;

namespace AbpAdmin.Notifications;

/// <summary>
/// T3.5 "我的通知"测试（第 11 步核心验收）：
/// 只返回自己的站内信；未读数 3 → 全部已读 0 → 再发 1 → 1。
/// </summary>
public abstract class MyNotificationAppServiceTests<TStartupModule> : AbpAdminApplicationTestBase<TStartupModule>
    where TStartupModule : IAbpModule
{
    protected static readonly Guid AdminUserId = Guid.Parse("2e701e62-0953-4dd3-910b-dc6cc93ccb0d");

    private readonly IMyNotificationAppService _myNotificationAppService;
    private readonly INotificationDispatcher _dispatcher;
    private readonly INotificationRepository _notificationRepository;
    private readonly ICurrentPrincipalAccessor _currentPrincipalAccessor;
    private readonly IdentityUserManager _userManager;

    protected MyNotificationAppServiceTests()
    {
        _myNotificationAppService = GetRequiredService<IMyNotificationAppService>();
        _dispatcher = GetRequiredService<INotificationDispatcher>();
        _notificationRepository = GetRequiredService<INotificationRepository>();
        _currentPrincipalAccessor = GetRequiredService<ICurrentPrincipalAccessor>();
        _userManager = GetRequiredService<IdentityUserManager>();
    }

    private IDisposable ChangeCurrentUser(Guid userId, string userName)
    {
        return _currentPrincipalAccessor.Change(new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim(AbpClaimTypes.UserId, userId.ToString()),
            new Claim(AbpClaimTypes.UserName, userName)
        })));
    }

    private async Task SendInAppAsync(Guid userId, string title)
    {
        await WithUnitOfWorkAsync(async () =>
        {
            await _dispatcher.SendInAppAsync(new SendInAppNotificationInput
            {
                UserIds = new List<Guid> { userId },
                Title = title,
                Body = "正文"
            });
        });
    }

    [Fact]
    public async Task GetList_Should_Return_Only_Own_InApp_Notifications()
    {
        // 另一个用户
        var otherUser = new IdentityUser(Guid.NewGuid(), "t35-other-" + Guid.NewGuid().ToString("N")[..6], "t35-other@abp.io");
        (await _userManager.CreateAsync(otherUser, "Test@123456")).Succeeded.ShouldBeTrue();

        var titleA = "ut-mine-" + Guid.NewGuid().ToString("N")[..8];
        var titleB = "ut-other-" + Guid.NewGuid().ToString("N")[..8];
        await SendInAppAsync(AdminUserId, titleA);
        await SendInAppAsync(otherUser.Id, titleB);
        // 给 admin 发一条短信渠道的通知——不应出现在"我的通知"里
        await WithUnitOfWorkAsync(async () =>
        {
            await _dispatcher.SendSmsAsync(new SendSmsNotificationInput
            {
                UserIds = new List<Guid> { AdminUserId },
                Text = "{\"code\":\"1\"}"
            });
        });

        var result = await WithUnitOfWorkAsync(async () =>
            await _myNotificationAppService.GetListAsync(new GetMyNotificationsInput { MaxResultCount = 100 }));

        result.Items.ShouldContain(x => x.Title == titleA);
        result.Items.ShouldNotContain(x => x.Title == titleB);
        result.Items.ShouldAllBe(x => x.Title != null); // 短信记录的 Title 为 null 不会进来
        result.Items.ShouldNotContain(x => x.Title == null);
    }

    [Fact]
    public async Task Cross_User_Access_Should_Not_Leak()
    {
        var userA = new IdentityUser(Guid.NewGuid(), "t35-a-" + Guid.NewGuid().ToString("N")[..6], "t35-a@abp.io");
        var userB = new IdentityUser(Guid.NewGuid(), "t35-b-" + Guid.NewGuid().ToString("N")[..6], "t35-b@abp.io");
        (await _userManager.CreateAsync(userA, "Test@123456")).Succeeded.ShouldBeTrue();
        (await _userManager.CreateAsync(userB, "Test@123456")).Succeeded.ShouldBeTrue();

        var titleB = "ut-b-" + Guid.NewGuid().ToString("N")[..8];
        await SendInAppAsync(userB.Id, titleB);

        using (ChangeCurrentUser(userA.Id, userA.UserName))
        {
            var result = await WithUnitOfWorkAsync(async () =>
                await _myNotificationAppService.GetListAsync(new GetMyNotificationsInput { MaxResultCount = 100 }));
            result.Items.ShouldNotContain(x => x.Title == titleB);

            var unread = await WithUnitOfWorkAsync(async () => await _myNotificationAppService.GetUnreadCountAsync());
            unread.Count.ShouldBe(0);
        }
    }

    [Fact]
    public async Task UnreadCount_Flow_Should_Be_3_0_1()
    {
        var user = new IdentityUser(Guid.NewGuid(), "t35-cnt-" + Guid.NewGuid().ToString("N")[..6], "t35-cnt@abp.io");
        (await _userManager.CreateAsync(user, "Test@123456")).Succeeded.ShouldBeTrue();

        using (ChangeCurrentUser(user.Id, user.UserName))
        {
            var marker = Guid.NewGuid().ToString("N")[..8];
            await SendInAppAsync(user.Id, "ut-c1-" + marker);
            await SendInAppAsync(user.Id, "ut-c2-" + marker);
            await SendInAppAsync(user.Id, "ut-c3-" + marker);

            var unread = await WithUnitOfWorkAsync(async () => await _myNotificationAppService.GetUnreadCountAsync());
            unread.Count.ShouldBe(3);

            await WithUnitOfWorkAsync(async () => await _myNotificationAppService.MarkAllAsReadAsync());

            unread = await WithUnitOfWorkAsync(async () => await _myNotificationAppService.GetUnreadCountAsync());
            unread.Count.ShouldBe(0);

            // Windows 上 DateTime.Now 分辨率约 15ms——等过一个 tick 边界，
            // 保证新通知 CreationTime 的 Ticks 严格大于已读时间戳
            await Task.Delay(100);
            await SendInAppAsync(user.Id, "ut-c4-" + marker);

            unread = await WithUnitOfWorkAsync(async () => await _myNotificationAppService.GetUnreadCountAsync());
            unread.Count.ShouldBe(1);
        }
    }
}
