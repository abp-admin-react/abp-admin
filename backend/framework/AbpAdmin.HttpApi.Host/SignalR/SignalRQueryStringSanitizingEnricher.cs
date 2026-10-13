using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.WebUtilities;
using Serilog.Core;
using Serilog.Events;

namespace AbpAdmin.SignalR;

/// <summary>
/// T3.2：对 /signalr-hubs 路径请求日志的 QueryString 属性脱敏（移除 access_token）。
///
/// 为什么中间件里剥离了 QueryString 还需要它：Microsoft.AspNetCore.Hosting 的
/// "Request starting" 日志在任何中间件执行之前就用原始 QueryString 记了，
/// 管道内的剥离对它无效（T3.2 e2e 实测确认）。Hosting 日志把 QueryString 作为
/// 结构化属性携带（属性名固定为 "QueryString"、"Path"），在这里按路径前缀定点替换，
/// 既堵住 token 落盘，又保留握手请求的其余可观测性（比整条丢弃强）。
/// </summary>
public class SignalRQueryStringSanitizingEnricher : ILogEventEnricher
{
    private const string HubPathPrefix = "/signalr-hubs";

    public void Enrich(LogEvent logEvent, ILogEventPropertyFactory propertyFactory)
    {
        if (!logEvent.Properties.TryGetValue("Path", out var pathValue) ||
            pathValue is not ScalarValue { Value: string path } ||
            !path.StartsWith(HubPathPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        if (!logEvent.Properties.TryGetValue("QueryString", out var queryValue) ||
            queryValue is not ScalarValue { Value: string queryString } ||
            !queryString.Contains("access_token", StringComparison.Ordinal))
        {
            return;
        }

        var query = QueryHelpers.ParseQuery(queryString);
        query.Remove("access_token");

        logEvent.AddOrUpdateProperty(propertyFactory.CreateProperty(
            "QueryString",
            QueryString.Create(
                    query.SelectMany(kv => kv.Value.Select(v => new KeyValuePair<string, string?>(kv.Key, v))))
                .Value));
    }
}
