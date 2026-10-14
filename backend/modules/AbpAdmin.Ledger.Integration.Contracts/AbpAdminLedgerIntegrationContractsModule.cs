using Volo.Abp.Application;
using Volo.Abp.Modularity;

namespace AbpAdmin.Ledger.Integration.Contracts;

/// <summary>
/// Ledger 集成契约：类型定义 only。实现模块 DependsOn 本模块并提供 ILedgerIntegrationService。
/// F0 仅落契约与错误码；过账引擎在 F1/F2。
/// </summary>
[DependsOn(typeof(AbpDddApplicationContractsModule))]
public class AbpAdminLedgerIntegrationContractsModule : AbpModule
{
}
