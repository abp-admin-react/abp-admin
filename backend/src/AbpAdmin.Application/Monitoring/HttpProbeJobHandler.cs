using System;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using AbpAdmin.ScheduledJobs;
using HttpAgent;
using Microsoft.Extensions.Logging;
using Volo.Abp;
using Volo.Abp.DependencyInjection;
using Volo.Abp.EventBus.Local;
using Volo.Abp.Timing;

namespace AbpAdmin.Monitoring;

/// <summary>
/// HTTP 探活定时作业（JobType = <see cref="ScheduledJobConsts.HttpProbeJobType"/>，Host/租户均可配置）。
/// 对 Payload 指定的 Url 发一次 GET（可选超时与期望状态码），成败交给调度器落执行记录；
/// 失败时先同步发布 <see cref="HttpProbeFailedEto"/>（onUnitOfWorkComplete:false 立即分发，
/// 不随本方法抛出后的外层 UoW 回滚而丢弃；Webhooks 桥接器自己开独立 UoW 落 SendRecord），
/// 再抛出让本次执行记为失败。
/// 日志语义（与下方日志字符串一一对应）：
/// - 成功 = Information「HTTP probe succeeded for {Url} ({StatusCode})」；
/// - 失败 = Warning「HTTP probe failed for {Url}: {Error}」+ 抛 AbpException
///   「HTTP 探活失败：{Url} → {StatusCode}：{Error}」（无响应时为「（无响应）」），
///   异常消息由调度器截断后落 ScheduledJobExecution.Message 与作业行 LastRunMessage；
/// - 停机/令牌取消：不打失败日志、不发事件（交回调度器取消分支，不污染执行历史）。
/// 刻意不做库内自动重试：探活要暴露的是持续性故障，重试会把"偶发抖动"和"真挂了"混在一起。
/// Payload 示例：{"Url":"https://example.com/health","TimeoutSeconds":10,"ExpectedStatusCode":200}
/// </summary>
[ExposeServices(typeof(IScheduledJobHandler))]
public class HttpProbeJobHandler : IScheduledJobHandler, ITransientDependency
{
    public const string PayloadSample = """{"Url":"https://example.com/health","TimeoutSeconds":10,"ExpectedStatusCode":200}""";

    /// <summary>
    /// 探活专用命名 HttpClient（注册在 AbpAdminApplicationModule，注册跟随消费方）：
    /// 目标 URL 来自作业 Payload 无 BaseAddress 可配，命名主要为了让测试能挂录制替身、
    /// 让 Profiler 等全局管道有客户端身份可辨。
    /// </summary>
    public const string HttpClientName = "AbpAdminHttpProbe";

    private const int DefaultTimeoutSeconds = 10;
    private const int MaxTimeoutSeconds = 60;

    /// <summary>与 WebhookSendRecord 响应摘要上限同量级：事件体进 webhook payload，不无限带异常全文。</summary>
    private const int MaxErrorMessageLength = 2000;

    public string JobType => ScheduledJobConsts.HttpProbeJobType;

    public string DisplayNameKey => "ScheduledJobType:HttpProbe";

    private readonly IHttpRemoteService _httpRemoteService;
    private readonly ILocalEventBus _localEventBus;
    private readonly IClock _clock;
    private readonly ILogger<HttpProbeJobHandler> _logger;

    public HttpProbeJobHandler(
        IHttpRemoteService httpRemoteService,
        ILocalEventBus localEventBus,
        IClock clock,
        ILogger<HttpProbeJobHandler> logger)
    {
        _httpRemoteService = httpRemoteService;
        _localEventBus = localEventBus;
        _clock = clock;
        _logger = logger;
    }

