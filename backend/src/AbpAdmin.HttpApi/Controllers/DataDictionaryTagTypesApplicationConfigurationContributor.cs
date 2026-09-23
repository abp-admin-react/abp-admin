using System.Threading.Tasks;
using AbpAdmin.DataDictionaries;
using Volo.Abp.AspNetCore.Mvc.ApplicationConfigurations;
using Volo.Abp.Data;

namespace AbpAdmin.Controllers;

/// <summary>
/// 通过 application-configuration 端点下发字典标签色白名单（T3.4）。
/// 白名单的唯一权威来源是 Domain.Shared 的 DataDictionaryTagTypes——
/// 此前前端 TAG_TYPE_OPTIONS 硬编码同 12 个值靠注释维系同步，后端加色/删色
/// 前端只能靠服务端 400 才发现；现在下拉选项从 application-configuration 取，双源收口。
/// </summary>
public class DataDictionaryTagTypesApplicationConfigurationContributor : IApplicationConfigurationContributor
{
    public Task ContributeAsync(ApplicationConfigurationContributorContext context)
    {
        context.ApplicationConfiguration.SetProperty("dataDictionaryTagTypes", DataDictionaryTagTypes.All);

        return Task.CompletedTask;
    }
}
