namespace AbpAdmin.DataScopes;

/// <summary>
/// generation 计数的缓存条目。<see cref="IDataScopeCacheGeneration"/> 只需要一个 long，
/// 但 ABP 的 <c>IDistributedCache&lt;T&gt;</c> 要求 T 为引用类型，故包一层。
/// 形状由序列化器决定：公开无参构造 + 可写属性。
/// </summary>
public class DataScopeGenerationCacheItem
{
    public long Generation { get; set; }
}
