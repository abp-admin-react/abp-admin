using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AbpAdmin.OperationLogs;
using AbpAdmin.Permissions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Options;
using Volo.Abp;
using Volo.Abp.Application.Dtos;
using Volo.Abp.Authorization.Permissions;
using Volo.Abp.Caching;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Localization;
using Volo.Abp.MultiTenancy;
using Volo.Abp.PermissionManagement;

namespace AbpAdmin.PermissionManagement;

/// <summary>
/// 权限定义运行时管理（IPermissionDefinitionManagementAppService 实现，Host 专属）。
/// 数据就是 ABP 既有的两张定义记录表（StaticPermissionSaver 启动时也写它们，
/// IsStatic 语义由"是否出现在代码定义"区分），写后删公共 stamp 键触发
/// DynamicPermissionDefinitionStore 全实例重读——这是 ABP 自带的失效通道，无需自建缓存层。
/// 与静态定义的关系：权限解析顺序静态优先，因此禁止运行时定义与静态同名（防"定义了却不生效"的假象）。
/// </summary>
[Authorize(AbpAdminPermissions.PermissionDefinitions.Default)]
public class PermissionDefinitionManagementAppService : AbpAdminAppService, IPermissionDefinitionManagementAppService
{
    private readonly IRepository<PermissionGroupDefinitionRecord, Guid> _groupRepository;
    private readonly IRepository<PermissionDefinitionRecord, Guid> _definitionRepository;
    private readonly IPermissionDefinitionManager _permissionDefinitionManager;
    private readonly IStaticPermissionDefinitionStore _staticPermissionStore;
    private readonly IDistributedCache _distributedCache;
    private readonly IDynamicPermissionDefinitionStoreInMemoryCache _storeCache;
    private readonly ILocalizableStringSerializer _localizableStringSerializer;
    private readonly IStringLocalizerFactory _stringLocalizerFactory;
    private readonly AbpDistributedCacheOptions _cacheOptions;

    public PermissionDefinitionManagementAppService(
        IRepository<PermissionGroupDefinitionRecord, Guid> groupRepository,
        IRepository<PermissionDefinitionRecord, Guid> definitionRepository,
        IPermissionDefinitionManager permissionDefinitionManager,
        IStaticPermissionDefinitionStore staticPermissionStore,
        IDistributedCache distributedCache,
        IDynamicPermissionDefinitionStoreInMemoryCache storeCache,
        IOptions<AbpDistributedCacheOptions> cacheOptions,
        ILocalizableStringSerializer localizableStringSerializer,
        IStringLocalizerFactory stringLocalizerFactory)
    {
        _groupRepository = groupRepository;
        _definitionRepository = definitionRepository;
        _permissionDefinitionManager = permissionDefinitionManager;
        _staticPermissionStore = staticPermissionStore;
        _distributedCache = distributedCache;
        _storeCache = storeCache;
        _cacheOptions = cacheOptions.Value;
        _localizableStringSerializer = localizableStringSerializer;
        _stringLocalizerFactory = stringLocalizerFactory;
    }

    public virtual async Task<PagedResultDto<PermissionGroupRecordDto>> GetGroupsAsync(GetPermissionGroupListInput input)
    {
        // 组记录数量级是"个位数~几十"，全量取回内存过滤分页即可（定义表才需要数据库分页）
        var groups = await _groupRepository.GetListAsync();

        var filtered = groups.AsQueryable();
        if (!string.IsNullOrWhiteSpace(input.Filter))
        {
            filtered = filtered.Where(g =>
                g.Name.Contains(input.Filter, StringComparison.OrdinalIgnoreCase) ||
                g.DisplayName.Contains(input.Filter, StringComparison.OrdinalIgnoreCase));
        }

        var ordered = filtered.OrderBy(g => g.Name, StringComparer.Ordinal).ToList();
        var page = ordered
            .Skip(input.SkipCount)
            .Take(input.MaxResultCount)
            .ToList();

        return new PagedResultDto<PermissionGroupRecordDto>(
            ordered.Count,
            page.Select(g => new PermissionGroupRecordDto
            {
                Id = g.Id,
                Name = g.Name,
                DisplayName = g.DisplayName,
                DisplayNameLocalized = LocalizeDisplayName(g.DisplayName),
            }).ToList());
    }

