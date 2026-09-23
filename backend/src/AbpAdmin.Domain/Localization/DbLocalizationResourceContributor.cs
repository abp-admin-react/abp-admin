using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;
using Volo.Abp.Localization;
using Volo.Abp.MultiTenancy;

namespace AbpAdmin.Localization;

/// <summary>
/// 数据库本地化资源贡献者。从数据库读取本地化文本覆盖。
/// 租户级覆盖优先于 host 级覆盖，host 级覆盖优先于虚拟文件 json 里的静态文本。
/// </summary>
public class DbLocalizationResourceContributor : ILocalizationResourceContributor
{
    private readonly DbExternalLocalizationStore _store;
    private readonly ICurrentTenant _currentTenant;
    private string _resourceName = string.Empty;

    public bool IsDynamic => true;

    /// <param name="rootServiceProvider">
    /// 应用根容器（IAbpApplication.ServiceProvider）。
    /// 注意：本对象是进程级常驻对象（挂在 LocalizationResource.Contributors 上直到进程结束），
    /// 按 00-overview 6.5 节《脱离请求路径的回调：依赖一律从本次触发的 scope 解析》，
    /// 不能持有 OnApplicationInitialization 阶段注入的依赖——ABP 的 InitializeModules
    /// 用 using CreateScope() 跑模块初始化，那个 scope 初始化结束即销毁，
    /// 从那里解析的仓储/连接串解析器随之变成死引用（租户上下文解析连接串时
    /// 抛 ObjectDisposedException）。所以这里改为从根容器解析一次 store 持有：
    /// 根容器常驻，store 及其仓储链上的 UnitOfWorkDbContextProvider/连接串解析器
    /// 捕获的都是根容器，GetDbContextAsync 按调用时的环境 UoW 取连接，无跨请求状态。
    /// </param>
    public DbLocalizationResourceContributor(
        IServiceProvider rootServiceProvider,
        ICurrentTenant currentTenant)
    {
        _store = rootServiceProvider.GetRequiredService<DbExternalLocalizationStore>();
        _currentTenant = currentTenant;
    }

    public void Initialize(LocalizationResourceInitializationContext context)
    {
        _resourceName = context.Resource.ResourceName;
    }

    public LocalizedString? GetOrNull(string cultureName, string name)
    {
        var dbTexts = _store.GetTextsAsync(_currentTenant.Id, _resourceName, cultureName).GetAwaiter().GetResult();
        if (dbTexts.TryGetValue(name, out var value))
        {
            return new LocalizedString(name, value);
        }
        return null;
    }

    public void Fill(string cultureName, Dictionary<string, LocalizedString> dictionary)
    {
        var dbTexts = _store.GetTextsAsync(_currentTenant.Id, _resourceName, cultureName).GetAwaiter().GetResult();
        foreach (var kvp in dbTexts)
        {
            dictionary[kvp.Key] = new LocalizedString(kvp.Key, kvp.Value);
        }
    }

    public async Task FillAsync(string cultureName, Dictionary<string, LocalizedString> dictionary)
    {
        var dbTexts = await _store.GetTextsAsync(_currentTenant.Id, _resourceName, cultureName);
        foreach (var kvp in dbTexts)
        {
            dictionary[kvp.Key] = new LocalizedString(kvp.Key, kvp.Value);
        }
    }

    public async Task<IEnumerable<string>> GetSupportedCulturesAsync()
    {
        // 返回所有已启用语言的文化名称
        // 这里简化处理，返回空列表表示支持所有文化
        return await Task.FromResult(new List<string>());
    }
}
