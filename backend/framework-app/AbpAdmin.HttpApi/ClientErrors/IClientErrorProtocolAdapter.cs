using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;

namespace AbpAdmin.ClientErrors;

/// <summary>
/// 客户端错误协议适配器接口（方案 5.3.3）。
/// 用于处理非 ABP 信封协议的错误响应（OAuth/Gateway/SSE 等）。
/// </summary>
/// <remarks>
/// <para>
/// 不同协议的错误响应形状：
/// </para>
/// <list type="bullet">
///   <item><description>OAuth：{ error, error_description } + WWW-Authenticate 头。</description></item>
///   <item><description>Gateway：GatewayError 固定形状。</description></item>
///   <item><description>SSE：错误帧（已开始流后由协议 handler 写出）。</description></item>
///   <item><description>SignalR：握手拒绝（由中间件处理）。</description></item>
/// </list>
/// <para>
/// 消费者：P1-HTTP 的协议感知 API 兜底中间件（根据 <see cref="IControlledProtocolRegistry"/> 选择适配器）。
/// </para>
/// <para>
/// 实现约束：
/// </para>
/// <list type="bullet">
///   <item><description>写出前检查 HasStarted（已开始时 no-op + 日志）。</description></item>
///   <item><description>保留协议必要语义（如 WWW-Authenticate、challenge/forbid）。</description></item>
///   <item><description>不得返回原始异常文本、堆栈、SQL、路径。</description></item>
///   <item><description>未开始响应时必须写出协议错误体（即使异常，也不返回错误页 HTML）。</description></item>
/// </list>
/// </remarks>
public interface IClientErrorProtocolAdapter
{
    /// <summary>
    /// 适配器支持的协议。
    /// </summary>
    ErrorProtocol Protocol { get; }

    /// <summary>
    /// 尝试按协议写出安全错误响应。
    /// </summary>
    /// <param name="httpContext">HTTP 上下文。</param>
    /// <param name="projection">投影结果（包含公开码、分类、文案、data 等）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>
    /// true 表示已处理（写出协议错误体或已开始流由 handler 处理）；
    /// false 表示未处理（应回退到默认 ABP 兜底）。
    /// </returns>
    Task<bool> TryWriteProtocolErrorAsync(
        HttpContext httpContext,
        ClientErrorHttpProjection projection,
        CancellationToken cancellationToken = default);
}
