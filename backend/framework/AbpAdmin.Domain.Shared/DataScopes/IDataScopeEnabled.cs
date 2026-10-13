namespace AbpAdmin.DataScopes;

/// <summary>
/// 空标记接口，仅作为 <see cref="Volo.Abp.Data.IDataFilter{T}"/> 的类型参数。
///
/// 用法：<c>using (_dataFilter.Disable&lt;IDataScopeEnabled&gt;()) { ... }</c>
/// 临时关闭数据范围过滤，用于种子、后台作业、DbMigrator 等无登录用户场景。
/// </summary>
public interface IDataScopeEnabled
{
}
