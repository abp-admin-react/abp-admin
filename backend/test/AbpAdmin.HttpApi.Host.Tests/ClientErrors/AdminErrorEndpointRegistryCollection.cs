using Xunit;

namespace AbpAdmin.ClientErrors;

/// <summary>
/// AdminErrorEndpointRegistry 测试集合。
/// 保证所有触摸 AdminErrorEndpointRegistry 的测试串行执行，避免 Clear() 互踩。
/// </summary>
[CollectionDefinition("AdminErrorEndpointRegistry")]
public class AdminErrorEndpointRegistryCollection
{
}
