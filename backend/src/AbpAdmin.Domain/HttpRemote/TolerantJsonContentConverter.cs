using System;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using HttpAgent;

namespace AbpAdmin.HttpRemote;

/// <summary>
/// 容错 JSON 响应内容转换器：响应体不是合法 JSON（网关错误页、纯文本 5xx 等）时返回 default 而非抛异常，
/// 让调用方按 HttpRemoteResult.IsSuccessStatusCode / StatusCode 兜底处理（如回落 "HTTP {status}"），
/// 与原手写 HttpClient 版"读字符串 + TryParse"的防御语义一致——HttpAgent 默认转换器在反序列化失败时
/// 直接抛 JsonException，会炸穿声明式调用且丢失状态码。声明式接口的 TResult DTO 需要此语义时注册。
/// </summary>
public class TolerantJsonContentConverter<TResult> : HttpContentConverterBase<TResult>
{
    // 同步版仅供库的同步桥（AsyncUtility.RunSync）调用，阻塞线程池线程——
    // 本项目声明式接口全部是 Task 返回（走异步版），不要新增同步声明式方法
    public override TResult? Read(HttpContentConverterContext context, CancellationToken cancellationToken)
    {
        try
        {
            return JsonSerializer.Deserialize<TResult>(context.ResponseMessage.Content.ReadAsStream(cancellationToken));
        }
        catch (Exception exception) when (exception is JsonException or NotSupportedException)
        {
            // NotSupportedException：ReadFromJson/反序列化对 Content-Type 声明的不支持字符集
            // （如 charset=x-user-defined）在读体前即抛出，同样属于"响应体不可用"而非调用方错误
            return default;
        }
    }

    public override async Task<TResult?> ReadAsync(
        HttpContentConverterContext context, CancellationToken cancellationToken)
    {
        try
        {
            return await context.ResponseMessage.Content.ReadFromJsonAsync<TResult>(cancellationToken);
        }
        catch (Exception exception) when (exception is JsonException or NotSupportedException)
        {
            // JsonException：非 JSON 体（网关错误页、纯文本 5xx）；NotSupportedException：不支持的字符集。
            // 两者都按"响应体不可用"返回 default，状态码仍在 HttpRemoteResult 上供调用方兜底
            return default;
        }
    }
}
