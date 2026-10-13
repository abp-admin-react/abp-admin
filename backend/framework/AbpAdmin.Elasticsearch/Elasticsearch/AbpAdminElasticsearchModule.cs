using System;
using Elastic.Clients.Elasticsearch;
using Elastic.Transport;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Volo.Abp.Modularity;

namespace AbpAdmin.Elasticsearch;

/// <summary>
/// Elasticsearch 接入模块(宿主级基础设施,由 AbpAdminHttpApiHostModule [DependsOn] 装配;
/// DbMigrator 不感知本模块)。
///
/// 职责:绑定配置 + 注册业务数据用的 ElasticsearchClient 单例(仅 <see cref="AbpAdminElasticsearchOptions.IsUsable"/>
/// 时生效,未启用对宿主零影响)。运行日志不走这里——由 Host 的 Program.cs 按
/// <see cref="AbpAdminElasticsearchOptions"/> 同一份配置装配 Serilog ES Sink(Serilog 引导
/// 必须早于 ABP 模块管线,无法放进模块)。
///
/// 注意:ES 不接入 ABP 的 UnitOfWork/仓储体系,ES 写入与 PG 事务是两套,
/// 业务侧建议经分布式事件总线异步写,失败靠事件重试补偿。
/// </summary>
public class AbpAdminElasticsearchModule : AbpModule
{
    /// <summary>
    /// 绑定配置并按开关注册客户端。禁用(或地址为空)时只注册 options,
    /// 不创建连接资源,健康检查也不注册(避免健康面板误报)。
    /// </summary>
    public override void ConfigureServices(ServiceConfigurationContext context)
    {
        var configuration = context.Services.GetConfiguration();
        var options = configuration.GetSection(AbpAdminElasticsearchOptions.SectionName)
                      .Get<AbpAdminElasticsearchOptions>() ?? new AbpAdminElasticsearchOptions();
        context.Services.AddSingleton(options);

        if (!options.IsUsable)
        {
            return;
        }

        // 官方客户端线程安全、自带连接池,注册单例(9.x 客户端未提供接口抽象,注册具体类;
        // 客户端无可释放资源——9.x 未实现 IDisposable,无需释放钩子)。
        // 安全提示:Url 建议 https;局域网 http + basic auth 属部署取舍(凭据明文 base64 传输),
        // 跨网段/生产外网必须换 https 或前置反代 TLS。
        context.Services.AddSingleton(_ => new ElasticsearchClient(new ElasticsearchClientSettings(new Uri(options.Url))
            .Authentication(new BasicAuthentication(options.Username, options.Password))));
    }
}
