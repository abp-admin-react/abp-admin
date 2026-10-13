using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AbpAdmin.Tenants;
using Microsoft.Extensions.Logging;
using Volo.Abp;
using Volo.Abp.Data;
using Volo.Abp.DependencyInjection;
using Volo.Abp.DistributedLocking;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Domain.Services;
using Volo.Abp.Guids;
using Volo.Abp.MultiTenancy;
using Volo.Abp.TenantManagement;
using Volo.Abp.Uow;

namespace AbpAdmin.Menus;

/// <summary>全局模板中的一个节点定义。Key/ParentKey 只用于模板内部组装，不落库。</summary>
public record MenuDefinition(
    string Key,
    string? ParentKey,
    MenuTypeEnum Type,
    string Title,
    string? Name,
    string? Path,
    string? Icon,
    int OrderNo,
    string? PermissionName = null,
    bool IsHide = false);

/// <summary>
/// 菜单域服务：Host 模板播种与租户懒拷贝。全局模板的节点定义（纯数据）抽在
/// <see cref="MenuTemplateDefinition"/>，与本域行为分离。模板镜像前端 config/routes.ts 的
/// 菜单层级（ABP 官方模板顺序：Saas → Identity → … → Settings），权限绑定与 src/access.ts 的
/// grantedPolicies 映射一致。Domain 层不引用 Application.Contracts，权限名用字面量
/// （与各模块 PermissionDefinitionProvider 注册名一致，稳定不变）。
/// </summary>
public class MenuManager : DomainService, ITransientDependency
{
    private readonly IRepository<Menu, Guid> _menuRepository;
    private readonly IRepository<MenuGrant, Guid> _menuGrantRepository;
    private readonly IRepository<TenantPackage, Guid> _tenantPackageRepository;
    private readonly ITenantRepository _tenantRepository;
    private readonly IDataFilter _dataFilter;
    private readonly Volo.Abp.Uow.IUnitOfWorkManager _unitOfWorkManager;
    private readonly IAbpDistributedLock _distributedLock;

    /// <summary>
    /// 「租户已播种」检查结果的进程内短缓存（按租户）：消灭 my-menu 热路径上每请求一次的
    /// CountAsync 计数查询。代价是「删光租户菜单后懒拷贝自动补种」最多延迟一个窗口才触发
    /// （与原行为只是时间差，不改变语义）。ResetTenantMenusAsync 硬删全部菜单后必须清除本缓存，
    /// 否则紧随其后的重拷贝会被缓存跳过（应用套餐场景）。
    /// 租户删除时由 <see cref="TenantDeletedMenuSeededCacheInvalidator"/> 清理对应条目。
    /// </summary>
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<Guid, DateTime> SeededCheckCache = new();

    private static readonly TimeSpan SeededCheckCacheDuration = TimeSpan.FromMinutes(1);

    /// <summary>清除某租户的「已播种」短缓存条目（同程序集内的事件处理器与重置流程共用）。</summary>
    internal static void ClearSeededCheckCache(Guid tenantId)
    {
        SeededCheckCache.TryRemove(tenantId, out _);
    }

    public MenuManager(
        IRepository<Menu, Guid> menuRepository,
        IRepository<MenuGrant, Guid> menuGrantRepository,
        IRepository<TenantPackage, Guid> tenantPackageRepository,
        ITenantRepository tenantRepository,
        IDataFilter dataFilter,
        Volo.Abp.Uow.IUnitOfWorkManager unitOfWorkManager,
        IAbpDistributedLock distributedLock)
    {
        _menuRepository = menuRepository;
        _menuGrantRepository = menuGrantRepository;
        _tenantPackageRepository = tenantPackageRepository;
        _tenantRepository = tenantRepository;
        _dataFilter = dataFilter;
        _unitOfWorkManager = unitOfWorkManager;
        _distributedLock = distributedLock;
    }

