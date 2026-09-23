using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using EasyAbp.Abp.DataDictionary;
using Microsoft.AspNetCore.Authorization;
using Volo.Abp;
using Volo.Abp.Application.Dtos;
using Volo.Abp.Domain.Repositories;
namespace AbpAdmin.DataDictionaries;

/// <summary>
/// 字典管理面的唯一应用服务（T3.4 第 12 步，后经收口扩展为列表/创建/删除）。
/// 权限口径：字典视图 GET {code} 放开给所有登录用户（普通用户的下拉不依赖模块权限）；
/// 列表与写操作沿用模块权限定义 Default/Create/Update/Delete，不另造权限。
/// EasyAbp 模块的 HttpApi/Application 依赖已整体移除——其控制器（全量替换、无静态
/// 结构锁）不再暴露，"静态字典结构由代码定义"这一不变量只在本服务闭合。
/// 本端点直接读库，不经过模块的缓存（CacheDataDictionaryValueProvider 只缓存 DisplayText
/// 且没有失效逻辑），所以修改保存后立即生效。
/// </summary>
[Authorize]
public class DataDictionaryViewAppService : AbpAdminAppService, IDataDictionaryViewAppService
{
    private readonly IDataDictionaryRepository _dataDictionaryRepository;
    private readonly IDataDictionaryManager _dataDictionaryManager;
    private readonly IRepository<DataDictionaryItemMeta, Guid> _metaRepository;
    private readonly DataDictionaryItemMetaManager _metaManager;

    public DataDictionaryViewAppService(
        IDataDictionaryRepository dataDictionaryRepository,
        IDataDictionaryManager dataDictionaryManager,
        IRepository<DataDictionaryItemMeta, Guid> metaRepository,
        DataDictionaryItemMetaManager metaManager)
    {
        _dataDictionaryRepository = dataDictionaryRepository;
        _dataDictionaryManager = dataDictionaryManager;
        _metaRepository = metaRepository;
        _metaManager = metaManager;
    }

    /// <inheritdoc />
    [RemoteService(false)]
    [Authorize(EasyAbp.Abp.DataDictionary.Permissions.DataDictionaryPermissions.DataDictionary.Default)]
    public virtual async Task<PagedResultDto<DataDictionaryListItemDto>> GetListAsync(PagedResultRequestDto input)
    {
        var totalCount = await _dataDictionaryRepository.GetCountAsync();
        // includeDetails: false——列表不展示 Items，省掉无谓的 Items join
        var dicts = await _dataDictionaryRepository.GetPagedListAsync(
            input.SkipCount, input.MaxResultCount, "Code", includeDetails: false);

        return new PagedResultDto<DataDictionaryListItemDto>(
            totalCount,
            dicts
                .Select(d => new DataDictionaryListItemDto
                {
                    Id = d.Id,
                    Code = d.Code,
                    DisplayText = d.DisplayText,
                    Description = d.Description,
                    IsStatic = d.IsStatic
                })
                .ToList());
    }

    /// <inheritdoc />
    [RemoteService(false)]
    public virtual async Task<DataDictionaryViewDto?> GetAsync(string code)
    {
        // FindAsync 默认 includeDetails: true（模块的 EF 仓储 WithDetails 会带上 Items）
        var dict = await _dataDictionaryRepository.FindAsync(d => d.Code == code);
        if (dict == null)
        {
            return null;
        }

        var metas = await _metaRepository.GetListAsync(x => x.DictionaryCode == code);
        var metaByItemCode = metas.ToDictionary(x => x.ItemCode, StringComparer.Ordinal);

        return new DataDictionaryViewDto
        {
            Code = dict.Code,
            DisplayText = dict.DisplayText,
            Description = dict.Description,
            IsStatic = dict.IsStatic,
            Items = dict.Items
                .Select(item => new DataDictionaryItemViewDto
                {
                    Code = item.Code,
                    DisplayText = item.DisplayText,
                    Description = item.Description,
                    IsStatic = item.IsStatic,
                    TagType = metaByItemCode.GetValueOrDefault(item.Code)?.TagType,
                    Order = metaByItemCode.GetValueOrDefault(item.Code)?.Order ?? int.MaxValue
                })
                // 显示顺序由元数据的 Order 决定；没有元数据的手工项排最后按编码字典序兜底
                .OrderBy(x => x.Order)
                .ThenBy(x => x.Code, StringComparer.Ordinal)
                .ToList()
        };
    }

    /// <inheritdoc />
    [RemoteService(false)]
    [Authorize(EasyAbp.Abp.DataDictionary.Permissions.DataDictionaryPermissions.DataDictionary.Create)]
    public virtual async Task<DataDictionaryViewDto> CreateAsync(CreateDataDictionaryInput input)
    {
        var existing = await _dataDictionaryRepository.FindAsync(d => d.Code == input.Code);
        if (existing != null)
        {
            throw new BusinessException(AbpAdminDomainErrorCodes.DataDictionaries.DictionaryCodeAlreadyExists)
                .WithData("DictionaryCode", input.Code);
        }

        // IsStatic 不开放给 API：静态字典的权威来源是代码（种子），从界面建静态字典
        // 会在下次部署被枚举同步的结构同步当作无主残留清掉
        var dict = new DataDictionary(
            GuidGenerator.Create(),
            CurrentTenant.Id,
            input.Code,
            input.DisplayText,
            input.Description,
            new List<DataDictionaryItem>(),
            isStatic: false);
        await _dataDictionaryManager.CreateAsync(dict);

        // 提交后再回读：新实体处于 Added 状态，对 SaveChanges 前的 SQL 查询不可见
        await CurrentUnitOfWork.SaveChangesAsync();

        return await GetAsync(input.Code)
            ?? throw new BusinessException(AbpAdminDomainErrorCodes.DataDictionaries.DictionaryNotFound)
                .WithData("DictionaryCode", input.Code);
    }

