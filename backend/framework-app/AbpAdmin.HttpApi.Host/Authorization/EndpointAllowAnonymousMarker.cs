using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.Filters;

namespace AbpAdmin.Authorization;

/// <summary>
/// 端点路由世界里的 [AllowAnonymous] 等价物：既能挂进 MVC 的 Filter 集合（Filter 集合会被
/// 复制进端点元数据），又实现 <see cref="IAllowAnonymous"/>（AuthorizationMiddleware 只认这个接口）。
/// 为什么不用框架的 <c>AllowAnonymousFilter</c>：.NET 10 里它只实现
/// <c>IAllowAnonymousFilter + IFilterMetadata</c>，不实现 <c>IAllowAnonymous</c>——
/// 在 FallbackPolicy（默认拒绝）下 AuthorizationMiddleware 对它视而不见，端点照样 401
/// （AnonymousEndpointSweepTests 反射核实过接口集）。Razor Pages 官方的
/// AddAllowAnonymousToPage 内部加的也是这个 filter，同病。
/// </summary>
public sealed class EndpointAllowAnonymousMarker : IAllowAnonymous, IFilterMetadata;
