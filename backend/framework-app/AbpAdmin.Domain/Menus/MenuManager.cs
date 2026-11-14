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
    bool IsHide = false,
    string[]? AnyPermissionNames = null,
    bool HostOnly = false);

/// <summary>
/// 同步宿主菜单模板，并按租户套餐创建或重置租户菜单。
/// </summary>
public class MenuManager : DomainService, ITransientDependency
{
    private readonly IRepository<Menu, Guid> _menuRepository;
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
        IRepository<TenantPackage, Guid> tenantPackageRepository,
        ITenantRepository tenantRepository,
        IDataFilter dataFilter,
        Volo.Abp.Uow.IUnitOfWorkManager unitOfWorkManager,
        IAbpDistributedLock distributedLock)
    {
        _menuRepository = menuRepository;
        _tenantPackageRepository = tenantPackageRepository;
        _tenantRepository = tenantRepository;
        _dataFilter = dataFilter;
        _unitOfWorkManager = unitOfWorkManager;
        _distributedLock = distributedLock;
    }

    /// <summary>
    /// 按路径幂等同步宿主模板。已有节点保留 ID，仅更新父级、顺序、标题、图标和权限。
    /// 模板外菜单和已有租户的菜单快照保留。
    /// </summary>
    public virtual async Task SeedHostTemplateAsync()
    {
        using (_dataFilter.Disable<IMultiTenant>())
        {
            var hostMenusByPath = (await _menuRepository.GetListAsync(x => x.TenantId == null))
                .Where(x => x.Path != null)
                .GroupBy(x => x.Path!)
                .ToDictionary(g => g.Key, g => g.First());

            var definitions = MenuTemplateDefinition.All;
            var keyToId = new Dictionary<string, Guid>();
            var existingByTemplateKey = new List<(MenuDefinition Def, Guid Id)>();

            // 按父先子后补齐节点，并记录新旧节点的 ID。
            foreach (var def in definitions)
            {
                if (def.Path != null && hostMenusByPath.TryGetValue(def.Path, out var existing))
                {
                    keyToId[def.Key] = existing.Id;
                    existingByTemplateKey.Add((def, existing.Id));
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
                menu.SetAnyPermissionNames(def.AnyPermissionNames ?? []);
                await _menuRepository.InsertAsync(menu, autoSave: true);
                keyToId[def.Key] = menu.Id;
            }

            // 全部 ID 已就绪，已有节点也能移到新目录。
            foreach (var (def, id) in existingByTemplateKey)
            {
                var menu = await _menuRepository.GetAsync(id);
                var parentId = def.ParentKey != null && keyToId.TryGetValue(def.ParentKey, out var pid) ? pid : (Guid?)null;
                var changed = false;
                if (menu.ParentId != parentId) { menu.SetParent(parentId); changed = true; }
                if (menu.OrderNo != def.OrderNo) { menu.SetOrderNo(def.OrderNo); changed = true; }
                if (menu.Title != def.Title) { menu.SetTitle(def.Title); changed = true; }
                if (menu.Icon != def.Icon) { menu.SetIcon(def.Icon); changed = true; }
                if (menu.PermissionName != def.PermissionName) { menu.SetPermissionName(def.PermissionName); changed = true; }
                if (def.IsHide && !menu.IsHide) { menu.Hide(); changed = true; }
                var anyPermissionNames = def.AnyPermissionNames ?? [];
                if (!menu.GetAnyPermissionNames().SequenceEqual(anyPermissionNames))
                {
                    menu.SetAnyPermissionNames(anyPermissionNames);
                    changed = true;
                }
                if (changed)
                {
                    await _menuRepository.UpdateAsync(menu, autoSave: true);
                }
            }
        }
    }

    /// <summary>
    /// 租户没有菜单时，从宿主模板复制。省略过滤集时读取租户套餐，无可用套餐勾选时复制全量。
    /// 获锁后在独立事务内复查并提交。超时后再次复查，仍为空时无锁复制，由菜单路径唯一索引兜底。
    /// </summary>
    public virtual async Task EnsureTenantMenusAsync(Guid tenantId, HashSet<Guid>? allowedTemplateMenuIds = null)
    {
        // 短缓存命中：该租户一分钟内已确认过非空，跳过计数查询（my-menu 是全站最高频接口）。
        // 过期条目顺手驱逐：只读不删会让字典随租户创建/删除无限增长。
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
                                "Tenant {TenantId} menus already exist; skipping lazy copy.",
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

            // 等锁期间可能已有请求完成复制，无锁复制前再次检查。
            if (await _menuRepository.CountAsync(x => true) > 0)
            {
                MarkTenantSeeded(tenantId);
            }
            else
            {
                Logger.LogWarning(
                    "Timed out waiting {LockTimeoutSeconds}s for the menu copy lock; copying without the lock for tenant {TenantId}. " +
                    "Concurrent inserts may conflict on the menu path unique index.",
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
                var tenantMenu = new Menu(
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
                    template.PermissionName);
                tenantMenu.SetAnyPermissionNames(template.GetAnyPermissionNames());
                await _menuRepository.InsertAsync(tenantMenu);
            }

            if (_unitOfWorkManager.Current != null)
            {
                await _unitOfWorkManager.Current.SaveChangesAsync();
            }

            // 事务由调用方提交，此处只记录已准备的节点数。
            Logger.LogDebug(
                "Prepared {MenuCount} menu(s) from the host template for tenant {TenantId}.",
                templateMenus.Count, tenantId);
        }
    }

    /// <summary>
    /// 重置租户菜单为"按套餐过滤的模板拷贝"（租户套餐应用入口，也可不传套餐做全量重置）。
    /// 破坏性操作：会删除该租户现有菜单。菜单仓储在租户上下文调用。
    /// 用硬删除：软删除行仍占用 (TenantId, Path) 唯一索引，重拷同路径会撞唯一约束。
    /// 与懒拷贝共用同一把锁，且在锁内独立事务里"先删后拷、一次提交"（EnsureTenantMenusAsync
    /// 同款范式）：删除+重拷对外是原子单元——并发懒拷贝要么整体发生在重置前（随后被删除）、
    /// 要么在重置提交后看到已有菜单而放弃，不可能交错出半程状态。拿不到锁直接失败
    /// （管理端操作可重试），不裸跑重置（ABP StaticPermissionSaver 同款 fail-closed）。
    /// </summary>
    public virtual async Task ResetTenantMenusAsync(Guid tenantId, HashSet<Guid>? allowedTemplateMenuIds)
    {
        // 重置可能改变租户菜单是否为空，先清除检查缓存。
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
    /// 删光当前租户上下文的全部菜单（租户删除清理用，不重拷模板）。
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