    public virtual async Task ExecuteAsync(ScheduledJobContext context)
    {
        var probe = ParsePayload(context.Payload);

        int? statusCode = null;
        string? error = null;
        try
        {
            using var response = await _httpRemoteService.SendAsync(
                HttpRequestBuilder.Get(probe.Url)
                    .SetHttpClientName(HttpClientName)
                    .SetTimeout(TimeSpan.FromSeconds(probe.TimeoutSeconds)),
                context.CancellationToken);
            if (response is null)
            {
                // HttpAgent 的 SendAsync 返回可空响应：null 视作网络层失败，走统一失败路径
                throw new InvalidOperationException("HttpRemoteService returned no response.");
            }

            statusCode = (int)response.StatusCode;

            if (IsSuccessful(probe, statusCode.Value))
            {
                _logger.LogInformation("HTTP probe succeeded for {Url} ({StatusCode}).", probe.Url, statusCode);
                return;
            }

            error = $"unexpected status code {(int)response.StatusCode} {response.ReasonPhrase}";
        }
        catch (OperationCanceledException) when (context.CancellationToken.IsCancellationRequested)
        {
            // 停机/令牌中止不是探活失败：不发布事件、不落失败记录，交回调度器的取消分支
            throw;
        }
        catch (Exception ex)
        {
            error = ex.Message.Truncate(MaxErrorMessageLength);
        }

        _logger.LogWarning("HTTP probe failed for {Url}: {Error}", probe.Url, error);
        await PublishFailureAsync(probe.Url, statusCode, error);

        throw new AbpException(
            $"HTTP 探活失败：{probe.Url}{(statusCode.HasValue ? $" → {statusCode}" : "（无响应）")}：{error}");
    }

    /// <summary>期望状态码显式指定则精确匹配；否则按 2xx 判定成功。</summary>
    private static bool IsSuccessful(ProbePayload probe, int statusCode) =>
        probe.ExpectedStatusCode.HasValue
            ? statusCode == probe.ExpectedStatusCode
            : statusCode is >= 200 and < 300;

    private async Task PublishFailureAsync(string url, int? statusCode, string? error)
    {
        // onUnitOfWorkComplete:false = 不缓冲进环境 UoW、立即分发。必须如此：本方法返回后
        // 外层调用方随即抛异常、调度器那个事务性 UoW 必然回滚，缓冲的事件会被一起丢弃
        // （失败通知静默丢失）。持久化由消费方负责——Webhooks 桥接器自己开 requiresNew
        // UoW 落 SendRecord（见 HttpProbeFailedWebhookEventHandler），不被外层回滚波及。
        await _localEventBus.PublishAsync(new HttpProbeFailedEto
        {
            Url = url,
            StatusCode = statusCode,
            ErrorMessage = error,
            OccurredAt = _clock.Now,
        }, onUnitOfWorkComplete: false);
    }

    private static ProbePayload ParsePayload(string? payload)
    {
        if (string.IsNullOrWhiteSpace(payload))
        {
            throw new AbpException($"缺少探活配置（Payload）。示例：{PayloadSample}");
        }

        ProbePayload probe;
        try
        {
            probe = JsonSerializer.Deserialize<ProbePayload>(payload, ProbeJsonOptions) ?? new ProbePayload();
        }
        catch (JsonException ex)
        {
            throw new AbpException($"探活配置（Payload）不是合法 JSON：{ex.Message}。示例：{PayloadSample}");
        }

        if (string.IsNullOrWhiteSpace(probe.Url)
            || !Uri.TryCreate(probe.Url, UriKind.Absolute, out var uri)
            || uri.Scheme is not ("http" or "https"))
        {
            throw new AbpException($"探活配置的 Url 缺失或不是合法的 http(s) 绝对地址。示例：{PayloadSample}");
        }

        if (probe.ExpectedStatusCode is < 100 or > 599)
        {
            throw new AbpException($"探活配置的 ExpectedStatusCode 必须在 100~599 之间，当前为 {probe.ExpectedStatusCode}。");
        }

        return probe;
    }

    /// <summary>Web 大小写不敏感；Url/ExpectedStatusCode 惰性计算，TimeoutSeconds 取值时钳制。</summary>
    private static readonly JsonSerializerOptions ProbeJsonOptions = new(JsonSerializerDefaults.Web);

    private sealed class ProbePayload
    {
        public string Url { get; set; } = default!;

        /// <summary>反序列化目标字段；取值时钳制到 [1,60]，缺省 10。暴露为 int 供 SetSeconds 直接消费。</summary>
        public int? TimeoutSecondsRaw { get; set; }

        public int TimeoutSeconds => TimeoutSecondsRaw is > 0
            ? Math.Min(TimeoutSecondsRaw.Value, MaxTimeoutSeconds)
            : DefaultTimeoutSeconds;

        public int? ExpectedStatusCode { get; set; }
    }
}
