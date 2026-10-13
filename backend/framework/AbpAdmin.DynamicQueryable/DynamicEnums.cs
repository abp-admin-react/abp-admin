namespace AbpAdmin.Linq.DynamicQueryable;

/// <summary>条件间的组合方式（与条件声明的 Logic 决定它与前面累计表达式的连接）。</summary>
public enum DynamicLogic
{
    /// <summary>且</summary>
    And = 0,

    /// <summary>或</summary>
    Or = 1,
}

/// <summary>单条件的比较操作符。</summary>
public enum DynamicComparison
{
    /// <summary>等于</summary>
    Equal = 0,

    /// <summary>不等于</summary>
    NotEqual = 1,

    /// <summary>小于</summary>
    LessThan = 2,

    /// <summary>小于等于</summary>
    LessThanOrEqual = 3,

    /// <summary>大于</summary>
    GreaterThan = 4,

    /// <summary>大于等于</summary>
    GreaterThanOrEqual = 5,

    /// <summary>左包含（前缀匹配）</summary>
    StartsWith = 6,

    /// <summary>左不包含（非前缀）</summary>
    NotStartsWith = 7,

    /// <summary>右包含（后缀匹配）</summary>
    EndsWith = 8,

    /// <summary>右不包含（非后缀）</summary>
    NotEndsWith = 9,

    /// <summary>包含</summary>
    Contains = 10,

    /// <summary>不包含</summary>
    NotContains = 11,

    /// <summary>空值</summary>
    Null = 12,

    /// <summary>非空</summary>
    NotNull = 13,
}