    public virtual async Task<PagedResultDto<PermissionDefinitionRecordDto>> GetDefinitionsAsync(
        GetPermissionDefinitionListInput input)
    {
        var queryable = await _definitionRepository.GetQueryableAsync();

        var filtered = queryable.AsQueryable();
        if (!string.IsNullOrWhiteSpace(input.GroupName))
        {
            filtered = filtered.Where(d => d.GroupName == input.GroupName);
        }

        // 定义表由 StaticPermissionSaver 写入全部静态权限（量级千行），count 与页数据都下推数据库
        if (!string.IsNullOrWhiteSpace(input.Filter))
        {
            // Contains 进数据库翻译（大小写语义随提供程序，管理页可接受）
            var pattern = input.Filter.Trim();
            filtered = filtered.Where(d => d.Name.Contains(pattern) || d.DisplayName.Contains(pattern));
        }

        var ordered = filtered.OrderBy(d => d.Name);
        var totalCount = await AsyncExecuter.CountAsync(ordered);
        var page = await AsyncExecuter.ToListAsync(
            ordered
                .Skip(input.SkipCount)
                .Take(input.MaxResultCount));

        return new PagedResultDto<PermissionDefinitionRecordDto>(
            totalCount,
            page.Select(d => new PermissionDefinitionRecordDto
            {
                Id = d.Id,
                GroupName = d.GroupName,
                Name = d.Name,
                ParentName = d.ParentName,
                DisplayName = d.DisplayName,
                DisplayNameLocalized = LocalizeDisplayName(d.DisplayName),
                IsEnabled = d.IsEnabled,
                MultiTenancySide = (byte)d.MultiTenancySide,
            }).ToList());
    }

    [Authorize(AbpAdminPermissions.PermissionDefinitions.Create)]
    [OperationLog("权限定义", "创建权限组", BizNo = "{{input.name}}", Success = "创建了权限组 {{input.name}}")]
    public virtual async Task<PermissionGroupRecordDto> CreateGroupAsync(CreatePermissionGroupInput input)
    {
        EnsureHostSide();
        var existing = await AsyncExecuter.FirstOrDefaultAsync(
            (await _groupRepository.GetQueryableAsync()).Where(g => g.Name == input.Name));
        if (existing != null)
        {
            throw new UserFriendlyException(L["AbpAdmin:PermissionGroupAlreadyExists", input.Name]);
        }

        var record = new PermissionGroupDefinitionRecord(
            GuidGenerator.Create(), input.Name, input.DisplayName);
        await _groupRepository.InsertAsync(record, autoSave: true);
        await InvalidateStoreCacheAsync();

        return new PermissionGroupRecordDto
        {
            Id = record.Id,
            Name = record.Name,
            DisplayName = record.DisplayName,
            DisplayNameLocalized = LocalizeDisplayName(record.DisplayName),
        };
    }

    [Authorize(AbpAdminPermissions.PermissionDefinitions.Update)]
    [OperationLog("权限定义", "更新权限组", BizNo = "{{id}}", Success = "更新了权限组 {{id}}")]
    public virtual async Task<PermissionGroupRecordDto> UpdateGroupAsync(Guid id, UpdatePermissionGroupInput input)
    {
        EnsureHostSide();
        var record = await _groupRepository.GetAsync(id);
        record.DisplayName = input.DisplayName;
        await _groupRepository.UpdateAsync(record, autoSave: true);
        await InvalidateStoreCacheAsync();

        return new PermissionGroupRecordDto
        {
            Id = record.Id,
            Name = record.Name,
            DisplayName = record.DisplayName,
            DisplayNameLocalized = LocalizeDisplayName(record.DisplayName),
        };
    }

