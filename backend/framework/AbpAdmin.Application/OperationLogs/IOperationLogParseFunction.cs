using System.Threading.Tasks;

namespace AbpAdmin.OperationLogs;

/// <summary>
/// 操作日志模板解析函数（对标 mzt-biz-log 的 IParseFunction / ruoyi 的 AdminUserParseFunction）：
/// 把模板中的实体 ID 翻译成可读名称，如 {{user(id)}} → zhangsan，避免语义日志里满屏裸 Guid。
/// 实现必须自我兜底：查不到实体返回「未知(前8位)」，任何故障不允许抛异常传染日志链路。
/// </summary>
public interface IOperationLogParseFunction
{
    /// <summary>函数名（模板里的调用名，大小写不敏感；不要与参数名/作用域变量名冲突）。</summary>
    string Name { get; }

    /// <summary>
    /// 解析单个值。入参既可以是单个 Guid/Guid?/Id 字符串，也可以是它们的集合（集合逐个解析后拼接）。
    /// 返回 null 渲染为空串。不允许抛异常。
    /// </summary>
    ValueTask<string?> ResolveAsync(object? value);
}
