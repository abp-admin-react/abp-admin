using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Threading.Tasks;
using AbpAdmin.Desensitization;
using AbpAdmin.Linq.DynamicQueryable;
using Volo.Abp.Application.Dtos;
using Volo.Abp.Application.Services;

namespace AbpAdmin.Identity;

/// <summary>
/// 用户动态搜索（dynamic-queryable 的落地示例与公共能力，借鉴 abp-next-admin 的
/// DynamicQueryableAppService 思路）：前端传「字段+操作符+值」条件组，后端翻译成
/// 表达式树在数据库侧过滤。字段必须来自 GetAvailableFieldsAsync 的白名单，
/// 白名单外字段在构建表达式前即被拒绝（fail-closed，字段名无法注入）。
/// </summary>
public interface IIdentityUserSearchAppService : IApplicationService
{
    /// <summary>
    /// 可用字段元数据（字段名/类型/可用操作符），前端据此动态渲染筛选器。
    /// </summary>
    Task<List<DynamicSearchFieldDto>> GetAvailableFieldsAsync();

    /// <summary>按动态条件分页搜索用户。空条件组返回全部（分页内）。</summary>
    Task<PagedResultDto<IdentityUserSearchItemDto>> SearchAsync(IdentityUserDynamicSearchInput input);
}

/// <summary>动态搜索请求：条件组 + 分页 + 排序（白名单校验）。</summary>
public class IdentityUserDynamicSearchInput : PagedResultRequestDto
{
    [MaxLength(32)]
    public List<DynamicCondition> Conditions { get; set; } = new();

    /// <summary>排序串（Dynamic LINQ，白名单见实现 SortingWhitelist）。</summary>
    public string? Sorting { get; set; }
}

/// <summary>可用字段元数据。</summary>
public class DynamicSearchFieldDto
{
    /// <summary>实体属性名（与 DynamicCondition.Field 完全一致，大小写敏感）。</summary>
    public string Field { get; set; } = default!;

    /// <summary>展示名（本地化键由前端按字段名映射）。</summary>
    public string DisplayName { get; set; } = default!;

    /// <summary>值的 JS 类型提示：string / number / boolean / date。</summary>
    public string ValueType { get; set; } = default!;

    /// <summary>该字段可用的操作符集合。</summary>
    public List<DynamicComparison> Comparisons { get; set; } = new();
}

/// <summary>搜索结果行（轻量视图，避免整个 IdentityUserDto 的字段面）。</summary>
public class IdentityUserSearchItemDto
{
    public Guid Id { get; set; }

    public string UserName { get; set; } = default!;

    /// <summary>与 IdentityUserDto 同口径脱敏：明文需 Identity.Users.Update（防新读面旁路字段权限）。</summary>
    [Masked(MaskKindEnum.Email, PlaintextPermission = Volo.Abp.Identity.IdentityPermissions.Users.Update)]
    public string Email { get; set; } = default!;

    public string? Name { get; set; }

    public string? Surname { get; set; }

    [Masked(MaskKindEnum.Mobile, PlaintextPermission = Volo.Abp.Identity.IdentityPermissions.Users.Update)]
    public string? PhoneNumber { get; set; }

    public bool IsActive { get; set; }

    public bool EmailConfirmed { get; set; }

    public DateTime CreationTime { get; set; }
}