    [Authorize(AbpAdminPermissions.PermissionDefinitions.Delete)]
    [OperationLog("权限定义", "删除权限组", BizNo = "{{id}}", Success = "删除了权限组 {{id}}")]
    public virtual async Task DeleteGroupAsync(Guid id)
    {
        EnsureHostSide();
        var record = await _groupRepository.GetAsync(id);
        var hasDefinitions = await AsyncExecuter.AnyAsync(
            (await _definitionRepository.GetQueryableAsync()).Where(d => d.GroupName == record.Name));
        if (hasDefinitions)
        {
            throw new UserFriendlyException(L["AbpAdmin:PermissionGroupHasDefinitions", record.Name]);
        }

        await _groupRepository.DeleteAsync(record, autoSave: true);
        await InvalidateStoreCacheAsync();
    }

    [Authorize(AbpAdminPermissions.PermissionDefinitions.Create)]
    [OperationLog("权限定义", "创建权限定义", BizNo = "{{input.name}}", Success = "创建了权限定义 {{input.name}}（组：{{input.groupName}}）")]
    public virtual async Task<PermissionDefinitionRecordDto> CreateDefinitionAsync(CreatePermissionDefinitionInput input)
    {
        EnsureHostSide();
        // 先查动态记录表（精确归因"重名"），再查管理器（区分"与静态定义撞名"）
        var nameExists = await AsyncExecuter.AnyAsync(
            (await _definitionRepository.GetQueryableAsync()).Where(d => d.Name == input.Name));
        if (nameExists)
        {
            throw new UserFriendlyException(L["AbpAdmin:PermissionNameAlreadyExists", input.Name]);
        }

        // 与静态定义同名会被静态优先的解析顺序遮蔽——直接拒绝，杜绝"配置了却不生效"
        if (await _permissionDefinitionManager.GetOrNullAsync(input.Name) != null)
        {
            throw new UserFriendlyException(L["AbpAdmin:PermissionShadowsStatic", input.Name]);
        }

        if (!string.IsNullOrWhiteSpace(input.ParentName) &&
            await _permissionDefinitionManager.GetOrNullAsync(input.ParentName) == null)
        {
            throw new UserFriendlyException(L["AbpAdmin:PermissionParentNotFound", input.ParentName]);
        }

        // 组可以只存在于静态定义（把运行时权限挂进代码定义的组）；两者都查不到才拒绝
        var groupExists = await AsyncExecuter.AnyAsync(
            (await _groupRepository.GetQueryableAsync()).Where(g => g.Name == input.GroupName));
        if (!groupExists && await FindStaticGroupAsync(input.GroupName) == null)
        {
            throw new UserFriendlyException(L["AbpAdmin:PermissionGroupNotFound", input.GroupName]);
        }

        var record = new PermissionDefinitionRecord(
            GuidGenerator.Create(),
            input.GroupName,
            input.Name,
            resourceName: null,
            managementPermissionName: null,
            // 无父权限必须落 null（不能传 ""）：FillAsync 按 ParentName == null 识别根权限建树
            parentName: input.ParentName,
            displayName: input.DisplayName,
            isEnabled: input.IsEnabled);
        await _definitionRepository.InsertAsync(record, autoSave: true);
        await InvalidateStoreCacheAsync();

        return MapDefinition(record);
    }

    [Authorize(AbpAdminPermissions.PermissionDefinitions.Update)]
    [OperationLog("权限定义", "更新权限定义", BizNo = "{{id}}", Success = "更新了权限定义 {{id}}")]
    public virtual async Task<PermissionDefinitionRecordDto> UpdateDefinitionAsync(Guid id, UpdatePermissionDefinitionInput input)
    {
        EnsureHostSide();
        var record = await _definitionRepository.GetAsync(id);
        record.DisplayName = input.DisplayName;
        record.IsEnabled = input.IsEnabled;
        await _definitionRepository.UpdateAsync(record, autoSave: true);
        await InvalidateStoreCacheAsync();

        return MapDefinition(record);
    }

