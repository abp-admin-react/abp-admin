using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace AbpAdmin.OperationLogs;

/// <summary>
/// 操作日志模板渲染（对标 mzt-biz-log 的 SpEL 模板，刻意不用 C# 表达式编译：
/// 语义日志模板由开发者在注解里声明，表达式树会引入编译开销与任意代码执行面）。
/// <para>
/// 支持：{{参数名.属性路径}}、{{_ret.属性路径}}、{{_error}}、{{_type}}、{{_subType}}、
/// {{_diff(旧,新)}}、{{函数名(参数路径)}}（如 {{user(id)}}，需传入解析函数表），
/// 以及方法体内通过 OperationLogScope.Set 注册的变量。
/// 根名解析不到的 token 原样保留（模板拼写错误可见）；解析到但值为 null 渲染为空串。
/// </para>
/// </summary>
public static partial class OperationLogTemplates
{
    /// <summary>根名解析失败的哨兵：区别于「解析成功但值为 null」。</summary>
    private static readonly object UnresolvedMarker = new();

    // 非贪婪以支持 _diff{a,b} 的内层花括号（组捕获到最外层 }} 之前的内容）
    [GeneratedRegex(@"\{\{\s*(.*?)\s*\}\}")]
    private static partial Regex TokenRegex();

    /// <summary>
    /// 渲染模板。
    /// </summary>
    /// <param name="template">模板字符串；null 返回 null。</param>
    /// <param name="arguments">方法参数名 → 值（ActionFilter 从 ActionArguments 提供）。</param>
    /// <param name="functions">解析函数表（函数名 → 实现）；null 表示无函数能力，函数 token 原样保留。</param>
    /// <param name="returnValue">方法返回值（_ret）。</param>
    /// <param name="errorMessage">异常消息（_error）。</param>
    /// <param name="type">注解声明的模块名（_type）。</param>
    /// <param name="subType">注解声明的操作名（_subType）。</param>
    public static async Task<string?> RenderAsync(
        string? template,
        IReadOnlyDictionary<string, object?> arguments,
        IReadOnlyDictionary<string, IOperationLogParseFunction>? functions = null,
        object? returnValue = null,
        string? errorMessage = null,
        string? type = null,
        string? subType = null)
    {
        if (template == null)
        {
            return null;
        }

        // 函数 token 要求异步解析，Regex.Replace 的同步 match evaluator 用不了，手工拼接
        var result = new StringBuilder(template.Length);
        var lastCopied = 0;
        foreach (var match in TokenRegex().Matches(template).Cast<Match>())
        {
            result.Append(template, lastCopied, match.Index - lastCopied);
            result.Append(await EvaluateAsync(
                match.Groups[1].Value,
                match.Value,
                arguments,
                functions,
                returnValue,
                errorMessage,
                type,
                subType));
            lastCopied = match.Index + match.Length;
        }

        result.Append(template, lastCopied, template.Length - lastCopied);
        return result.ToString();
    }

