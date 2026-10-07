using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace AbpAdmin.Linq.DynamicQueryable;

/// <summary>
/// 单个动态查询条件。与原版（abp-next-admin DynamicParamter）相比的两处修正：
/// 命名拼写（Parameter）；Value 固定为字符串——JSON 绑定下 object 会落成
/// JsonElement 无法直接 Convert.ChangeType，字符串 + 属性真实类型反解更稳。
/// </summary>
public class DynamicCondition
{
    /// <summary>实体属性名（大小写敏感，须与属性完全一致）。</summary>
    [Required]
    public string Field { get; set; } = default!;

    public DynamicLogic Logic { get; set; } = DynamicLogic.And;

    public DynamicComparison Comparison { get; set; } = DynamicComparison.Equal;

    /// <summary>比较值（字符串形态，由表达式构建器按属性类型转换）。</summary>
    public string? Value { get; set; }
}

