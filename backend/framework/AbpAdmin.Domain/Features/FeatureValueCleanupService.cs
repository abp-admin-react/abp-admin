using System.Threading.Tasks;
using Volo.Abp.DependencyInjection;
using Volo.Abp.FeatureManagement;
using Volo.Abp.Features;

namespace AbpAdmin.Features;

/// <summary>
/// 清空某 (providerName, providerKey) 名下的全部功能值——版本删除（"E"+版本 Id）与
/// 租户删除（"T"+租户 Id）两个清理处理器共用的唯一实现。
///
/// 关键不变量（历史上是两份拷贝，现收敛于此）：必须经 IFeatureManagementStore.DeleteAsync
/// 逐条删除，而不是仓储直接删行——Store 在删行的同时按 (name, providerName, providerKey)
/// 失效 FeatureValueCacheItem 缓存（considerUow 推迟到提交后），仓储直删会留下脏缓存，
/// 功能解析继续命中已删除的值。功能值数量与功能定义同阶（个位数），逐条删除可接受。
/// </summary>
public class FeatureValueCleanupService : ITransientDependency
{
    private readonly IFeatureValueRepository _featureValueRepository;
    private readonly IFeatureManagementStore _featureManagementStore;

    public FeatureValueCleanupService(
        IFeatureValueRepository featureValueRepository,
        IFeatureManagementStore featureManagementStore)
    {
        _featureValueRepository = featureValueRepository;
        _featureManagementStore = featureManagementStore;
    }

    /// <summary>删除该 provider 名下全部功能值并逐项失效缓存。幂等：无值时无操作。</summary>
    public virtual async Task DeleteAllAsync(string providerName, string providerKey)
    {
        var featureValues = await _featureValueRepository.GetListAsync(providerName, providerKey);

        foreach (var featureValue in featureValues)
        {
            await _featureManagementStore.DeleteAsync(
                featureValue.Name,
                featureValue.ProviderName,
                featureValue.ProviderKey);
        }
    }
}
