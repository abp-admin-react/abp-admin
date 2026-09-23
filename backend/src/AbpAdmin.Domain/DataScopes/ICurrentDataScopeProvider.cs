using System.Threading.Tasks;

namespace AbpAdmin.DataScopes;

/// <summary>
/// 当前用户数据范围解析器。返回不可变快照，中间件拿到后直接灌进环境状态。
/// </summary>
public interface ICurrentDataScopeProvider
{
    /// <summary>
    /// 解析当前用户的数据范围，返回不可变快照。
    /// 结果会缓存 5 分钟（绝对过期），缓存键含 generation 版本戳。
    /// </summary>
    Task<DataScopeSnapshot> GetAsync();
}
