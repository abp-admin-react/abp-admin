using System;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Volo.Abp.BackgroundJobs;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.MultiTenancy;
using Volo.Abp.Timing;
using Volo.Abp.Uow;

namespace AbpAdmin.Webhooks.Delivery;

/// <summary>
/// 投递作业：以订阅配置的密钥做 HMAC-SHA256 签名（对 "{timestampSeconds}.{body}"），
/// 头部 X-AbpAdmin-Event / X-AbpAdmin-Timestamp / X-AbpAdmin-Signature；收端按同一算法验签即防伪造。
/// 失败原地重试（退避 2s/4s，共 3 次尝试，见 <see cref="RetryBackoffs"/>），终态写回
/// SendRecord（成功/最终失败 + 尝试次数 + 响应摘要）。
/// UoW：终态写回走 <see cref="SaveResultAsync"/> 的独立新 UoW（requiresNew）——终态必须落库，
/// 不与作业里其他潜在写操作共生死；除此之外本作业不开环境 UoW。
/// 日志语义：还会重试的失败=Warning（带 retrying）；最后一次失败=Error；订阅已删/暂停=静默落失败终态。
/// </summary>
public class WebhookDeliveryJob : AsyncBackgroundJob<WebhookDeliveryJobArgs>, ITransientDependency
{
    private static readonly TimeSpan HttpTimeout = TimeSpan.FromSeconds(AbpAdminWebhooksConsts.DeliveryTimeoutSeconds);
    private static readonly TimeSpan[] RetryBackoffs = [TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(4)];

    private readonly IServiceProvider _serviceProvider;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IUnitOfWorkManager _unitOfWorkManager;
    private readonly IClock _clock;

    public ILogger<WebhookDeliveryJob> Logger { get; set; } = NullLogger<WebhookDeliveryJob>.Instance;

    public WebhookDeliveryJob(
        IServiceProvider serviceProvider,
        IHttpClientFactory httpClientFactory,
        IUnitOfWorkManager unitOfWorkManager,
        IClock clock)
    {
        _serviceProvider = serviceProvider;
        _httpClientFactory = httpClientFactory;
        _unitOfWorkManager = unitOfWorkManager;
        _clock = clock;
    }

    public override async Task ExecuteAsync(WebhookDeliveryJobArgs args)
    {
        // 作业在无请求作用域执行；SendRecord 的租户过滤需要显式租户上下文
        using (_serviceProvider.GetRequiredService<ICurrentTenant>().Change(args.TenantId))
        {
            var recordRepository = _serviceProvider.GetRequiredService<IRepository<WebhookSendRecord, Guid>>();
            var subscriptionRepository = _serviceProvider.GetRequiredService<IRepository<WebhookSubscription, Guid>>();

            var subscription = await subscriptionRepository.FindAsync(args.SubscriptionId);
            if (subscription == null || !subscription.IsActive)
            {
                // 订阅已删/暂停：记录终态为失败（原因在无响应可讲，状态码留空）
                await SaveResultAsync(recordRepository, args, succeeded: false, statusCode: null,
                    responseBody: "subscription removed or paused", attemptCount: 1);
                return;
            }

            var timestamp = new DateTimeOffset(_clock.Now).ToUnixTimeSeconds().ToString();
            var signature = ComputeSignature(subscription.Secret, timestamp, args.Payload);

            HttpResponseMessage? response = null;
            var attempts = 0;
            try
            {
                var client = _httpClientFactory.CreateClient(WebhookDeliveryHttpClientExtensions.ClientName);
                client.Timeout = HttpTimeout;

                do
                {
                    attempts++;
                    try
                    {
                        using var request = new HttpRequestMessage(HttpMethod.Post, subscription.WebhookUri);
                        request.Headers.TryAddWithoutValidation("X-AbpAdmin-Event", args.EventName);
                        request.Headers.TryAddWithoutValidation("X-AbpAdmin-Timestamp", timestamp);
                        request.Headers.TryAddWithoutValidation("X-AbpAdmin-Signature", signature);
                        request.Content = new StringContent(args.Payload, Encoding.UTF8, "application/json");

                        response = await client.SendAsync(request);
                        if ((int)response.StatusCode is >= 200 and < 300)
                        {
                            break; // 成功
                        }
                    }
                    catch (Exception ex)
                    {
                        // 网络层失败也计入尝试：重试耗尽后走"无响应"终态，不让异常逃出作业。
                        // 日志级别对齐语义：还会重试的失败=Warning；最后一次失败=Error（终态已注定）
                        var isFinalAttempt = attempts >= RetryBackoffs.Length + 1;
                        if (isFinalAttempt)
                        {
                            Logger.LogError(ex, "Webhook delivery failed after {Attempt} attempts for {Uri}.",
                                attempts, subscription.WebhookUri);
                        }
                        else
                        {
                            Logger.LogWarning(ex, "Webhook delivery attempt {Attempt} failed for {Uri}, retrying.",
                                attempts, subscription.WebhookUri);
                        }
                    }

                    if (attempts < RetryBackoffs.Length + 1)
                    {
                        await Task.Delay(RetryBackoffs[attempts - 1]);
                    }
                } while (attempts < RetryBackoffs.Length + 1);

                var succeeded = response != null
                    && (int)response.StatusCode is >= 200 and < 300;
                var body = response == null
                    ? "no response (network error)"
                    : await response.Content.ReadAsStringAsync();

                await SaveResultAsync(recordRepository, args, succeeded,
                    (int?)response?.StatusCode, body, attempts);
            }
            finally
            {
                response?.Dispose();
            }
        }
    }

    private async Task SaveResultAsync(
        IRepository<WebhookSendRecord, Guid> recordRepository,
        WebhookDeliveryJobArgs args,
        bool succeeded,
        int? statusCode,
        string responseBody,
        int attemptCount)
    {
        // 独立短 UoW：交付终态必须落库，即使作业环境 UoW 有其他成员失败也不被连带回滚
        using var uow = _unitOfWorkManager.Begin(requiresNew: true);
        var record = await recordRepository.GetAsync(args.SendRecordId);
        record.SetResult(succeeded, statusCode, responseBody, attemptCount, _clock.Now);
        await recordRepository.UpdateAsync(record);
        await uow.CompleteAsync();
    }

    /// <summary>HMAC-SHA256("{timestamp}.{body}", secret) 的十六进制小写。</summary>
    private static string ComputeSignature(string secret, string timestamp, string body)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        var bytes = Encoding.UTF8.GetBytes($"{timestamp}.{body}");
        return Convert.ToHexString(hmac.ComputeHash(bytes)).ToLowerInvariant();
    }
}

/// <summary>命名 HTTP 客户端（投递专用；超时在作业内按次设置）。</summary>
public static class WebhookDeliveryHttpClientExtensions
{
    public const string ClientName = "AbpAdminWebhookDelivery";

    public static IServiceCollection AddWebhookDeliveryHttpClient(this IServiceCollection services)
    {
        _ = services.AddHttpClient(ClientName);
        return services;
    }
}
