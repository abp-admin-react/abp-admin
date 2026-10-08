using System;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using HttpAgent;
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
/// 出站走 HttpAgent（IHttpRemoteService + HttpRequestBuilder，底层仍是命名 HttpClient
/// AbpAdminWebhookDelivery）：重试交框架重试策略——间隔 2s/4s 与既有手写退避一致，
/// 仅对网络异常与瞬态状态码（408/429/5xx）重试，4xx 重发无意义不重试；签名对整体 payload
/// 不随重试变化，重发合法。终态写回 SendRecord（成功/最终失败 + 尝试次数 + 响应摘要）。
/// UoW：终态写回走 <see cref="SaveResultAsync"/> 的独立新 UoW（requiresNew）——终态必须落库，
/// 不与作业里其他潜在写操作共生死；除此之外本作业不开环境 UoW。
/// 日志语义（与下方日志字符串一一对应）：
/// - 还会重试的失败 = Warning「Webhook delivery attempt {Attempt} failed for {Uri} ({Reason}), retrying.」
///   （框架 OnRetry 回调；异常触发 {Reason}=异常消息，状态码触发 {Reason}=状态码）；
/// - 最后一次失败 = Error，两种终态形态——非 2xx 终态「Webhook delivery failed after {Attempt} attempts
///   for {Uri}: {StatusCode}.」（无异常、状态码即原因），网络层终态「Webhook delivery failed after
///   {Attempt} attempts for {Uri}.」+ 异常堆栈（终态按"无响应"落库）；
/// - 订阅已删/暂停 = 不打日志，静默落失败终态。
/// </summary>
public class WebhookDeliveryJob : AsyncBackgroundJob<WebhookDeliveryJobArgs>, ITransientDependency
{
    private static readonly TimeSpan HttpTimeout = TimeSpan.FromSeconds(AbpAdminWebhooksConsts.DeliveryTimeoutSeconds);

    /// <summary>与既有手写重试一致的退避序列；RetryIntervals 一经设置，重试次数即数组长度。</summary>
    private static readonly TimeSpan[] RetryBackoffs = [TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(4)];

    /// <summary>值得重试的瞬态状态码：请求超时/限流/服务器与网关侧错误。</summary>
    private static readonly HttpStatusCode[] RetryableStatusCodes =
    [
        HttpStatusCode.RequestTimeout,
        HttpStatusCode.TooManyRequests,
        HttpStatusCode.InternalServerError,
        HttpStatusCode.BadGateway,
        HttpStatusCode.ServiceUnavailable,
        HttpStatusCode.GatewayTimeout,
    ];

    private readonly IServiceProvider _serviceProvider;
    private readonly IHttpRemoteService _httpRemoteService;
    private readonly IUnitOfWorkManager _unitOfWorkManager;
    private readonly IClock _clock;

    public ILogger<WebhookDeliveryJob> Logger { get; set; } = NullLogger<WebhookDeliveryJob>.Instance;

    public WebhookDeliveryJob(
        IServiceProvider serviceProvider,
        IHttpRemoteService httpRemoteService,
        IUnitOfWorkManager unitOfWorkManager,
        IClock clock)
    {
        _serviceProvider = serviceProvider;
        _httpRemoteService = httpRemoteService;
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

            // OnRetry 在每次重试前触发（ctx.Attempt 从 1 计）：落 Warning 日志并跟踪尝试次数。
            // attempts 初值 1（首发）；耗尽后网络异常路径 attempts 恰为 RetryBackoffs.Length + 1。
            var attempts = 1;
            HttpResponseMessage? response = null;
            bool succeeded;
            int? statusCode;
            string responseBody;
            try
            {
                var sent = await _httpRemoteService.SendAsync(BuildDeliveryRequest(
                    subscription.WebhookUri, args, timestamp, signature,
                    ctx =>
                    {
                        attempts = ctx.Attempt + 1;
                        Logger.LogWarning(ctx.Exception,
                            "Webhook delivery attempt {Attempt} failed for {Uri} ({Reason}), retrying.",
                            ctx.Attempt, subscription.WebhookUri,
                            ctx.IsExceptionRetry ? ctx.Exception?.Message : $"{(int?)ctx.StatusCode} {ctx.StatusCode}");
                    }));
                if (sent is null)
                {
                    // HttpAgent 的 SendAsync 返回可空响应：null 视作网络层失败，走统一失败终态
                    throw new InvalidOperationException("HttpRemoteService returned no response.");
                }
                response = sent;

                statusCode = (int)response.StatusCode;
                succeeded = statusCode is >= 200 and < 300;
                responseBody = await response.Content.ReadAsStringAsync();
                if (!succeeded)
                {
                    Logger.LogError("Webhook delivery failed after {Attempt} attempts for {Uri}: {StatusCode}.",
                        attempts, subscription.WebhookUri, statusCode);
                }
            }
            catch (Exception ex)
            {
                // 重试耗尽仍是网络层失败：不让异常逃出作业，按"无响应"落失败终态
                Logger.LogError(ex, "Webhook delivery failed after {Attempt} attempts for {Uri}.",
                    attempts, subscription.WebhookUri);
                succeeded = false;
                statusCode = null;
                responseBody = "no response (network error)";
            }
            finally
            {
                response?.Dispose();
            }

            await SaveResultAsync(recordRepository, args, succeeded, statusCode, responseBody, attempts);
        }
    }

    /// <summary>
    /// 投递请求。正文必须原样发送（签名覆盖 payload 原文），所以用 SetContent(text, contentType)
    /// 而非 SetJsonContent/SetRawStringContent——后者会把字符串再包一层引号，破坏签名与收端解析。
    /// </summary>
    private static HttpRequestBuilder BuildDeliveryRequest(
        string uri, WebhookDeliveryJobArgs args, string timestamp, string signature,
        Action<HttpRetryContext> onRetry)
    {
        return HttpRequestBuilder.Post(uri)
            .SetHttpClientName(WebhookDeliveryHttpClientExtensions.ClientName)
            .SetTimeout(HttpTimeout)
            .SetContent(args.Payload, "application/json")
            .WithHeader("X-AbpAdmin-Event", args.EventName)
            .WithHeader("X-AbpAdmin-Timestamp", timestamp)
            .WithHeader("X-AbpAdmin-Signature", signature)
            .SetRetry(options => options
                .SetRetryIntervals(RetryBackoffs)
                .AddRetryStatusCodes(RetryableStatusCodes)
                .SetOnRetry(onRetry));
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

/// <summary>命名 HTTP 客户端（投递专用；超时经 HttpRequestBuilder.SetTimeout 按次设置）。</summary>
public static class WebhookDeliveryHttpClientExtensions
{
    public const string ClientName = "AbpAdminWebhookDelivery";

    public static IServiceCollection AddWebhookDeliveryHttpClient(this IServiceCollection services)
    {
        _ = services.AddHttpClient(ClientName);
        return services;
    }
}