    /// <summary>
    /// 播种 Host 全局模板（幂等：按 Path 跳过已存在节点，DbMigrator 重复运行安全）。
    /// 模板后续增删节点不会被本方法同步到已有环境之外的数据（租户侧由 EnsureTenantMenusAsync 拷贝时的快照决定），
    /// 需要重新对齐时由管理员在菜单管理页手工调整或删表重种。
    /// </summary>
    public virtual async Task SeedHostTemplateAsync()
    {
        using (_dataFilter.Disable<IMultiTenant>())
        {
            // 一次全表查询同时充当「已存在判定」与「已存在实体读取」（按 Path 分组防重复行），
            // 避免循环内对每个已存在节点再查一次全表
            var hostMenusByPath = (await _menuRepository.GetListAsync(x => x.TenantId == null))
                .Where(x => x.Path != null)
                .GroupBy(x => x.Path!)
                .ToDictionary(g => g.Key, g => g.First());

            var definitions = MenuTemplateDefinition.All;
            var keyToId = new Dictionary<string, Guid>();

            // 先建父再建子：按定义顺序（模板列表保证父在子前）
            foreach (var def in definitions)
            {
                if (def.Path != null && hostMenusByPath.TryGetValue(def.Path, out var existing))
                {
                    // 已存在：记录 id 供子节点挂接
                    keyToId[def.Key] = existing.Id;
                    continue;
                }

                var parentKey = def.ParentKey;
                var parentId = parentKey != null && keyToId.TryGetValue(parentKey, out var pid) ? pid : (Guid?)null;
                var menu = new Menu(
                    GuidGenerator.Create(),
                    null,
                    parentId,
                    def.Type,
                    def.Title,
                    def.Name,
                    def.Path,
                    def.Icon,
                    def.OrderNo,
                    def.IsHide,
                    isEnabled: true,
                    def.PermissionName);
                await _menuRepository.InsertAsync(menu, autoSave: true);
                keyToId[def.Key] = menu.Id;
            }
        }
    }

    /// <summary>
    /// 租户懒拷贝：租户还没有任何菜单时，从 Host 模板整棵拷贝一份（角色授权留空——
    /// 模板上的角色授权属于 Host 角色，不随拷贝）。allowedTemplateMenuIds 为 null 表示全量拷贝。
    /// 租户已配置套餐（Tenant extra property PackageId）且未显式指定过滤集时，按套餐勾选过滤——
    /// 让"套餐"成为数据：即使应用套餐的请求曾失败，租户首次访问也会按套餐收敛，
    /// 而不是先看到全量菜单等管理员补救。
    /// 并发首访：先短缓存、再计数双检，拷贝+提交整体由分布式锁串行（多实例安全，
    /// ABP StaticPermissionSaver 同款范式：锁内开独立事务，提交先于锁释放）。
    /// 锁超时裸拷贝的极端竞态由 (TenantId, Path) 唯一索引兜底——后提交方撞索引抛
    /// DbUpdateException 以 500 暴露（宁可失败不可脏数据）。
    /// </summary>
    public virtual async Task EnsureTenantMenusAsync(Guid tenantId, HashSet<Guid>? allowedTemplateMenuIds = null)
    {
        // 短缓存命中：该租户一分钟内已确认过非空，跳过计数查询（my-menu 是全站最高频接口）。
        // 过期条目顺手驱逐（round4）：只读不删会让字典随租户创建/删除无限增长。
        if (SeededCheckCache.TryGetValue(tenantId, out var until))
        {
            if (DateTime.UtcNow < until)
            {
                return;
            }

            SeededCheckCache.TryRemove(tenantId, out _);
        }

        var tenantMenuCount = await _menuRepository.CountAsync(x => true);
        if (tenantMenuCount > 0)
        {
            MarkTenantSeeded(tenantId);
            return;
        }

        // 未显式指定过滤集时读租户套餐：套餐勾选存宿主库，需在禁用租户过滤器前取好。
        // 此读依赖的是历史已提交数据（应用套餐的请求写完 PackageId 后另行提交），
        // 因此可以在锁外、环境事务里完成，不与下方的独立事务产生读写依赖。
        if (allowedTemplateMenuIds == null)
        {
            allowedTemplateMenuIds = await LoadTenantPackageMenuIdsAsync(tenantId);
        }

        await using (var handle = await _distributedLock.TryAcquireAsync(
                         CopyLockKey(tenantId), TimeSpan.FromSeconds(CopyLockTimeoutSeconds)))
        {
            if (handle != null)
            {
                // 独立事务 + 提交在锁内（requiresNew：不与调用方环境事务共享提交时机）。
                // 后到实例拿锁后的双检查到的是已提交数据，"全量 vs 按套餐过滤"两类首拷
                // 不可能交错——任何一方整体提交后，另一方只会看到"已有菜单"而放弃，
                // 租户菜单集不会残留并发前的超宽快照。
                using (var copyUow = _unitOfWorkManager.Begin(requiresNew: true, isTransactional: true))
                {
                    try
                    {
                        // 双检：等锁期间另一实例可能已完成拷贝（独立事务里读到的必是已提交数据）
                        if (await _menuRepository.CountAsync(x => true) == 0)
                        {
                            await CopyTemplateToTenantAsync(tenantId, allowedTemplateMenuIds);
                        }
                        else
                        {
                            Logger.LogDebug(
                                "Tenant {TenantId} menus already exist (copied by another instance); skipping copy.",
                                tenantId);
                        }

                        await copyUow.CompleteAsync();
                    }
                    catch
                    {
                        try
                        {
                            await copyUow.RollbackAsync();
                        }
                        catch
                        {
                            // ignored
                        }

                        throw;
                    }
                }

                MarkTenantSeeded(tenantId);
                return;
            }

            // 锁等待超时仍未拿到锁（持锁方拷贝异常缓慢）：重查一次，仍无数据就裸拷贝，
            // 由唯一索引兜底（并发拷贝撞索引 → 500，宁可失败不可脏数据）
            if (await _menuRepository.CountAsync(x => true) > 0)
            {
                MarkTenantSeeded(tenantId);
            }
            else
            {
                Logger.LogWarning(
                    "Distributed lock for tenant menu lazy-copy timed out ({LockTimeoutSeconds}s); " +
                    "proceeding with an unlocked bare copy for tenant {TenantId}. " +
                    "Concurrent copies collide on the (TenantId, Path) unique index and fail with 500.",
                    CopyLockTimeoutSeconds, tenantId);

                await CopyTemplateToTenantAsync(tenantId, allowedTemplateMenuIds);
            }
        }
    }

