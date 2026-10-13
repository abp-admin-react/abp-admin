using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Threading.Tasks;
using EasyAbp.Abp.DataDictionary;
using Volo.Abp.Application.Dtos;
using Volo.Abp.Application.Services;

namespace AbpAdmin.DataDictionaries;

/// <summary>
/// 字典管理面的唯一 HTTP 入口（T3.4 第 12 步，后经收口扩展为列表/创建/删除）。
/// 前端的 useDictionary / dictionaryValueEnum 统一读这个端点，不直连模块端点：
/// 模块的 DataDictionaryItemDto 只有 Code/DisplayText/Description（没有 isStatic/tagType）。
/// 模块 HttpApi/Application 依赖已整体移除（其控制器路由 api/data-dictionary/data-dictionary
/// 不再暴露），字典的读写全部由本服务承载，静态结构锁等不变量在服务端这里闭合。
/// 路由由 HttpApi 层的显式 Controller 钉死为 /api/app/data-dictionary-view/*
/// （ABP 常规路由只认名为 id 的路径参数，见该 Controller 的注释）。
/// </summary>
public interface IDataDictionaryViewAppService : IApplicationService
{
    /// <summary>字典分页列表（管理页左列；不含 Items）。按 Code 排序。</summary>
    Task<PagedResultDto<DataDictionaryListItemDto>> GetListAsync(PagedResultRequestDto input);

    /// <summary>按字典编码取合并视图（模块字典项 join 我们的元数据）。字典不存在时返回 null。</summary>
    Task<DataDictionaryViewDto?> GetAsync(string code);

    /// <summary>创建非静态字典。静态字典只能由代码定义（种子），API 一律创建 IsStatic = false。</summary>
    Task<DataDictionaryViewDto> CreateAsync(CreateDataDictionaryInput input);

    /// <summary>删除字典（静态字典拒绝；非静态字典的展示元数据行一并清理）。</summary>
    Task DeleteAsync(string code);

    /// <summary>
    /// 原子保存字典显示信息与全部字典项（字典显示名/描述 + 项集合 + 展示元数据）。
    /// 项的 Order 取 Items 数组顺序。Items 是全量替换语义：不在列表里的项会被删除
    /// （静态字典除外——其项集合由代码定义，只许改文本）。
    /// </summary>
    Task<DataDictionaryViewDto> SaveItemsAsync(string dictionaryCode, SaveDataDictionaryItemsInput input);
}

public class DataDictionaryViewDto
{
    public string Code { get; set; } = default!;

    public string DisplayText { get; set; } = default!;

    public string? Description { get; set; }

    /// <summary>静态字典由代码定义：不允许删除、不允许改编码，只允许改显示名。</summary>
    public bool IsStatic { get; set; }

    public List<DataDictionaryItemViewDto> Items { get; set; } = new();
}

public class DataDictionaryItemViewDto
{
    public string Code { get; set; } = default!;

    public string DisplayText { get; set; } = default!;

    public string? Description { get; set; }

    /// <summary>模块 DTO 不带这个字段，但实体上有——静态项在管理页禁止删除、禁止改编码。</summary>
    public bool IsStatic { get; set; }

    /// <summary>antd 标签色标识（DataDictionaryTagTypes），来自 AppDataDictionaryItemMetas。</summary>
    public string? TagType { get; set; }

    /// <summary>显示顺序（枚举字典 = 成员声明顺序）。不要按 Code 排序：数值字符串序下 "10" 排在 "2" 前。</summary>
    public int Order { get; set; }
}

/// <summary>字典列表项（管理页左列；不含 Items）。</summary>
public class DataDictionaryListItemDto
{
    public Guid Id { get; set; }

    public string Code { get; set; } = default!;

    public string DisplayText { get; set; } = default!;

    public string? Description { get; set; }

    /// <summary>静态字典由代码定义：不允许删除、不允许改编码，只允许改显示名。</summary>
    public bool IsStatic { get; set; }
}

/// <summary>创建字典。IsStatic 不开放：静态字典只能由代码定义（种子），API 一律创建非静态字典。</summary>
public class CreateDataDictionaryInput
{
    /// <summary>字典编码。代码与前端消费字典的键，创建后不可改。</summary>
    [Required]
    [StringLength(DataDictionaryConsts.MaxCodeLength)]
    public string Code { get; set; } = default!;

    [Required]
    [StringLength(DataDictionaryConsts.MaxDisplayTextLength)]
    public string DisplayText { get; set; } = default!;

    [StringLength(DataDictionaryConsts.MaxDescriptionLength)]
    public string? Description { get; set; }
}

/// <summary>原子保存字典项的单个条目（SaveItemsAsync 用）。Order 由数组顺序决定，不单独传。</summary>
public class SaveDataDictionaryItemInput
{
    /// <summary>字典项编码；静态项编码不可改（服务端按编码集合校验）。</summary>
    [Required]
    [StringLength(DataDictionaryItemConsts.MaxCodeLength)]
    public string Code { get; set; } = default!;

    [Required]
    [StringLength(DataDictionaryConsts.MaxDisplayTextLength)]
    public string DisplayText { get; set; } = default!;

    [StringLength(DataDictionaryConsts.MaxDescriptionLength)]
    public string? Description { get; set; }

    /// <summary>antd 标签色标识，可选值见 DataDictionaryTagTypes；null 表示无颜色。</summary>
    [StringLength(DataDictionaryItemMetaConsts.MaxTagTypeLength)]
    public string? TagType { get; set; }
}

/// <summary>
/// 原子保存字典显示信息与全部字典项（PUT {dictionaryCode}/items）。
/// Items 是全量替换语义：不在列表里的项会被删除（静态字典除外——其项集合由代码定义，只许改文本）。
/// </summary>
public class SaveDataDictionaryItemsInput : IValidatableObject
{
    /// <summary>
    /// 单次提交的项数上限。字典项的设计量级是几十；不设上限的话，一个持 Update 权限的
    /// 调用方可用超大列表把原子保存的 O(N) 库往返拖成超长事务（校验在整单拒绝一侧，fail-closed）。
    /// </summary>
    public const int MaxItemCount = 500;

    [Required]
    [StringLength(DataDictionaryConsts.MaxDisplayTextLength)]
    public string DisplayText { get; set; } = default!;

    [StringLength(DataDictionaryConsts.MaxDescriptionLength)]
    public string? Description { get; set; }

    [Required]
    public List<SaveDataDictionaryItemInput> Items { get; set; } = new();

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (Items.Count > MaxItemCount)
        {
            yield return new ValidationResult(
                $"字典项数量不能超过 {MaxItemCount}（当前 {Items.Count}）。",
                new[] { nameof(Items) });
        }
    }
}
