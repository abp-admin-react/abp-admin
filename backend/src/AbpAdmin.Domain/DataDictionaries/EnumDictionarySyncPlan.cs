using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using EasyAbp.Abp.DataDictionary;
using Volo.Abp;

namespace AbpAdmin.DataDictionaries;

/// <summary>单个枚举成员的同步计划。</summary>
public class EnumDictionarySyncItemPlan
{
    /// <summary>枚举成员的数值字符串（"0"、"1"）。不用成员名：ABP 的 JSON 输出侧枚举序列化为数值，
    /// 前端 valueEnum 的键必须跟接口返回值对得上（04 T3.4 第 11 步，已拍板）。</summary>
    public string Code { get; set; } = default!;

    /// <summary>成员上的 [Description]，缺省回落成员名（并打 Warning）。</summary>
    public string DisplayText { get; set; } = default!;

    /// <summary>成员名（"Male"），写进字典项的 Description 字段。</summary>
    public string Description { get; set; } = default!;

    /// <summary>成员上的 [DictionaryTag]，未标注或取值非法时为 null。</summary>
    public string? TagType { get; set; }

    /// <summary>成员声明顺序（0、1、2……）。</summary>
    public int Order { get; set; }
}

/// <summary>单个枚举类型的同步计划。</summary>
public class EnumDictionarySyncPlan
{
    public Type EnumType { get; set; } = default!;

    /// <summary>字典编码：类型名去 Enum 后缀。</summary>
    public string DictionaryCode { get; set; } = default!;

    /// <summary>字典显示名：类型级 [Description]，缺省回落字典编码。</summary>
    public string DictionaryDisplayText { get; set; } = default!;

    public List<EnumDictionarySyncItemPlan> Items { get; set; } = new();
}

/// <summary>
/// 枚举 → 数据字典的同步计划构建器（T3.4 第 11 步）。静态方法独立出来是为了单测
/// （Warning、冲突抛异常、Description 回落这些行为不用起数据库就能验）。
/// </summary>
public static class EnumDictionarySyncPlanBuilder
{
    /// <summary>命名约定：只有以该后缀结尾的公开枚举才进字典。</summary>
    public const string EnumTypeSuffix = "Enum";

    /// <summary>
    /// 把候选类型整理成同步计划。
    /// 不以 Enum 结尾的公开枚举：打 Warning（带类型全名）并跳过——方便发现「想进字典却忘了改名」。
    /// 两个不同命名空间下的枚举短名相同（去后缀后字典编码冲突）：直接抛异常中止，不静默覆盖。
    /// </summary>
    public static List<EnumDictionarySyncPlan> BuildPlans(
        IEnumerable<Type> candidateTypes,
        Action<string> logWarning)
    {
        var plans = new List<EnumDictionarySyncPlan>();

        foreach (var type in candidateTypes
                     .Where(t => t.IsEnum && t.IsPublic)
                     .OrderBy(t => t.FullName, StringComparer.Ordinal))
        {
            if (!type.Name.EndsWith(EnumTypeSuffix, StringComparison.Ordinal))
            {
                logWarning($"公开枚举 {type.FullName} 不以 {EnumTypeSuffix} 结尾，跳过字典同步。若它应该进字典，请把类型名加上 {EnumTypeSuffix} 后缀。");
                continue;
            }

            var dictionaryCode = type.Name[..^EnumTypeSuffix.Length];
            var plan = new EnumDictionarySyncPlan
            {
                EnumType = type,
                DictionaryCode = dictionaryCode,
                DictionaryDisplayText = type.GetCustomAttribute<DescriptionAttribute>()?.Description
                                        ?? dictionaryCode
            };

            // GetFields 返回元数据顺序，与 C# 声明顺序一致，作为 Order 取值
            var fields = type.GetFields(BindingFlags.Public | BindingFlags.Static);
            var order = 0;
            foreach (var field in fields)
            {
                var memberName = field.Name;
                var code = Convert.ToInt64(field.GetRawConstantValue(), CultureInfo.InvariantCulture)
                    .ToString(CultureInfo.InvariantCulture);

                var displayText = field.GetCustomAttribute<DescriptionAttribute>()?.Description;
                if (displayText.IsNullOrWhiteSpace())
                {
                    logWarning($"枚举成员 {type.FullName}.{memberName} 没有 [Description]，字典显示名回落为成员名。");
                    displayText = memberName;
                }

                if (displayText.Length > DataDictionaryItemConsts.MaxDisplayTextLength)
                {
                    logWarning($"枚举成员 {type.FullName}.{memberName} 的 [Description] 超过 {DataDictionaryItemConsts.MaxDisplayTextLength} 字符，已截断。");
                    displayText = displayText[..DataDictionaryItemConsts.MaxDisplayTextLength];
                }

                var tagType = field.GetCustomAttribute<DictionaryTagAttribute>()?.TagType;
                if (!DataDictionaryTagTypes.IsValid(tagType))
                {
                    logWarning($"枚举成员 {type.FullName}.{memberName} 的 [DictionaryTag] 取值 \"{tagType}\" 不在 DataDictionaryTagTypes 内，已忽略。");
                    tagType = null;
                }

                plan.Items.Add(new EnumDictionarySyncItemPlan
                {
                    Code = code,
                    DisplayText = displayText,
                    Description = memberName,
                    TagType = tagType,
                    Order = order++
                });
            }

            plans.Add(plan);
        }

        var duplicate = plans.GroupBy(p => p.DictionaryCode).FirstOrDefault(g => g.Count() > 1);
        if (duplicate != null)
        {
            throw new AbpException(
                $"枚举同步到数据字典时发现字典编码冲突：{duplicate.Key} " +
                $"（{string.Join(", ", duplicate.Select(p => p.EnumType.FullName))}）。请重命名其中一个枚举类型。");
        }

        return plans;
    }

    /// <summary>
    /// 字典 Id 的稳定哈希。规格原文是 MD5(type.FullName)，这里把租户一起哈希：
    /// 模块没有租户回落，字典必须逐租户各写一份，同一枚举在 host 与每个租户下各有一行——
    /// Id 不含租户的话，第二次同步就撞主键。加入租户后仍满足「多次同步、多环境之间 Id 一致」。
    /// </summary>
    public static Guid CreateStableDictionaryId(Guid? tenantId, string enumTypeFullName)
    {
        var bytes = MD5.HashData(Encoding.UTF8.GetBytes(
            $"{enumTypeFullName}@{(tenantId?.ToString("N") ?? "host")}"));
        return new Guid(bytes);
    }
}