    /// <summary>租户菜单懒拷贝/重置共用锁的等待上限（秒）：超时后懒拷贝降级裸拷贝、重置直接失败。</summary>
    public const int CopyLockTimeoutSeconds = 10;

    /// <summary>租户菜单懒拷贝的分布式锁 key（按租户隔离，互不阻塞）。</summary>
    public static string CopyLockKey(Guid tenantId) => $"AbpAdmin:Menu:TenantCopy:{tenantId:N}";

    private static void MarkTenantSeeded(Guid tenantId)
    {
        SeededCheckCache[tenantId] = DateTime.UtcNow + SeededCheckCacheDuration;
    }

    /// <summary>读租户的套餐勾选（Tenant.PackageId extra property → 套餐菜单子集）；无套餐返回 null。
    /// 属性名与写侧 TenantPackageManager 共用 <see cref="AbpAdminTenantConsts.PackageIdPropertyName"/>。</summary>
    protected virtual async Task<HashSet<Guid>?> LoadTenantPackageMenuIdsAsync(Guid tenantId)
    {
        var tenant = await _tenantRepository.FindAsync(tenantId);
        var packageIdStr = tenant?.GetProperty<string>(AbpAdminTenantConsts.PackageIdPropertyName);
        if (string.IsNullOrWhiteSpace(packageIdStr) || !Guid.TryParse(packageIdStr, out var packageId))
        {
            return null;
        }

        var query = await _tenantPackageRepository.WithDetailsAsync(x => x.Menus);
        var package = await AsyncExecuter.FirstOrDefaultAsync(query.Where(x => x.Id == packageId));
        return package == null || package.Menus.Count == 0
            ? null
            : package.Menus.Select(x => x.TemplateMenuId).ToHashSet();
    }

