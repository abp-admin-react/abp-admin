using System.Linq;
using System.Threading.Tasks;
using AbpAdmin.OperationLogs;
using AbpAdmin.Permissions;
using Microsoft.AspNetCore.Authorization;
using Volo.Abp;
using Volo.Abp.Authorization;
using Volo.Abp.Tracing;

namespace AbpAdmin.Modules;

[Authorize(AbpAdminPermissions.ModuleManagement.Default)]
[RemoteService(false)]
public class ModuleManagementAppService(
    IDeploymentModuleStateReader reader, DeploymentModuleRegistry registry,
    DeploymentModuleManager manager, IOperationLogWriter operationLogWriter,
    ICorrelationIdProvider correlationIdProvider) : AbpAdminAppService, IModuleManagementAppService
{
    public virtual async Task<ModuleStateListDto> GetAsync()
    {
        AssertHost();
        return Map(await reader.GetAsync());
    }

    [Authorize(AbpAdminPermissions.ModuleManagement.Update)]
    public virtual async Task<ModuleChangePreviewDto> PreviewAsync(ModuleChangeInput input)
    {
        AssertHost();
        var snapshot = await reader.GetAsync();
        if (snapshot.Version != input.ExpectedVersion)
            throw new BusinessException(DeploymentModuleErrorCodes.VersionConflict);
        var plan = DeploymentModuleChanges.Plan(registry, snapshot, input.ModuleId, input.IsEnabled);
        return new ModuleChangePreviewDto
        {
            Version = plan.Version, CanApply = plan.CanApply,
            BlockingModuleIds = plan.BlockingModuleIds.ToList(),
            Changes = plan.Changes.Select(c => new ModuleStateChangeDto
                { ModuleId = c.ModuleId, WasEnabled = c.WasEnabled, IsEnabled = c.IsEnabled }).ToList()
        };
    }

    [Authorize(AbpAdminPermissions.ModuleManagement.Update)]
    public virtual async Task<ModuleStateListDto> UpdateAsync(ModuleChangeInput input)
    {
        AssertHost();
        var result = await manager.UpdateAsync(input.ModuleId, input.IsEnabled, input.ExpectedVersion);
        if (result.Plan.Changes.Count > 0)
        {
            // Manager committed its independent transaction; the existing writer has its own UoW and is fail-open.
            await operationLogWriter.WriteAsync(new OperationLogEntry
            {
                Type = "模块管理", SubType = "模块启停", BizId = input.ModuleId,
                Action = input.IsEnabled ? "启用模块" : "停用模块", Success = true,
                Extra = string.Join(", ", result.Plan.Changes.Select(c => $"{c.ModuleId}: {c.WasEnabled} -> {c.IsEnabled}")),
                RequestMethod = "PUT", RequestUrl = "/api/admin/module-management",
                CorrelationId = correlationIdProvider.Get()
            });
        }
        return Map(result.Snapshot);
    }

    private ModuleStateListDto Map(DeploymentModuleSnapshot snapshot) => new()
    {
        Version = snapshot.Version, Initialized = snapshot.Initialized,
        Modules = registry.Definitions.Values.Select(d => new ModuleStateDto
        {
            Id = d.Id, DisplayName = d.DisplayName, IsEnabled = snapshot.IsEnabled(d.Id),
            Dependencies = d.Dependencies.ToList(), ConfigurationPath = d.ConfigurationPath, HistoryPath = d.HistoryPath
        }).ToList()
    };

    private void AssertHost()
    {
        if (CurrentTenant.Id.HasValue) throw new AbpAuthorizationException();
    }
}
