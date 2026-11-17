using System.Collections.Generic;
using AbpAdmin.Modules;
using Volo.Abp.DependencyInjection;

namespace AbpAdmin.Ordering;

[ExposeServices(typeof(IDeploymentModuleDefinitionProvider))]
public class OrderingDeploymentModuleDefinitionProvider : IDeploymentModuleDefinitionProvider, ITransientDependency
{
    public IReadOnlyList<DeploymentModuleDefinition> GetDefinitions() =>
    [
        new(DeploymentModuleIds.Orders, "Orders", [DeploymentModuleIds.Collection],
            configurationPath: "/collection/orders?settings=rules", historyPath: "/collection/orders")
    ];
}
