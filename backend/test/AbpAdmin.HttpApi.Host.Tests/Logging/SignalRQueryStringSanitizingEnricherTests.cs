using System;
using System.Linq;
using AbpAdmin.SignalR;
using Serilog.Core;
using Serilog.Events;
using Serilog.Parsing;
using Shouldly;
using Xunit;

namespace AbpAdmin.Logging;

/// <summary>
/// T3.2 日志脱敏器：Hosting 层的 "Request starting" 在任何中间件之前就用原始
/// QueryString 记录，管道内的剥离盖不住它，必须在这里定点替换。
/// </summary>
public class SignalRQueryStringSanitizingEnricherTests
{
    private readonly SignalRQueryStringSanitizingEnricher _enricher = new();
    private readonly ILogEventPropertyFactory _propertyFactory = new ScalarOnlyPropertyFactory();

    private sealed class ScalarOnlyPropertyFactory : ILogEventPropertyFactory
    {
        public LogEventProperty CreateProperty(string name, object? value, bool destructureObjects = false)
        {
            return new LogEventProperty(name, new ScalarValue(value));
        }
    }

    private LogEvent CreateRequestLog(string path, string queryString)
    {
        return new LogEvent(
            DateTimeOffset.Now,
            LogEventLevel.Information,
            exception: null,
            messageTemplate: new MessageTemplate([]),
            properties:
            [
                new LogEventProperty("Path", new ScalarValue(path)),
                new LogEventProperty("QueryString", new ScalarValue(queryString))
            ]);
    }

    private static string QueryStringOf(LogEvent logEvent)
    {
        return ((logEvent.Properties["QueryString"] as ScalarValue)?.Value as string)!;
    }

    [Fact]
    public void Hub_Path_With_Access_Token_Should_Be_Sanitized()
    {
        var logEvent = CreateRequestLog(
            "/signalr-hubs/notification/negotiate",
            "?negotiateVersion=1&access_token=secret-token&id=abc");

        _enricher.Enrich(logEvent, _propertyFactory);

        var sanitized = QueryStringOf(logEvent);
        sanitized.ShouldNotContain("secret-token");
        sanitized.ShouldNotContain("access_token");
        // 其余参数必须保留：SignalR 的 id / negotiateVersion 对排查握手问题有用
        sanitized.ShouldContain("negotiateVersion=1");
        sanitized.ShouldContain("id=abc");
    }

    [Fact]
    public void Non_Hub_Path_Should_Be_Left_Alone()
    {
        var logEvent = CreateRequestLog("/api/app/foo", "?access_token=secret-token");

        _enricher.Enrich(logEvent, _propertyFactory);

        // 非 Hub 路径不处理：全 API 不允许 token 走 query string，
        // 真出现时应作为可疑流量留在日志里，而不是被悄悄抹掉
        QueryStringOf(logEvent).ShouldBe("?access_token=secret-token");
    }

    [Fact]
    public void Hub_Path_Without_Access_Token_Should_Be_Left_Alone()
    {
        var logEvent = CreateRequestLog("/signalr-hubs/notification", "?id=abc");

        _enricher.Enrich(logEvent, _propertyFactory);

        QueryStringOf(logEvent).ShouldBe("?id=abc");
    }

    [Fact]
    public void Hub_Path_Prefix_Match_Should_Be_Case_Insensitive()
    {
        var logEvent = CreateRequestLog("/SignalR-Hubs/notification", "?access_token=secret-token");

        _enricher.Enrich(logEvent, _propertyFactory);

        QueryStringOf(logEvent).ShouldNotContain("secret-token");
    }
}
