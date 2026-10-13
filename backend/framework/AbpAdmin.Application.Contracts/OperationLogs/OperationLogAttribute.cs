using System;

namespace AbpAdmin.OperationLogs;

/// <summary>
/// 语义化操作日志注解（对标 mzt-biz-log 的 @LogRecord，精简掉自定义函数与 AOP 全家桶）。
/// 打在应用服务方法上（自动 API 控制器会透传到 MVC Action），由 OperationLogActionFilter
/// 在方法执行完成后渲染模板并落库；后台作业等非 HTTP 流程可手动调用 IOperationLogWriter。
/// <para>
/// 模板语法（区别于 SpEL，刻意保持极简）：
/// <list type="bullet">
/// <item>{{参数名[.属性路径]}} —— 方法参数（含嵌套属性）</item>
/// <item>{{_ret[.属性路径]}} —— 返回值</item>
/// <item>{{_error}} —— 异常消息（仅失败模板）</item>
/// <item>{{_type}} / {{_subType}} —— 注解自身声明的模块/操作名</item>
/// <item>{{_diff(旧值表达式,新值表达式)}} —— 对象属性级差异，如「【名称】a → b；【状态】1 → 2」</item>
/// </list>
/// 方法体内可通过 OperationLogScope.Set 注册额外变量（如加载的旧实体）。
/// </para>
/// </summary>
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class, AllowMultiple = false, Inherited = true)]
public sealed class OperationLogAttribute : Attribute
{
    public OperationLogAttribute(string type, string subType)
    {
        Type = type;
        SubType = subType;
    }

    /// <summary>操作模块，如「身份管理」。类级注解在方法无注解时被整条沿用（含 SubType/模板），不是只补 Type。</summary>
    public string Type { get; }

    /// <summary>操作名，如「锁定用户」。支持模板。</summary>
    public string SubType { get; }

    /// <summary>业务编号模板，如 {{id}}。</summary>
    public string? BizNo { get; init; }

    /// <summary>成功文案模板。</summary>
    public string? Success { get; init; }

    /// <summary>
    /// 失败文案模板。缺省为「{Type}-{SubType} 失败：{_error}」。
    /// </summary>
    public string? Fail { get; init; }
}
