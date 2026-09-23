using System;
using System.Threading.Tasks;
using AbpAdmin.Menus;
using AbpAdmin.Posts;
using AbpAdmin.ScheduledJobs;
using AbpAdmin.Tenants;
using Microsoft.Extensions.Caching.Distributed;
using Volo.Abp.Caching;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Identity;
using Volo.Abp.TenantManagement;

namespace AbpAdmin.OperationLogs;

// 内置解析函数：一个文件聚合全部实现（与 UserExportDtos.cs 同款组织方式）。
// 命名规范：camelCase，与模板 {{user(id)}} 的书写习惯一致；不要起与业务参数名冲突的名字。

/// <summary>用户 ID → 用户名。集合入参逐个解析（如通知发送的 userIds）。</summary>
public class UserOperationLogParseFunction : OperationLogParseFunctionBase, ITransientDependency
{
    private readonly IIdentityUserRepository _userRepository;

    public UserOperationLogParseFunction(
        IDistributedCache<OperationLogNameCacheItem> cache,
        IIdentityUserRepository userRepository)
        : base(cache)
    {
        _userRepository = userRepository;
    }

    public override string Name => "user";

    protected override async ValueTask<string?> FindNameAsync(Guid id)
        => (await _userRepository.FindAsync(id))?.UserName;
}

/// <summary>组织单元 ID → 显示名。</summary>
public class OrganizationUnitOperationLogParseFunction : OperationLogParseFunctionBase, ITransientDependency
{
    private readonly IOrganizationUnitRepository _organizationUnitRepository;

    public OrganizationUnitOperationLogParseFunction(
        IDistributedCache<OperationLogNameCacheItem> cache,
        IOrganizationUnitRepository organizationUnitRepository)
        : base(cache)
    {
        _organizationUnitRepository = organizationUnitRepository;
    }

    public override string Name => "ou";

    protected override async ValueTask<string?> FindNameAsync(Guid id)
        => (await _organizationUnitRepository.FindAsync(id))?.DisplayName;
}

/// <summary>岗位 ID → 岗位名。</summary>
public class PostOperationLogParseFunction : OperationLogParseFunctionBase, ITransientDependency
{
    private readonly IRepository<Post, Guid> _postRepository;

    public PostOperationLogParseFunction(
        IDistributedCache<OperationLogNameCacheItem> cache,
        IRepository<Post, Guid> postRepository)
        : base(cache)
    {
        _postRepository = postRepository;
    }

    public override string Name => "post";

    protected override async ValueTask<string?> FindNameAsync(Guid id)
        => (await _postRepository.FindAsync(id))?.Name;
}

/// <summary>租户 ID → 租户名。</summary>
public class TenantOperationLogParseFunction : OperationLogParseFunctionBase, ITransientDependency
{
    private readonly ITenantRepository _tenantRepository;

    public TenantOperationLogParseFunction(
        IDistributedCache<OperationLogNameCacheItem> cache,
        ITenantRepository tenantRepository)
        : base(cache)
    {
        _tenantRepository = tenantRepository;
    }

    public override string Name => "tenant";

    protected override async ValueTask<string?> FindNameAsync(Guid id)
        => (await _tenantRepository.FindAsync(id))?.Name;
}

/// <summary>租户套餐 ID → 套餐名。</summary>
public class TenantPackageOperationLogParseFunction : OperationLogParseFunctionBase, ITransientDependency
{
    private readonly IRepository<TenantPackage, Guid> _tenantPackageRepository;

    public TenantPackageOperationLogParseFunction(
        IDistributedCache<OperationLogNameCacheItem> cache,
        IRepository<TenantPackage, Guid> tenantPackageRepository)
        : base(cache)
    {
        _tenantPackageRepository = tenantPackageRepository;
    }

    public override string Name => "tenantPackage";

    protected override async ValueTask<string?> FindNameAsync(Guid id)
        => (await _tenantPackageRepository.FindAsync(id))?.Name;
}

/// <summary>菜单 ID → 菜单标题。</summary>
public class MenuOperationLogParseFunction : OperationLogParseFunctionBase, ITransientDependency
{
    private readonly IRepository<Menu, Guid> _menuRepository;

    public MenuOperationLogParseFunction(
        IDistributedCache<OperationLogNameCacheItem> cache,
        IRepository<Menu, Guid> menuRepository)
        : base(cache)
    {
        _menuRepository = menuRepository;
    }

    public override string Name => "menu";

    protected override async ValueTask<string?> FindNameAsync(Guid id)
        => (await _menuRepository.FindAsync(id))?.Title;
}

/// <summary>定时作业 ID → 作业名。</summary>
public class ScheduledJobOperationLogParseFunction : OperationLogParseFunctionBase, ITransientDependency
{
    private readonly IRepository<ScheduledJob, Guid> _scheduledJobRepository;

    public ScheduledJobOperationLogParseFunction(
        IDistributedCache<OperationLogNameCacheItem> cache,
        IRepository<ScheduledJob, Guid> scheduledJobRepository)
        : base(cache)
    {
        _scheduledJobRepository = scheduledJobRepository;
    }

    public override string Name => "scheduledJob";

    protected override async ValueTask<string?> FindNameAsync(Guid id)
        => (await _scheduledJobRepository.FindAsync(id))?.Name;
}
