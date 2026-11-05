using System.Collections.Generic;

namespace AbpAdmin.ClientErrors;

/// <summary>
/// 只读公开错误目录（方案 5.2）。P1 契约。
/// </summary>
/// <remarks>
/// <para>
/// 启动校验完成后冻结，运行期只读。冻结语义与 <see cref="AdminErrorEndpointRegistry.Freeze"/>
/// 一致：冻结后任何写入尝试都必须失败，并发读安全。
/// </para>
/// <para>
/// 消费者：P1 Domain 非 HTTP 投影核心（按内部码查定义）、P1-HTTP 出口投影、P3 快照导出。
/// </para>
/// </remarks>
public interface IPublicErrorCatalog
{
    /// <summary>
    /// 冻结后的全部定义（只读视图）。
    /// </summary>
    IReadOnlyCollection<PublicErrorDefinition> Definitions { get; }

    /// <summary>
    /// 按内部查找键精确匹配（Ordinal 大小写敏感，与 ABP 错误码语义一致）。
    /// </summary>
    PublicErrorDefinition? FindByInternalCode(string internalCode);

    /// <summary>
    /// 按公开码精确匹配（Ordinal 大小写敏感）。
    /// </summary>
    PublicErrorDefinition? FindByPublicCode(string publicCode);
}
