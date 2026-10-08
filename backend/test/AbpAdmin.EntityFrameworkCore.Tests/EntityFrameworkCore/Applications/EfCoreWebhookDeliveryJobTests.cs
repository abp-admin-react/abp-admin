using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using AbpAdmin;
using AbpAdmin.Webhooks;
using AbpAdmin.Webhooks.Delivery;
using Shouldly;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Guids;
using Volo.Abp.Modularity;
using Xunit;

namespace AbpAdmin.EntityFrameworkCore.Applications;

/* Webhook 投递作业的 SSRF 防线回归：订阅创建/更新入口已拦内网目标（见
 * WebhookSubscriptionAppServiceTests），这里钉住发送前的第二道校验——兜住
 * "校验上线前的存量订阅行"与"提交后 DNS 切到内网记录"两条路径：拦截 = 不发请求、
 * 不重试、直接落失败终态（ResponseBody 带被拒主机，管理端可读出原因）。 */
[Collection(AbpAdminTestConsts.CollectionDefinitionName)]
public class EfCoreWebhookDeliveryJobTests : AbpAdminApplicationTestBase<WebhooksTestsModule>
{
    private readonly IRepository<WebhookSubscription, Guid> _subscriptionRepository;
    private readonly IRepository<WebhookSendRecord, Guid> _sendRecordRepository;
    private readonly WebhookDeliveryJob _deliveryJob;
    private readonly HttpStubs.RecordingWebhookDeliveryHandler _deliveryHandler;
    private readonly IGuidGenerator _guidGenerator;

    public EfCoreWebhookDeliveryJobTests()
    {
        _subscriptionRepository = GetRequiredService<IRepository<WebhookSubscription, Guid>>();
        _sendRecordRepository = GetRequiredService<IRepository<WebhookSendRecord, Guid>>();
        _deliveryJob = GetRequiredService<WebhookDeliveryJob>();
        _deliveryHandler = GetRequiredService<HttpStubs.RecordingWebhookDeliveryHandler>();
        _guidGenerator = GetRequiredService<IGuidGenerator>();
    }

    [Fact]
    public async Task Intranet_subscription_target_blocked_without_request()
    {
        // 绕过 AppService 校验直插存量行（模拟校验上线前已落库的内网订阅）
        var subscription = await _subscriptionRepository.InsertAsync(
            new WebhookSubscription(
                _guidGenerator.Create(),
                "http://169.254.169.254/latest/meta-data/",
                "s-0123456789",
                new List<string> { "e1" }),
            autoSave: true);

        var record = await _sendRecordRepository.InsertAsync(
            new WebhookSendRecord(_guidGenerator.Create(), subscription.Id, "e1", "{}", DateTime.UtcNow),
            autoSave: true);

        // 拦截路径断言零外呼：投递替身（WebhooksTestsModule 挂主处理器位）收不到任何请求——
        // 若防线回归，真实出站会打到 169.254.169.254（ RecordedRequests 非空 + 终态断言双红）
        _deliveryHandler.Reset();
        await _deliveryJob.ExecuteAsync(new WebhookDeliveryJobArgs
        {
            SubscriptionId = subscription.Id,
            SendRecordId = record.Id,
            EventName = "e1",
            Payload = "{}",
        });

        _deliveryHandler.RecordedRequests.ShouldBeEmpty();
        var reloaded = await _sendRecordRepository.GetAsync(record.Id);
        reloaded.Succeeded.ShouldBeFalse();
        reloaded.ResponseStatusCode.ShouldBeNull();
        reloaded.ResponseBody.ShouldNotBeNull();
        reloaded.ResponseBody.ShouldContain("blocked");
        reloaded.ResponseBody.ShouldContain("169.254.169.254");
        reloaded.AttemptCount.ShouldBe(1);
    }
}
