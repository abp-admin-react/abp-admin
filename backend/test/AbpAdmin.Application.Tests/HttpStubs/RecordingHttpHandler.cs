using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace AbpAdmin.HttpStubs;

/// <summary>
/// HttpAgent 声明式客户端的录制型 HttpMessageHandler 替身（挂命名 HttpClient 主处理器位，
/// 测试模块晚于生产模块注册生效），不真实外呼。测试入队 (状态码, 响应体)，
/// 用 RecordedRequests 断言请求形状（方法/URL/表单/Authorization 头）——
/// 这是全仓第一例出站 HTTP 层面的假件（此前惯例是替换更外层的请求器接口，
/// HttpAgent 绑定命名 HttpClient 后主处理器成为新的可替换点）。
/// </summary>
public abstract class RecordingHttpHandler : HttpMessageHandler
{
    public sealed record RecordedRequest(
        HttpMethod Method,
        Uri RequestUri,
        IReadOnlyDictionary<string, string> Form,
        string? Authorization,
        IReadOnlyDictionary<string, string> Headers);

    private readonly ConcurrentQueue<(HttpStatusCode Status, string Body, string ContentType)> _responses = new();
    private readonly ConcurrentQueue<Func<Exception>> _exceptionFactories = new();
    private readonly ConcurrentQueue<RecordedRequest> _requests = new();

    public ConcurrentQueue<RecordedRequest> RecordedRequests => _requests;

    /// <summary>入队 canned 响应；非 JSON 场景显式传 contentType（如 "text/html"）保持替身保真。</summary>
    public void Enqueue(HttpStatusCode status, string body, string contentType = "application/json")
        => _responses.Enqueue((status, body, contentType));

    /// <summary>入队网络层异常（超时/连接拒绝等），锚定"异常不抑制、原样穿透"的契约。</summary>
    public void EnqueueException(Func<Exception> exceptionFactory) => _exceptionFactories.Enqueue(exceptionFactory);

    public void Reset()
    {
        _responses.Clear();
        _exceptionFactories.Clear();
        _requests.Clear();
    }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        _requests.Enqueue(new RecordedRequest(
            request.Method,
            request.RequestUri!,
            await ReadFormAsync(request, cancellationToken),
            request.Headers.Authorization?.ToString(),
            request.Headers.ToDictionary(h => h.Key, h => string.Join(", ", h.Value))));

        if (_exceptionFactories.TryDequeue(out var exceptionFactory))
        {
            throw exceptionFactory();
        }

        if (!_responses.TryDequeue(out var canned))
        {
            throw new InvalidOperationException(
                $"{GetType().Name} 没有入队的 canned 响应——测试先 Enqueue 再触发请求。");
        }

        return new HttpResponseMessage(canned.Status)
        {
            Content = new StringContent(canned.Body, System.Text.Encoding.UTF8, canned.ContentType)
        };
    }

    private static async Task<Dictionary<string, string>> ReadFormAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var form = new Dictionary<string, string>();
        if (request.Content == null)
        {
            return form;
        }

        var body = await request.Content.ReadAsStringAsync(cancellationToken);
        // FormUrlEncodedContent 输出 a=1&b=2（空格编码为 +，值内字面 + 为 %2B），
        // 键值两侧都先把 + 还原为空格再反转义，即标准 form-urlencoding 解码
        foreach (var pair in body.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var idx = pair.IndexOf('=');
            if (idx <= 0)
            {
                continue;
            }

            form[Uri.UnescapeDataString(pair[..idx].Replace('+', ' '))] =
                Uri.UnescapeDataString(pair[(idx + 1)..].Replace('+', ' '));
        }

        return form;
    }
}

/// <summary>"AuthServerTokenExchange" 命名 HttpClient 的替身（/connect/token 交换）。</summary>
public sealed class RecordingConnectTokenHandler : RecordingHttpHandler
{
}

/// <summary>"Turnstile" 命名 HttpClient 的替身（siteverify 校验）。</summary>
public sealed class RecordingTurnstileHandler : RecordingHttpHandler
{
}
