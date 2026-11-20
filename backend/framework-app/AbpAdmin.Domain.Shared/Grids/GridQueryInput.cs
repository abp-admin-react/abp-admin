using System.Collections.Generic;

namespace AbpAdmin.Grids;

/// <summary>分页列表查询。列组之间取交集，组内依 Join 组合；Sort 的顺序即排序优先级。</summary>
public sealed class GridQueryInput
{
    public int SkipCount { get; set; }
    public int MaxResultCount { get; set; } = 20;
    public string? Keyword { get; set; }
    public List<GridFilterGroup> Filters { get; set; } = [];
    public List<GridSort> Sort { get; set; } = [];
}

public sealed class GridFilterGroup
{
    public string Field { get; set; } = "";
    public string Join { get; set; } = "and";
    public List<GridCondition> Conditions { get; set; } = [];
}

public sealed class GridCondition
{
    public string Op { get; set; } = "";
    public string Value { get; set; } = "";
    public string? ValueTo { get; set; }
}

public sealed class GridSort
{
    public string Field { get; set; } = "";
    public string Direction { get; set; } = "asc";
}
