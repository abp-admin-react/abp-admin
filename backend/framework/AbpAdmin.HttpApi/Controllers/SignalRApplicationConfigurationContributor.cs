using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Volo.Abp.AspNetCore.Mvc.ApplicationConfigurations;
using Volo.Abp.Data;

namespace AbpAdmin.Controllers;

/// <summary>
/// 通过 application-configuration 端点下发 SignalR 开关（T3.2），
/// 前端 getInitialState() 读到 signalr.enabled = false 时直接不尝试连接，
/// 省掉一轮注定失败的重试；isRealTimeAvailable() 同步置 false，
/// T3.5 的铃铛据此降级为轮询。靠"连不上就是没启用"区分不了网络问题，所以要显式下发。
/// </summary>
public class SignalRApplicationConfigurationContributor : IApplicationConfigurationContributor
{
    public Task ContributeAsync(ApplicationConfigurationContributorContext context)
    {
        var configuration = context.ServiceProvider.GetRequiredService<IConfiguration>();

        context.ApplicationConfiguration.SetProperty(
            "signalr",
            new
            {
                enabled = configuration.GetValue("SignalR:Enabled", true)
            });

        return Task.CompletedTask;
    }
}