    private static async Task<string> EvaluateAsync(
        string token,
        string rawToken,
        IReadOnlyDictionary<string, object?> arguments,
        IReadOnlyDictionary<string, IOperationLogParseFunction>? functions,
        object? returnValue,
        string? errorMessage,
        string? type,
        string? subType)
    {
        // 解析函数 token：name(argPath)。函数名在注册表命中才分派（大小写不敏感），
        // 未命中时继续走普通路径解析（含 _diff），避免吞掉含括号的普通 token。
        if (functions != null && token.EndsWith(")", StringComparison.Ordinal) && token.Contains('('))
        {
            var openIndex = token.IndexOf('(');
            var functionName = token[..openIndex].Trim();
            if (TryFindFunction(functions, functionName, out var function))
            {
                var argumentPath = token[(openIndex + 1)..^1].Trim();
                var argumentValue = ResolvePath(argumentPath, arguments, returnValue);
                if (argumentValue == UnresolvedMarker)
                {
                    return rawToken;
                }

                try
                {
                    // 解析函数实现承诺不抛（自身兜底）；此处是最后防线——
                    // 单个函数故障只保留原 token，不影响模板其余部分。
                    return await function.ResolveAsync(argumentValue) ?? "";
                }
                catch
                {
                    return rawToken;
                }
            }
        }

        // 差异 token：_diff(a,b)。用圆括号避免与模板外层 {{}} 的花括号冲突
        if (token.StartsWith("_diff(", StringComparison.OrdinalIgnoreCase) && token.EndsWith(")", StringComparison.Ordinal))
        {
            var inner = token["_diff(".Length..^1];
            var comma = inner.IndexOf(',');
            if (comma > 0)
            {
                var oldValue = ResolvePath(inner[..comma].Trim(), arguments, returnValue);
                var newValue = ResolvePath(inner[(comma + 1)..].Trim(), arguments, returnValue);
                if (oldValue == UnresolvedMarker || newValue == UnresolvedMarker)
                {
                    return rawToken;
                }

                return OperationLogDiffer.Diff(oldValue, newValue) ?? "（无变化）";
            }
        }

        object? value;
        if (string.Equals(token, "_error", StringComparison.OrdinalIgnoreCase))
        {
            value = errorMessage;
        }
        else if (string.Equals(token, "_type", StringComparison.OrdinalIgnoreCase))
        {
            value = type;
        }
        else if (string.Equals(token, "_subType", StringComparison.OrdinalIgnoreCase))
        {
            value = subType;
        }
        else
        {
            value = ResolvePath(token, arguments, returnValue);
            if (value == UnresolvedMarker)
            {
                return rawToken;
            }
        }

        return OperationLogDiffer.FormatValue(value) ?? "";
    }

    /// <summary>
    /// 函数名大小写不敏感匹配——与根名解析同一契约：注册表的 comparer 属于装配方，
    /// 引擎自身必须保证 {{User(id)}} 与 {{user(id)}} 等价。
    /// </summary>
    private static bool TryFindFunction(
        IReadOnlyDictionary<string, IOperationLogParseFunction> functions,
        string name,
        out IOperationLogParseFunction function)
    {
        foreach (var pair in functions)
        {
            if (string.Equals(pair.Key, name, StringComparison.OrdinalIgnoreCase))
            {
                function = pair.Value;
                return true;
            }
        }

        function = null!;
        return false;
    }

    /// <summary>
    /// 根名大小写不敏感是模板语法的公共契约（{{Id}} 与 {{id}} 等价），
    /// 不能依赖调用方字典的 comparer——ActionArguments 与任意手动构造的字典都是默认 comparer。
    /// </summary>
    private static bool TryFindKeyInsensitive(IReadOnlyDictionary<string, object?> source, string key, out object? value)
    {
        foreach (var pair in source)
        {
            if (string.Equals(pair.Key, key, StringComparison.OrdinalIgnoreCase))
            {
                value = pair.Value;
                return true;
            }
        }

        value = null;
        return false;
    }

    /// <summary>
    /// 解析点分路径：根名（参数名/作用域变量，大小写不敏感）+ 逐级属性。
    /// 根名解析不到返回 <see cref="UnresolvedMarker"/>；路径中段解析不到视为 null。
    /// </summary>
    private static object? ResolvePath(string path, IReadOnlyDictionary<string, object?> arguments, object? returnValue)
    {
        var segments = path.Split('.');
        var root = segments[0];

        object? current;
        if (string.Equals(root, "_ret", StringComparison.OrdinalIgnoreCase))
        {
            current = returnValue;
        }
        else if (TryFindKeyInsensitive(arguments, root, out var argValue))
        {
            current = argValue;
        }
        else if (OperationLogScope.TryGet(root, out var scopeValue))
        {
            current = scopeValue;
        }
        else
        {
            return UnresolvedMarker;
        }

        foreach (var segment in segments.Skip(1))
        {
            if (current == null)
            {
                return null;
            }

            var property = current.GetType().GetProperty(
                segment,
                BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
            current = property?.GetValue(current);
        }

        return current;
    }
}