    [Authorize(AbpAdminPermissions.PermissionDefinitions.Delete)]
    [OperationLog("权限定义", "删除权限定义", BizNo = "{{id}}", Success = "删除了权限定义 {{id}}")]
    public virtual async Task DeleteDefinitionAsync(Guid id)
    {
        EnsureHostSide();
        var record = await _definitionRepository.GetAsync(id);
        // StaticPermissionSaver 也会把代码内定义落库成记录：这类记录是静态定义的镜像，
        // 删了只会撑到下次启动被重建（期间动态存储少一份冗余、语义混乱）——静态定义的变更走代码。
        // 判定必须用静态存储（IPermissionDefinitionManager 会先查静态再查动态，
        // 运行时刚创建的动态定义也会命中，误伤删除）
        if (await _staticPermissionStore.GetOrNullAsync(record.Name) != null)
        {
            throw new UserFriendlyException(L["AbpAdmin:PermissionShadowsStatic", record.Name]);
        }

        var hasChildren = await AsyncExecuter.AnyAsync(
            (await _definitionRepository.GetQueryableAsync()).Where(d => d.ParentName == record.Name));
        if (hasChildren)
        {
            throw new UserFriendlyException(L["AbpAdmin:PermissionDefinitionHasChildren", record.Name]);
        }

        await _definitionRepository.DeleteAsync(record, autoSave: true);
        await InvalidateStoreCacheAsync();
    }

    private PermissionDefinitionRecordDto MapDefinition(PermissionDefinitionRecord record)
    {
        return new PermissionDefinitionRecordDto
        {
            Id = record.Id,
            GroupName = record.GroupName,
            Name = record.Name,
            ParentName = record.ParentName,
            DisplayName = record.DisplayName,
            DisplayNameLocalized = LocalizeDisplayName(record.DisplayName),
            IsEnabled = record.IsEnabled,
            MultiTenancySide = (byte)record.MultiTenancySide,
        };
    }

    /// <summary>静态定义组查找（动态组之外的组名合法性校验用）。</summary>
    private async Task<PermissionGroupDefinition?> FindStaticGroupAsync(string name)
    {
        var groups = await _permissionDefinitionManager.GetGroupsAsync();
        return groups.FirstOrDefault(g => g.Name == name);
    }

    /// <summary>
    /// 把落库的本地化串（"L:资源:键" 或纯文本）解析为当前文化的显示名；
    /// 反序列化失败（手工写入的裸文本）原样返回。
    /// </summary>
    private string LocalizeDisplayName(string serialized)
    {
        try
        {
            var localizable = _localizableStringSerializer.Deserialize(serialized);
            return localizable.Localize(_stringLocalizerFactory);
        }
        catch (Exception)
        {
            return serialized;
        }
    }

    /// <summary>
    /// 触发动态定义存储刷新。双动作：
    /// 升级回归锚点：ABP 若改动 stamp 内部常量格式，
    /// PermissionDefinitionManagementAppServiceTests.CreateGroup_And_Definition_Should_Be_Visible_To_Runtime_Store
    /// 集成用例会红（创建后解析不到），以它为 ABP 升级时的对照。
    /// ① 删除公共 stamp（{KeyPrefix}_AbpInMemoryPermissionCacheStamp）——其他实例按各自的
    ///    30 秒检查节流在下次边界发现 stamp 变化即全量重读（ABP 自带失效通道，键名与框架同源拼接）；
    /// ② 本实例立即重置 LastCheckTime——绕过 30 秒节流，下一次权限解析即重读记录表，
    ///    保证"改完立刻生效"的单机语义（多实例最长延迟一个检查周期，与框架行为一致）。
    /// </summary>
    private async Task InvalidateStoreCacheAsync()
    {
        await _distributedCache.RemoveAsync(
            $"{_cacheOptions.KeyPrefix}_AbpInMemoryPermissionCacheStamp");
        _storeCache.LastCheckTime = null;
    }
}
