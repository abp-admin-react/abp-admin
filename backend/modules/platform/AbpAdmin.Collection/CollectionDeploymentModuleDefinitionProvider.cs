using System.Collections.Generic;
using AbpAdmin.Modules;
using Volo.Abp.DependencyInjection;

namespace AbpAdmin.Collection;

[ExposeServices(typeof(IDeploymentModuleDefinitionProvider))]
public class CollectionDeploymentModuleDefinitionProvider : IDeploymentModuleDefinitionProvider, ITransientDependency
{
    public IReadOnlyList<DeploymentModuleDefinition> GetDefinitions() =>
    [
        new(DeploymentModuleIds.Collection, "Payments",
            configurationPath: "/operations/payments?settings=channels", historyPath: "/operations/payments"),
        new(DeploymentModuleIds.Chain, "On-chain deposits and withdrawals",
            configurationPath: "/web3/operations?settings=network", historyPath: "/web3/operations")
    ];
}
