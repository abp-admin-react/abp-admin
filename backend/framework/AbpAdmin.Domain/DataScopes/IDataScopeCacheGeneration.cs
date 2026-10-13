using System.Threading.Tasks;

namespace AbpAdmin.DataScopes;

/// <summary>
/// 数据范围缓存的 generation 版本戳。组织树结构变更时递增，
/// 使所有用户的数据范围缓存同时失效（下一次请求重算）。
/// <para>本接口不带生命周期标记——标记打在实现类上。</para>
/// </summary>
public interface IDataScopeCacheGeneration
{
    /// <summary>
    /// 读取当前 generation 值。
    /// </summary>
    Task<long> GetAsync();

    /// <summary>
    /// 递增 generation 并返回新值。
    /// </summary>
    Task<long> IncrementAsync();
}
