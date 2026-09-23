using System;
using System.Threading.Tasks;
using Shouldly;
using Volo.Abp.Modularity;
using Xunit;

namespace AbpAdmin.RealTime;

/* T3.2 IRealTimeNotifier 门面测试（无 SignalR 的宿主形态，即 DbMigrator / 单测的形状）。
 * 验收点：
 * - 没引 SignalR 时 IRealTimeNotifier 解析到 NullRealTimeNotifier（兜底存在，业务代码可注入）
 * - Null 实现的三个方法静默完成、绝不抛出（推送不能影响主流程）
 * SignalR 实现（SignalRRealTimeNotifier 替换 Null）的注册核对在
 * test/AbpAdmin.HttpApi.Host.Tests/SignalR/SignalRServiceRegistrationTests.cs。
 */
public abstract class RealTimeNotifierTests<TStartupModule> : AbpAdminDomainTestBase<TStartupModule>
    where TStartupModule : IAbpModule
{
    private readonly IRealTimeNotifier _notifier;

    protected RealTimeNotifierTests()
    {
        _notifier = GetRequiredService<IRealTimeNotifier>();
    }

    [Fact]
    public void Should_Resolve_NullRealTimeNotifier_Without_SignalR_Host()
    {
        _notifier.ShouldBeOfType<NullRealTimeNotifier>();
    }

    [Fact]
    public async Task Null_Notifier_Should_Complete_Silently()
    {
        var message = new RealTimeMessage
        {
            Name = RealTimeMessageNames.Notification,
            Payload = new { Title = "test" }
        };

        await _notifier.NotifyUserAsync(Guid.NewGuid(), message);
        await _notifier.NotifyUsersAsync([Guid.NewGuid(), Guid.NewGuid()], message);
        await _notifier.NotifyTenantAsync(Guid.NewGuid(), message);
        await _notifier.NotifyTenantAsync(null, message);
    }
}
