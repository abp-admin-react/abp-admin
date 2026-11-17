using System.Collections.Generic;
using AbpAdmin.Modules;
using Volo.Abp.DependencyInjection;

namespace AbpAdmin.Ledger;

[ExposeServices(typeof(IDeploymentModuleDefinitionProvider))]
public class FinanceDeploymentModuleDefinitionProvider : IDeploymentModuleDefinitionProvider, ITransientDependency
{
    public IReadOnlyList<DeploymentModuleDefinition> GetDefinitions() =>
    [
        new(DeploymentModuleIds.Finance, "Finance workspace",
            configurationPath: "/finance/overview?settings=ledger", historyPath: "/finance/vouchers")
    ];
}