    /// <inheritdoc />
    [RemoteService(false)]
    [Authorize(EasyAbp.Abp.DataDictionary.Permissions.DataDictionaryPermissions.DataDictionary.Delete)]
    public virtual async Task DeleteAsync(string code)
    {
        var dict = await _dataDictionaryRepository.FindAsync(d => d.Code == code);
        if (dict == null)
        {
            throw new BusinessException(AbpAdminDomainErrorCodes.DataDictionaries.DictionaryNotFound)
                .WithData("DictionaryCode", code);
        }

        if (dict.IsStatic)
        {
            // 同一把静态结构锁：静态字典不能整本删除（删掉会让引用它的后端 switch/常量失效）；
            // 下线途径 = 删代码定义后由枚举同步的结构同步清理
            throw new BusinessException(AbpAdminDomainErrorCodes.DataDictionaries.StaticStructureLocked)
                .WithData("DictionaryCode", code);
        }

        await _dataDictionaryRepository.DeleteAsync(dict);

        // 旁挂元数据行随字典一起清（模块实体删除感知不到我们的表，不能等部署期种子兜底扫）
        await _metaManager.DeleteAllForDictionaryAsync(code);
    }

    /// <inheritdoc />
    [RemoteService(false)]
    [Authorize(EasyAbp.Abp.DataDictionary.Permissions.DataDictionaryPermissions.DataDictionary.Update)]
    public virtual async Task<DataDictionaryViewDto> SaveItemsAsync(
        string dictionaryCode, SaveDataDictionaryItemsInput input)
    {
        // 校验全部通过才动库：TagType 合法、编码不重复
        var invalidTag = input.Items.FirstOrDefault(x => !DataDictionaryTagTypes.IsValid(x.TagType));
        if (invalidTag != null)
        {
            throw new BusinessException(AbpAdminDomainErrorCodes.DataDictionaries.InvalidTagType)
                .WithData("TagType", invalidTag.TagType);
        }

        var duplicateCode = input.Items
            .GroupBy(x => x.Code, StringComparer.Ordinal)
            .FirstOrDefault(g => g.Count() > 1);
        if (duplicateCode != null)
        {
            throw new BusinessException(AbpAdminDomainErrorCodes.DataDictionaries.DuplicateItemCode)
                .WithData("DictionaryCode", dictionaryCode)
                .WithData("Code", duplicateCode.Key);
        }

        // FindAsync 默认 includeDetails: true（模块的 EF 仓储 WithDetails 会带上 Items）
        var dict = await _dataDictionaryRepository.FindAsync(d => d.Code == dictionaryCode);
        if (dict == null)
        {
            throw new BusinessException(AbpAdminDomainErrorCodes.DataDictionaries.DictionaryNotFound)
                .WithData("DictionaryCode", dictionaryCode);
        }

        var inputCodes = input.Items.Select(x => x.Code).ToHashSet(StringComparer.Ordinal);
        var existingCodes = dict.Items.Select(x => x.Code).ToHashSet(StringComparer.Ordinal);

        // 静态字典的项集合由代码定义（种子权威来源）：只许改文本，不许增删/改编码。
        // 不做这个校验的话，"全量替换"语义会在漏传时静默删光静态项
        if (dict.IsStatic && !inputCodes.SetEquals(existingCodes))
        {
            throw new BusinessException(AbpAdminDomainErrorCodes.DataDictionaries.StaticStructureLocked)
                .WithData("DictionaryCode", dictionaryCode);
        }

        // 字典本体显示名/描述
        dict.SetContent(input.DisplayText, input.Description ?? string.Empty);

        // 文本更新：按编码对位 SetContent（模块的 AddOrUpdateItem 对已存在项只改文本，IsStatic 保持不变）
        foreach (var item in input.Items)
        {
            dict.AddOrUpdateItem(item.Code, item.DisplayText, item.Description ?? string.Empty, isStatic: false);
        }

        // 非静态字典才是全量替换语义：删掉没提交的项（与模块 UpdateAsync 同款写法）。
        // 项级 IsStatic 不参与删除（正常只有种子写静态项；直改数据混进非静态字典的静态项
        // 也不允许被全量提交静默删掉——"静态项不可删"是 DTO/UI 承诺的服务端不变量）
        if (!dict.IsStatic)
        {
            dict.Items.RemoveAll(x => !inputCodes.Contains(x.Code) && !x.IsStatic);
        }

        await _dataDictionaryRepository.UpdateAsync(dict);

        // 展示元数据：Order 取数组顺序；孤儿行（被删项的 meta）同一工作单元内清掉。
        // keepItemCodes 用删完后的项集合 = 提交项 ∪ 幸存的静态项，幸存项的元数据原样保留
        var order = 0;
        var saveItems = input.Items
            .Select(item => new DataDictionaryItemMetaSaveItem(item.Code, item.TagType, order++))
            .ToList();
        await _metaManager.SaveForDictionaryAsync(dictionaryCode, saveItems, dict.Items.Select(x => x.Code));

        // 提交后再回读：新插入的 meta 行处于 Added 状态，对 SaveChanges 前的 SQL 查询不可见，
        // 不提交会让返回视图里"本次新增项"的 TagType/Order 是脏值
        await CurrentUnitOfWork.SaveChangesAsync();

        return await GetAsync(dictionaryCode)
            ?? throw new BusinessException(AbpAdminDomainErrorCodes.DataDictionaries.DictionaryNotFound)
                .WithData("DictionaryCode", dictionaryCode);
    }
}
