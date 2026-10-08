using System;
using System.Linq;
using System.Threading.Tasks;
using AbpAdmin;
using AbpAdmin.Webhooks;
using Shouldly;
using Volo.Abp;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Modularity;
using Xunit;

namespace AbpAdmin.EntityFrameworkCore.Applications;

/* Webhook 订阅管理集成测试（WebhooksTestsModule 提供内存 SQLite 双上下文）。
 * 断言点：CRUD 语义、密钥不回传、事件集整体替换、级联删除、订阅筛选（发布路径）。 */
public abstract class WebhookSubscriptionAppServiceTests<TStartupModule> : AbpAdminApplicationTestBase<TStartupModule>
    where TStartupModule : IAbpModule
{
    private readonly IWebhookSubscriptionAppService _subscriptionAppService;
    private readonly IRepository<WebhookSubscriptionEvent> _eventRepository;

    protected WebhookSubscriptionAppServiceTests()
    {
        _subscriptionAppService = GetRequiredService<IWebhookSubscriptionAppService>();
        _eventRepository = GetRequiredService<IRepository<WebhookSubscriptionEvent>>();
    }

    [Fact]
    public async Task Create_Get_Update_Delete_Full_Lifecycle()
    {
        // Create：密钥不回传（HasSecret=true），事件集落库
        var created = await _subscriptionAppService.CreateAsync(new CreateWebhookSubscriptionInput
        {
            WebhookUri = "https://example.com/hook",
            Secret = "test-secret-0123456789",
            Description = "E2E 订阅",
            IsActive = true,
            Events = new() { "identity.user.created", "identity.user.updated" },
        });

        created.HasSecret.ShouldBeTrue();
        created.Events.Count.ShouldBe(2);

        var got = await _subscriptionAppService.GetAsync(created.Id);
        got.WebhookUri.ShouldBe("https://example.com/hook");
        got.Events.ShouldContain("identity.user.created");

        // Update：不传密钥（保持原值）+ 事件集整体替换
        var updated = await _subscriptionAppService.UpdateAsync(created.Id, new UpdateWebhookSubscriptionInput
        {
            WebhookUri = "https://example.com/hook-v2",
            Secret = null, // 留空 = 保持原密钥
            Description = "E2E 订阅 v2",
            IsActive = false,
            Events = new() { "identity.user.deleted" },
        });

        updated.WebhookUri.ShouldBe("https://example.com/hook-v2");
        updated.IsActive.ShouldBeFalse();
        updated.Events.ShouldBe(new[] { "identity.user.deleted" });
        updated.HasSecret.ShouldBeTrue(); // 原密钥仍在

        // Delete：级联删除事件子表
        await _subscriptionAppService.DeleteAsync(created.Id);
        // GetAsync 对已删除订阅抛 EntityNotFoundException（ABP 约定）
        await Should.ThrowAsync<Volo.Abp.Domain.Entities.EntityNotFoundException>(async () =>
            await _subscriptionAppService.GetAsync(created.Id));
        var orphanEvents = await _eventRepository.GetListAsync(e => e.SubscriptionId == created.Id);
        orphanEvents.ShouldBeEmpty();
    }

    [Fact]
    public async Task Create_Should_Reject_Duplicate_Events()
    {
        // 应用服务层前置转换为用户可见 400（实体层保留 ArgumentException 编程契约守卫）
        var exception = await Should.ThrowAsync<UserFriendlyException>(() =>
            _subscriptionAppService.CreateAsync(new CreateWebhookSubscriptionInput
            {
                WebhookUri = "https://example.com/hook",
                Secret = "s-0123456789",
                Events = new() { "a.b", "a.b" },
            }));

        exception.Message.ShouldContain("a.b");
    }

    [Theory]
    [InlineData("http://169.254.169.254/latest/meta-data/", "169.254.169.254")] // 云元数据端点
    [InlineData("http://127.0.0.1:9000/hook", "127.0.0.1")]                     // loopback
    [InlineData("http://10.0.0.5/hook", "10.0.0.5")]                            // RFC1918
    [InlineData("http://[::1]/hook", "::1")]                                    // IPv6 loopback
    public async Task Create_Should_Reject_Intranet_Targets(string uri, string blockedHost)
    {
        // SSRF 防线：只校验 scheme 挡不住"订阅指向内网/云元数据，借服务端 POST 探测并回读响应"；
        // 断言违规主机出现在错误消息里（与本地化语言无关）
        var exception = await Should.ThrowAsync<UserFriendlyException>(() =>
            _subscriptionAppService.CreateAsync(new CreateWebhookSubscriptionInput
            {
                WebhookUri = uri,
                Secret = "s-0123456789",
                Events = new() { "e1" },
            }));

        exception.Message.ShouldContain(blockedHost);
    }

    [Fact]
    public async Task Create_Should_Reject_Intranet_Domain_Via_Dns()
    {
        // 域名路径：localhost 解析到 loopback 同样被拒（DNS 解析后校验，防"域名合法但指向内网"）
        await Should.ThrowAsync<UserFriendlyException>(() =>
            _subscriptionAppService.CreateAsync(new CreateWebhookSubscriptionInput
            {
                WebhookUri = "http://localhost/hook",
                Secret = "s-0123456789",
                Events = new() { "e1" },
            }));
    }

    [Fact]
    public async Task Update_Should_Reject_Intranet_Target()
    {
        var created = await _subscriptionAppService.CreateAsync(new CreateWebhookSubscriptionInput
        {
            WebhookUri = "https://example.com/hook",
            Secret = "s-0123456789",
            Events = new() { "e1" },
        });

        // 编辑指向内网同样拦截（防"先建合法订阅再改指向"）
        var exception = await Should.ThrowAsync<UserFriendlyException>(() =>
            _subscriptionAppService.UpdateAsync(created.Id, new UpdateWebhookSubscriptionInput
            {
                WebhookUri = "http://192.168.1.10/hook",
                Description = "改指向内网",
                IsActive = true,
                Events = new() { "e1" },
            }));

        exception.Message.ShouldContain("192.168.1.10");

        // 原订阅未被改动
        var unchanged = await _subscriptionAppService.GetAsync(created.Id);
        unchanged.WebhookUri.ShouldBe("https://example.com/hook");
    }

    [Fact]
    public async Task GetList_Should_Filter_By_IsActive_And_Text()
    {
        var active = await _subscriptionAppService.CreateAsync(new CreateWebhookSubscriptionInput
        {
            WebhookUri = $"https://example.com/{Guid.NewGuid():N}",
            Secret = "s-0123456789",
            Description = "活跃订阅标记XYZ",
            IsActive = true,
            Events = new() { "e1" },
        });
        var paused = await _subscriptionAppService.CreateAsync(new CreateWebhookSubscriptionInput
        {
            WebhookUri = $"https://example.com/{Guid.NewGuid():N}",
            Secret = "s-0123456789",
            Description = "暂停订阅",
            IsActive = false,
            Events = new() { "e1" },
        });

        var byStatus = await _subscriptionAppService.GetListAsync(new GetWebhookSubscriptionListInput
        {
            IsActive = false,
            MaxResultCount = 100,
        });
        byStatus.Items.ShouldNotContain(s => s.Id == active.Id);
        byStatus.Items.ShouldContain(s => s.Id == paused.Id);

        var byText = await _subscriptionAppService.GetListAsync(new GetWebhookSubscriptionListInput
        {
            Filter = "XYZ",
            MaxResultCount = 100,
        });
        byText.TotalCount.ShouldBe(1);
        byText.Items[0].Id.ShouldBe(active.Id);
    }
}
