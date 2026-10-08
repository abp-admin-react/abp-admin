using System;
using System.Threading.Tasks;
using AbpAdmin;
using AbpAdmin.Monitoring;
using AbpAdmin.Webhooks;
using Shouldly;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.EventBus.Local;
using Volo.Abp.Uow;
using Xunit;

namespace AbpAdmin.EntityFrameworkCore.Applications;

/* 探活失败 → Webhook 桥接（HttpProbeFailedWebhookEventHandler，Webhooks 模块首个业务事件源）。
 * 断言点：订阅了 AbpAdmin.HttpProbe.Failed 的活跃订阅在事件发布后产生 SendRecord（payload 带探活
 * 上下文）；暂停订阅不产生。测试基座不执行后台作业，SendRecord 停在 pending 属预期。
 * 生产方（HttpProbeJobHandler）用 onUnitOfWorkComplete:false 立即分发；此处经 UoW 内发布的
 * 姿势顺带锚定"常规缓冲发布→提交后投递"这条同事件的另一条通路。 */
[Collection(AbpAdminTestConsts.CollectionDefinitionName)]
public class EfCoreHttpProbeFailedWebhookBridgeTests : AbpAdminApplicationTestBase<WebhooksTestsModule>
{
    private readonly IWebhookSubscriptionAppService _subscriptionAppService;
    private readonly IRepository<WebhookSendRecord, Guid> _sendRecordRepository;
    private readonly ILocalEventBus _localEventBus;
    private readonly IUnitOfWorkManager _unitOfWorkManager;

    public EfCoreHttpProbeFailedWebhookBridgeTests()
    {
        _subscriptionAppService = GetRequiredService<IWebhookSubscriptionAppService>();
        _sendRecordRepository = GetRequiredService<IRepository<WebhookSendRecord, Guid>>();
        _localEventBus = GetRequiredService<ILocalEventBus>();
        _unitOfWorkManager = GetRequiredService<IUnitOfWorkManager>();
    }

    [Fact]
    public async Task Published_probe_failure_creates_send_record_for_subscriber()
    {
        var subscription = await _subscriptionAppService.CreateAsync(new CreateWebhookSubscriptionInput
        {
            WebhookUri = "https://example.com/hook",
            Secret = "s-0123456789",
            IsActive = true,
            Events = new() { HttpProbeFailedEto.WebhookEventName },
        });

        using (var uow = _unitOfWorkManager.Begin())
        {
            await _localEventBus.PublishAsync(new HttpProbeFailedEto
            {
                Url = "http://probe.test/health",
                StatusCode = 503,
                ErrorMessage = "unexpected status code 503 Service Unavailable",
                OccurredAt = DateTime.UtcNow,
            });
            await uow.CompleteAsync();
        }

        var records = await _sendRecordRepository.GetListAsync(r => r.SubscriptionId == subscription.Id);
        var record = records.ShouldHaveSingleItem();
        record.EventName.ShouldBe(HttpProbeFailedEto.WebhookEventName);
        record.Payload.ShouldContain("probe.test/health");
    }

    [Fact]
    public async Task Paused_subscription_is_skipped()
    {
        var paused = await _subscriptionAppService.CreateAsync(new CreateWebhookSubscriptionInput
        {
            WebhookUri = "https://example.com/paused",
            Secret = "s-0123456789",
            IsActive = false,
            Events = new() { HttpProbeFailedEto.WebhookEventName },
        });

        using (var uow = _unitOfWorkManager.Begin())
        {
            await _localEventBus.PublishAsync(new HttpProbeFailedEto
            {
                Url = "http://probe.test/health",
                OccurredAt = DateTime.UtcNow,
            });
            await uow.CompleteAsync();
        }

        var records = await _sendRecordRepository.GetListAsync(r => r.SubscriptionId == paused.Id);
        records.ShouldBeEmpty();
    }
}
