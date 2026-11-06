using System.Collections.Generic;

namespace AbpAdmin.ClientErrors;

/// <summary>
/// 公开错误定义 Provider 契约（方案 5.2）。
/// </summary>
/// <remarks>
/// <para>
/// 模块以 Provider 方式注册定义，模块移除即移除自身声明，框架不引用业务实现。
/// </para>
/// <para>
/// 实现约束：
/// </para>
/// <list type="bullet">
///   <item><description>必须是纯内存声明：不访问数据库、Redis 或外部服务（方案 5.2 启动校验约定）。</description></item>
///   <item><description>每次调用返回相同的定义集合（Domain 注册表在启动时只读取一次并冻结）。</description></item>
///   <item><description>不得重新定义框架基础码（<see cref="ClientErrorCodes"/>）的 kind。</description></item>
/// </list>
/// <para>
/// 消费者：Domain 的 PublicErrorDefinitionRegistry（聚合 + 启动校验）、
/// P3 公开机器契约快照导出（仅导出公开码/分类/公开字段/公开 HTTP 组合，不含内部映射与文案）。
/// </para>
/// </remarks>
public interface IPublicErrorDefinitionProvider
{
    /// <summary>
    /// 返回本模块声明的全部公开错误定义。
    /// </summary>
    IEnumerable<PublicErrorDefinition> GetDefinitions();
}