    private async Task CopyTemplateToTenantAsync(Guid tenantId, HashSet<Guid>? allowedTemplateMenuIds)
    {
        using (_dataFilter.Disable<IMultiTenant>())
        {
            var templateMenus = await _menuRepository.GetListAsync(x => x.TenantId == null);
            if (templateMenus.Count == 0)
            {
                if (CurrentTenant.Id != null)
                {
                    // 租户上下文里读不到 Host 模板 = 租户使用独立数据库（菜单仓储已切到租户库）。
                    // 此时绝不能把"Host 模板"误种进租户库（会产生租户库内无法管理的 TenantId=null 死数据，
                    // 且套餐里的宿主模板 Guid 与租户库模板 Guid 永不相交 → 过滤结果为空 → 租户菜单被清空）。
                    // fail-closed：明确报错，由 Host 侧先完成模板播种/在共享库部署下使用菜单功能。
                    throw new BusinessException(AbpAdminDomainErrorCodes.Menus.HostTemplateMissing)
                        .WithData("TenantId", CurrentTenant.Id.Value);
                }

                await SeedHostTemplateAsync();
                templateMenus = await _menuRepository.GetListAsync(x => x.TenantId == null);
            }

            if (allowedTemplateMenuIds != null)
            {
                templateMenus = FilterWithAncestors(templateMenus, allowedTemplateMenuIds);

                if (templateMenus.Count == 0)
                {
                    // 过滤集与模板完全不相交：套餐勾选引用的模板行已不存在（典型成因：模板"删表重种"
                    // 重新生成了全部 Guid）。放行会导致"硬删租户现有菜单 → 拷贝 0 个节点"的清空事故，
                    // 因此拒绝并要求先重新配置套餐。fail-closed，防数据丢失。
                    throw new BusinessException(AbpAdminDomainErrorCodes.Menus.PackageMenusDangling)
                        .WithData("AllowedCount", allowedTemplateMenuIds.Count);
                }
            }

            var idMap = new Dictionary<Guid, Guid>();
            foreach (var template in templateMenus.OrderBy(x => x.OrderNo))
            {
                idMap[template.Id] = GuidGenerator.Create();
            }

            foreach (var template in templateMenus.OrderBy(x => x.OrderNo))
            {
                var parentId = template.ParentId != null && idMap.TryGetValue(template.ParentId.Value, out var pid)
                    ? pid
                    : (Guid?)null;
                // 逐行 autoSave 会在锁临界段内做 N 次 SaveChanges 往返、放大持锁时长；
                // 改为收集后一次性收口（随所在事务一并提交）
                await _menuRepository.InsertAsync(new Menu(
                    idMap[template.Id],
                    tenantId,
                    parentId,
                    template.Type,
                    template.Title,
                    template.Name,
                    template.Path,
                    template.Icon,
                    template.OrderNo,
                    template.IsHide,
                    template.IsEnabled,
                    template.PermissionName));
            }

            if (_unitOfWorkManager.Current != null)
            {
                await _unitOfWorkManager.Current.SaveChangesAsync();
            }

            // 实际落库的节点数 = 过滤（FilterWithAncestors）后的 templateMenus 数量
            Logger.LogInformation(
                "Lazy-copied {MenuCount} menu(s) from the host template to tenant {TenantId}.",
                templateMenus.Count, tenantId);
        }
    }

