using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AbpAdmin;
using AbpAdmin.Monitoring;
using AbpAdmin.ScheduledJobs;
using AbpAdmin.Webhooks;
using AbpAdmin.Webhooks.Delivery;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Shouldly;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Guids;
using Volo.Abp.Modularity;
using Xunit;

namespace AbpAdmin.EntityFrameworkCore.Applications;

/* SSRF 放行开关（AllowIntranetTargets）的 allow-path 集成锚。
 * deny-path 由 WebhookSubscriptionAppServiceTests / HttpProbeJobHandlerTests /
 * EfCoreWebhookDeliveryJobTests 钉住；这里补反方向：内网部署显式置 true 后，
 * 三个校验站点（订阅创建、探活执行、投递作业）都必须真的放行——否则开关失灵
 * （键改名/配置节丢失/判定回归）时内网部署全静默断功能，无测试可红。
 * 开关判定本体（显式 "true" 才放行、垃圾值拒绝）已由 SafeHttpUrlTests 的
 * 门控重载单测钉住，这里钉的是"接线"：各站点把正确的键喂给了同一入口。
 * 配置覆盖：派生模块在基线模块之后 Replace IConfiguration（同键 + 两个放行键）。
 */
[DependsOn(typeof(WebhooksTestsModule))]
public class AbpAdminEntityFrameworkCoreAllowIntranetTestModule : AbpModule
{
    public override void ConfigureServices(ServiceConfigurationContext context)
    {
        // 与 AbpAdminApplicationTestModule 的内存配置同键（保持其余消费方语义不变），
        // 追加两个放行开关——晚于基线模块 Replace，最后生效
        context.Services.Replace(ServiceDescriptor.Singleton<IConfiguration>(
            new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["App:SelfUrl"] = "https://test.local",
                ["App:SpaUrl"] = "http://test.localhost:8000",
                ["AuthServer:Authority"] = "https://test.local",
                ["Webhooks:AllowIntranetTargets"] = "true",
                ["HttpProbe:AllowIntranetTargets"] = "true",
            }).Build()));
    }
}

[Collection(AbpAdminTestConsts.CollectionDefinitionName)]
public class EfCoreWebhookSubscriptionAllowIntranetTests : AbpAdminApplicationTestBase<AbpAdminEntityFrameworkCoreAllowIntranetTestModule>
{
    private readonly IWebhookSubscriptionAppService _subscriptionAppService;

    public EfCoreWebhookSubscriptionAllowIntranetTests()
    {
        _subscriptionAppService = GetRequiredService<IWebhookSubscriptionAppService>();
    }

    [Fact]
    public async Task Create_allows_intranet_target_when_switch_is_true()
    {
        // deny-path 反向锚：WebhookSubscriptionAppServiceTests 里同样的 URI 在默认配置下被拒
        var created = await _subscriptionAppService.CreateAsync(new CreateWebhookSubscriptionInput
        {
            WebhookUri = "http://192.168.1.10/hook",
            Secret = "s-0123456789",
            Events = new() { "e1" },
        });

        created.WebhookUri.ShouldBe("http://192.168.1.10/hook");
    }
}

[Collection(AbpAdminTestConsts.CollectionDefinitionName)]
public class EfCoreHttpProbeAllowIntranetTests : AbpAdminApplicationTestBase<AbpAdminEntityFrameworkCoreAllowIntranetTestModule>
{
    [Fact]
    public async Task Probe_reaches_intranet_target_when_switch_is_true()
    {
        var handler = GetRequiredService<HttpStubs.RecordingHttpProbeHandler>();
        var jobHandler = GetRequiredService<IEnumerable<IScheduledJobHandler>>()
            .OfType<HttpProbeJobHandler>().ShouldHaveSingleItem();
        handler.Reset();
        handler.Enqueue(System.Net.HttpStatusCode.OK, "");

        // deny-path 反向锚：同一 URL 在默认配置下被拦（不发请求、抛 AbpException）
        await jobHandler.ExecuteAsync(new ScheduledJobContext
        {
            JobType = ScheduledJobConsts.HttpProbeJobType,
            Payload = """{"Url":"http://10.0.0.5/health"}""",
            CancellationToken = CancellationToken.None,
        });

        handler.RecordedRequests.ShouldHaveSingleItem()
            .RequestUri.ToString().ShouldBe("http://10.0.0.5/health");
    }
}

[Collection(AbpAdminTestConsts.CollectionDefinitionName)]
public class EfCoreWebhookDeliveryAllowIntranetTests : AbpAdminApplicationTestBase<AbpAdminEntityFrameworkCoreAllowIntranetTestModule>
{
    [Fact]
    public async Task Delivery_reaches_intranet_target_when_switch_is_true()
    {
        var handler = GetRequiredService<HttpStubs.RecordingWebhookDeliveryHandler>();
        var deliveryJob = GetRequiredService<WebhookDeliveryJob>();
        var subscriptionRepository = GetRequiredService<IRepository<WebhookSubscription, Guid>>();
        var sendRecordRepository = GetRequiredService<IRepository<WebhookSendRecord, Guid>>();
        var guidGenerator = GetRequiredService<IGuidGenerator>();
        handler.Reset();
        handler.Enqueue(System.Net.HttpStatusCode.OK, "");

        // 绕过 AppService 直插存量内网订阅（与 deny-path 测试同构）
        var subscription = await subscriptionRepository.InsertAsync(
            new WebhookSubscription(
                guidGenerator.Create(),
                "http://10.0.0.5/hook",
                "s-0123456789",
                new List<string> { "e1" }),
            autoSave: true);
        var record = await sendRecordRepository.InsertAsync(
            new WebhookSendRecord(guidGenerator.Create(), subscription.Id, "e1", "{}", DateTime.UtcNow),
            autoSave: true);

        await deliveryJob.ExecuteAsync(new WebhookDeliveryJobArgs
        {
            SubscriptionId = subscription.Id,
            SendRecordId = record.Id,
            EventName = "e1",
            Payload = "{}",
        });

        // 放行 = 请求真实发起（投递替身收到）+ 2xx 终态成功
        handler.RecordedRequests.ShouldHaveSingleItem()
            .RequestUri.ToString().ShouldBe("http://10.0.0.5/hook");
        var reloaded = await sendRecordRepository.GetAsync(record.Id);
        reloaded.Succeeded.ShouldBeTrue();
    }
}
