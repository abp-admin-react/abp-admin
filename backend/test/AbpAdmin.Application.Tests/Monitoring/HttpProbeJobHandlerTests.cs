using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using AbpAdmin.HttpStubs;
using AbpAdmin.ScheduledJobs;
using Shouldly;
using Volo.Abp;
using Volo.Abp.EventBus;
using Volo.Abp.EventBus.Local;
using Volo.Abp.Modularity;
using Xunit;

namespace AbpAdmin.Monitoring;

/* HTTP 探活定时作业（HttpAgent 命名客户端 AbpAdminHttpProbe）的回归锚。
 * 覆盖：2xx 放行、ExpectedStatusCode 精确匹配/不匹配、非 2xx 失败先发 HttpProbeFailedEto 再抛出、
 * 网络层异常失败（StatusCode=null）同样"发事件+抛出"、Payload 缺失/非法 JSON/非法 URL
 * 快速失败且不发任何请求。出站请求由 RecordingHttpProbeHandler 截停（不真实外呼）。
 * 事件用字段承接（不能用"返回闭包局部变量"——拿到的是 return 那一刻的 null 值拷贝）。
 */
public abstract class HttpProbeJobHandlerTests<TStartupModule> : AbpAdminApplicationTestBase<TStartupModule>
    where TStartupModule : IAbpModule
{
    private readonly RecordingHttpProbeHandler _handler;
    private readonly HttpProbeJobHandler _jobHandler;
    private readonly ILocalEventBus _localEventBus;

    /// <summary>最近一次探活失败事件（SubscribeOnce 后由订阅回调写入）。</summary>
    private HttpProbeFailedEto? _received;

    protected HttpProbeJobHandlerTests()
    {
        _handler = GetRequiredService<RecordingHttpProbeHandler>();
        // [ExposeServices(typeof(IScheduledJobHandler))] 使容器只暴露接口不暴露具体类，
        // 从 handler 集合里筛出被测实例（与调度器 GetServices 的解析方式同源）
        _jobHandler = GetRequiredService<IEnumerable<IScheduledJobHandler>>()
            .OfType<HttpProbeJobHandler>().ShouldHaveSingleItem();
        _localEventBus = GetRequiredService<ILocalEventBus>();
        _handler.Reset();
    }

    private static ScheduledJobContext Context(string? payload) => new()
    {
        JobType = ScheduledJobConsts.HttpProbeJobType,
        Payload = payload,
        CancellationToken = CancellationToken.None,
    };

    /// <summary>订阅失败事件（写入 <see cref="_received"/>；各测试独立重置，旧订阅残留无副作用）。</summary>
    private void SubscribeOnce()
    {
        _received = null;
        _localEventBus.Subscribe<HttpProbeFailedEto>(eto =>
        {
            _received = eto;
            return Task.CompletedTask;
        });
    }

    [Fact]
    public async Task Success_2xx_passes_without_failure_event()
    {
        _handler.Enqueue(HttpStatusCode.OK, "");
        SubscribeOnce();

        await _jobHandler.ExecuteAsync(Context("""{"Url":"http://probe.test/health"}"""));

        var request = _handler.RecordedRequests.ShouldHaveSingleItem();
        request.Method.ShouldBe(HttpMethod.Get);
        request.RequestUri.ToString().ShouldBe("http://probe.test/health");
        _received.ShouldBeNull();
    }

    [Fact]
    public async Task ExpectedStatusCode_exact_match_passes()
    {
        _handler.Enqueue(HttpStatusCode.Created, "");
        SubscribeOnce();

        await _jobHandler.ExecuteAsync(Context(
            """{"Url":"http://probe.test/created","ExpectedStatusCode":201}"""));

        _received.ShouldBeNull();
    }

    [Fact]
    public async Task Non_2xx_fails_publishes_event_then_throws()
    {
        _handler.Enqueue(HttpStatusCode.ServiceUnavailable, "boom");
        SubscribeOnce();

        var exception = await Should.ThrowAsync<AbpException>(() =>
            _jobHandler.ExecuteAsync(Context("""{"Url":"http://probe.test/health"}""")));

        exception.Message.ShouldContain("探活失败");
        exception.Message.ShouldContain("503");
        _received.ShouldNotBeNull();
        _received.Url.ShouldBe("http://probe.test/health");
        _received.StatusCode.ShouldBe(503);
        _received.ErrorMessage.ShouldNotBeNull();
        _received.ErrorMessage.ShouldContain("unexpected status code");
    }

    [Fact]
    public async Task ExpectedStatusCode_mismatch_fails_even_when_2xx()
    {
        _handler.Enqueue(HttpStatusCode.OK, "");
        SubscribeOnce();

        var exception = await Should.ThrowAsync<AbpException>(() =>
            _jobHandler.ExecuteAsync(Context(
                """{"Url":"http://probe.test/health","ExpectedStatusCode":204}""")));

        exception.Message.ShouldContain("探活失败");
        _received.ShouldNotBeNull();
        _received.StatusCode.ShouldBe(200);
    }

    [Fact]
    public async Task Network_error_fails_with_null_status_and_publishes_event()
    {
        _handler.EnqueueException(() => new HttpRequestException("connection refused"));
        SubscribeOnce();

        var exception = await Should.ThrowAsync<AbpException>(() =>
            _jobHandler.ExecuteAsync(Context("""{"Url":"http://probe.test/health"}""")));

        exception.Message.ShouldContain("无响应");
        _received.ShouldNotBeNull();
        _received.StatusCode.ShouldBeNull();
        _received.ErrorMessage.ShouldNotBeNull();
        _received.ErrorMessage.ShouldContain("connection refused");
    }

    [Fact]
    public async Task Missing_payload_fails_fast_without_request()
    {
        SubscribeOnce();

        var exception = await Should.ThrowAsync<AbpException>(() =>
            _jobHandler.ExecuteAsync(Context(null)));

        exception.Message.ShouldContain("示例");
        _handler.RecordedRequests.ShouldBeEmpty();
        _received.ShouldBeNull();
    }

    [Fact]
    public async Task Invalid_json_payload_fails_fast_without_request()
    {
        SubscribeOnce();

        var exception = await Should.ThrowAsync<AbpException>(() =>
            _jobHandler.ExecuteAsync(Context("not-json")));

        exception.Message.ShouldContain("合法 JSON");
        _handler.RecordedRequests.ShouldBeEmpty();
        _received.ShouldBeNull();
    }

    [Fact]
    public async Task Non_http_url_fails_fast_without_request()
    {
        SubscribeOnce();

        var exception = await Should.ThrowAsync<AbpException>(() =>
            _jobHandler.ExecuteAsync(Context("""{"Url":"ftp://probe.test/file"}""")));

        exception.Message.ShouldContain("http(s)");
        _handler.RecordedRequests.ShouldBeEmpty();
        _received.ShouldBeNull();
    }
}