    /// <summary>
    /// 重置租户菜单为"按套餐过滤的模板拷贝"（租户套餐应用入口，也可不传套餐做全量重置）。
    /// 破坏性操作：会删除该租户现有菜单及其角色勾选记录。菜单仓储在租户上下文调用。
    /// 用硬删除：软删除行仍占用 (TenantId, Path) 唯一索引，重拷同路径会撞唯一约束。
    /// 与懒拷贝共用同一把锁，且在锁内独立事务里"先删后拷、一次提交"（EnsureTenantMenusAsync
    /// 同款范式）：删除+重拷对外是原子单元——并发懒拷贝要么整体发生在重置前（随后被删除）、
    /// 要么在重置提交后看到已有菜单而放弃，不可能交错出半程状态。拿不到锁直接失败
    /// （管理端操作可重试），不裸跑重置（ABP StaticPermissionSaver 同款 fail-closed）。
    /// </summary>
    public virtual async Task ResetTenantMenusAsync(Guid tenantId, HashSet<Guid>? allowedTemplateMenuIds)
    {
        // 硬删全部菜单后必须清掉「已播种」短缓存，否则紧随其后的 EnsureTenantMenusAsync
        // 会被缓存短路，租户菜单停留在空树（应用套餐场景）
        ClearSeededCheckCache(tenantId);

        await using (var handle = await _distributedLock.TryAcquireAsync(
                         CopyLockKey(tenantId), TimeSpan.FromSeconds(CopyLockTimeoutSeconds)))
        {
            if (handle == null)
            {
                throw new AbpException(
                    $"Could not acquire the tenant menu reset distributed lock for tenant {tenantId}.");
            }

            using (var resetUow = _unitOfWorkManager.Begin(requiresNew: true, isTransactional: true))
            {
                try
                {
                    await DeleteTenantMenusCoreAsync();
                    await CopyTemplateToTenantAsync(tenantId, allowedTemplateMenuIds);
                    await resetUow.CompleteAsync();
                }
                catch
                {
                    try
                    {
                        await resetUow.RollbackAsync();
                    }
                    catch
                    {
                        // ignored
                    }

                    throw;
                }
            }

            Logger.LogInformation(
                "Tenant menus reset committed for tenant {TenantId} (delete + re-copy in one transaction).",
                tenantId);
        }
    }

    /// <summary>
    /// 删光当前租户上下文的全部菜单与角色勾选（租户删除清理用，不重拷模板）。
    /// 必须在租户上下文调用（菜单仓储按 TenantId 过滤）——参数只用于清「已播种」短缓存，
    /// 删除范围由 ICurrentTenant 决定，故显式校验两者一致，防止在 Host 上下文误删全局模板。
    /// 硬删除理由同 ResetTenantMenusAsync。
    /// </summary>
    public virtual async Task DeleteTenantMenusAsync(Guid tenantId)
    {
        if (CurrentTenant.Id != tenantId)
        {
            throw new BusinessException(AbpAdminDomainErrorCodes.Tenants.TenantMenuCleanupWrongTenantContext)
                .WithData("ExpectedTenantId", tenantId)
                .WithData("CurrentTenantId", CurrentTenant.Id?.ToString() ?? "null");
        }

        ClearSeededCheckCache(tenantId);

        await DeleteTenantMenusCoreAsync();
    }

    private async Task DeleteTenantMenusCoreAsync()
    {
        var tenantMenus = await _menuRepository.GetListAsync(x => true);
        var menuIds = tenantMenus.Select(x => x.Id).ToHashSet();

        var grants = await _menuGrantRepository.GetListAsync(x => menuIds.Contains(x.MenuId));
        foreach (var grant in grants)
        {
            await _menuGrantRepository.DeleteAsync(grant);
        }

        foreach (var menu in tenantMenus)
        {
            await _menuRepository.HardDeleteAsync(menu);
        }

        // 显式先刷删除：EF 单次 SaveChanges 默认"先插后删"，重置场景里删除与重拷
        // 正是同一批 (TenantId, Path)——不先刷删除，重拷插入会撞唯一索引
        if (_unitOfWorkManager.Current != null)
        {
            await _unitOfWorkManager.Current.SaveChangesAsync();
        }
    }

    /// <summary>保留勾选节点及其全部祖先（祖先链上的目录才能挂接子节点）。
    /// 遍历结构统一走 <see cref="MenuTreeWalker.AncestorChainOf"/>（断链/防环语义集中一处）。</summary>
    private static List<Menu> FilterWithAncestors(List<Menu> templateMenus, HashSet<Guid> allowedIds)
    {
        var byId = templateMenus.ToDictionary(x => x.Id);
        var keep = new HashSet<Guid>();
        foreach (var id in allowedIds)
        {
            // 链不完整（勾选的 id 或其祖先不在模板里）：跳过该节点；
            // 整条链（含自身）全部保留——祖先链上的目录才能挂接子节点
            var chain = MenuTreeWalker.AncestorChainOf(byId, id, includeSelf: true);
            if (chain == null)
            {
                continue;
            }

            foreach (var node in chain)
            {
                keep.Add(node.Id);
            }
        }

        return templateMenus.Where(x => keep.Contains(x.Id)).ToList();
    }
}
