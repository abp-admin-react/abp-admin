using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Dynamic.Core;
using System.Threading.Tasks;
using AbpAdmin.Linq.DynamicQueryable;
using AbpAdmin.Permissions;
using Microsoft.AspNetCore.Authorization;
using Volo.Abp;
using Volo.Abp.Application.Dtos;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Identity;
using Volo.Abp.Validation;

namespace AbpAdmin.Identity;

/// <summary>
/// 用户动态搜索（IIdentityUserSearchAppService 实现）。
/// 授权复用 Volo Identity 的 Users.Default（能看到用户列表的人就能用同一批字段做深度筛选）。
/// 字段白名单是唯一入口：条件里的 Field 必须命中 GetAvailableFields 的清单，
/// 否则在构建表达式前直接拒绝——PropertyOrField 虽然天然防注入，但白名单还保证了
/// 错误提示的友好（ArgumentException → 字段级 400）与排序字段的可枚举。
/// </summary>
[Authorize(IdentityPermissions.Users.Default)]
public class IdentityUserSearchAppService : AbpAdminAppService, IIdentityUserSearchAppService
{
    /// <summary>排序白名单：Sorting 直达 Dynamic LINQ OrderBy 前必须过校验（仓库既有约定）。</summary>
    private static readonly string[] SortableFields =
    [
        nameof(IdentityUser.UserName),
        nameof(IdentityUser.Email),
        nameof(IdentityUser.Name),
        nameof(IdentityUser.CreationTime),
    ];

    /// <summary>文本类字段共用操作符集（先于 AvailableFields 声明：静态初始化按代码顺序执行）。</summary>
    private static readonly List<DynamicComparison> TextComparisons = new()
    {
        DynamicComparison.Equal,
        DynamicComparison.NotEqual,
        DynamicComparison.Contains,
        DynamicComparison.NotContains,
        DynamicComparison.StartsWith,
        DynamicComparison.EndsWith,
        DynamicComparison.Null,
        DynamicComparison.NotNull,
    };

    /// <summary>字段白名单（含类型与操作符元数据）。新增可筛字段只改这一处。</summary>
    private static readonly List<DynamicSearchFieldDto> AvailableFields = new()
    {
        new()
        {
            Field = nameof(IdentityUser.UserName), DisplayName = "UserName", ValueType = "string",
            Comparisons = TextComparisons,
        },
        new()
        {
            Field = nameof(IdentityUser.Email), DisplayName = "Email", ValueType = "string",
            Comparisons = TextComparisons,
        },
        new()
        {
            Field = nameof(IdentityUser.Name), DisplayName = "Name", ValueType = "string",
            Comparisons = TextComparisons,
        },
        new()
        {
            Field = nameof(IdentityUser.Surname), DisplayName = "Surname", ValueType = "string",
            Comparisons = TextComparisons,
        },
        new()
        {
            Field = nameof(IdentityUser.PhoneNumber), DisplayName = "PhoneNumber", ValueType = "string",
            Comparisons = TextComparisons,
        },
        new()
        {
            Field = nameof(IdentityUser.IsActive), DisplayName = "IsActive", ValueType = "boolean",
            Comparisons = new List<DynamicComparison> { DynamicComparison.Equal, DynamicComparison.NotEqual },
        },
        new()
        {
            Field = nameof(IdentityUser.EmailConfirmed), DisplayName = "EmailConfirmed", ValueType = "boolean",
            Comparisons = new List<DynamicComparison> { DynamicComparison.Equal, DynamicComparison.NotEqual },
        },
        new()
        {
            Field = nameof(IdentityUser.CreationTime), DisplayName = "CreationTime", ValueType = "date",
            Comparisons = new List<DynamicComparison>
            {
                DynamicComparison.Equal, DynamicComparison.NotEqual,
                DynamicComparison.LessThan, DynamicComparison.LessThanOrEqual,
                DynamicComparison.GreaterThan, DynamicComparison.GreaterThanOrEqual,
            },
        },
    };

