using Volo.Abp.Application;
using Volo.Abp.Modularity;

namespace AbpAdmin.Collection.Contracts;

/// <summary>
/// 收款契约模块：只有类型定义，无服务注册。订单模块 DependsOn 本模块即可使用
/// 集成服务接口与 ETO；收款实现模块同样 DependsOn 本模块并提供实现。
/// </summary>
[DependsOn(typeof(AbpDddApplicationContractsModule))]
public class AbpAdminCollectionContractsModule : AbpModule
{
}
