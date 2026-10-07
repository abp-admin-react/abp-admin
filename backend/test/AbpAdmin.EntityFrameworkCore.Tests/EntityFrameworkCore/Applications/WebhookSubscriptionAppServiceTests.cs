using System;
using System.Linq;
using System.Threading.Tasks;
using AbpAdmin;
using AbpAdmin.Webhooks;
using Shouldly;
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
        var exception = await Should.ThrowAsync<ArgumentException>(() =>
            _subscriptionAppService.CreateAsync(new CreateWebhookSubscriptionInput
            {
                WebhookUri = "https://example.com/hook",
                Secret = "s-0123456789",
                Events = new() { "a.b", "a.b" },
            }));

        exception.Message.ShouldContain("a.b");
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