    /// <summary>条件组数量上限（契约出处 IIdentityUserDynamicSearchInput.Conditions 注释）。</summary>
    private const int MaxConditions = 32;

    private readonly IRepository<IdentityUser, Guid> _userRepository;

    public IdentityUserSearchAppService(IRepository<IdentityUser, Guid> userRepository)
    {
        _userRepository = userRepository;
    }

    public virtual Task<List<DynamicSearchFieldDto>> GetAvailableFieldsAsync()
    {
        // 静态清单直接拷贝返回，避免调用方改到共享实例
        return Task.FromResult(AvailableFields.Select(f => new DynamicSearchFieldDto
        {
            Field = f.Field,
            DisplayName = f.DisplayName,
            ValueType = f.ValueType,
            Comparisons = new List<DynamicComparison>(f.Comparisons),
        }).ToList());
    }

    public virtual async Task<PagedResultDto<IdentityUserSearchItemDto>> SearchAsync(
        IdentityUserDynamicSearchInput input)
    {
        // 条件数上限：DataAnnotations MaxLength 对 List 不生效，这里显式执行（契约值 32）
        if (input.Conditions.Count > MaxConditions)
        {
            throw new AbpValidationException(
                L["AbpAdmin:TooManySearchConditions", input.Conditions.Count, MaxConditions]);
        }

        // 字段白名单 + 每字段操作符集校验（先于表达式构建，非法组合转 400 而非 500）
        var fieldMap = AvailableFields.ToDictionary(f => f.Field);
        foreach (var condition in input.Conditions)
        {
            if (!fieldMap.TryGetValue(condition.Field, out var fieldMeta))
            {
                throw new UserFriendlyException(L["AbpAdmin:InvalidSearchField", condition.Field]);
            }

            if (!fieldMeta.Comparisons.Contains(condition.Comparison))
            {
                throw new UserFriendlyException(
                    L["AbpAdmin:InvalidSearchComparison", condition.Field, condition.Comparison.ToString()]);
            }
        }

        if (!SortingWhitelist.IsValid(input.Sorting, SortableFields))
        {
            throw new AbpValidationException(
                L["AbpAdmin:InvalidSorting", input.Sorting ?? string.Empty]);
        }

        var queryable = await _userRepository.GetQueryableAsync();

        // 表达式构建（含值→类型转换）包在业务错里：字段/操作符白名单已过，
        // 剩余失败只会是值不可转（"garbage" 转 int 等），转 400 而非 500。
        // 白名单同时传给 DynamicQuery（库内 fail-closed 复核，安全契约不依赖本调用方自觉）
        IQueryable<IdentityUser> filtered;
        try
        {
            filtered = queryable.DynamicQuery(input.Conditions, fieldMap.Keys);
        }
        catch (Exception ex) when (ex is not UserFriendlyException
            && (ex is FormatException or InvalidOperationException or ArgumentException or OverflowException))
        {
            throw new UserFriendlyException(L["AbpAdmin:InvalidSearchCondition", ex.Message]);
        }

        // 计数在无序查询上做（部分提供程序不会消掉带排序查询的 ORDER BY，白做一次排序）
        var totalCount = await AsyncExecuter.CountAsync(filtered);
        var ordered = string.IsNullOrWhiteSpace(input.Sorting)
            ? filtered.OrderByDescending(u => u.CreationTime)
            : filtered.OrderBy(input.Sorting);
        var items = await AsyncExecuter.ToListAsync(
            ordered
                .Skip(input.SkipCount)
                .Take(input.MaxResultCount));

        return new PagedResultDto<IdentityUserSearchItemDto>(
            totalCount,
            items.Select(u => new IdentityUserSearchItemDto
            {
                Id = u.Id,
                UserName = u.UserName,
                Email = u.Email,
                Name = u.Name,
                Surname = u.Surname,
                PhoneNumber = u.PhoneNumber,
                IsActive = u.IsActive,
                EmailConfirmed = u.EmailConfirmed,
                CreationTime = u.CreationTime,
            }).ToList());
    }
}
