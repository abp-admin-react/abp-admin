using System;
using System.Collections.Generic;
using System.Threading;

namespace AbpAdmin.OperationLogs;

/// <summary>
/// 操作日志模板变量作用域（对标 mzt-biz-log 的 LogRecordContext）。
/// 典型用法：更新类方法体内先加载旧实体，OperationLogScope.Set("oldUser", dto)，
/// 模板再用 {{_diff(oldUser,input)}} 输出属性级变更。
/// <para>
/// 实现要点：AsyncLocal 持有的是「可变字典」且只做原地读写、从不整体替换，
/// 因此方法体内写入的变量对调用方（ActionFilter 的后续渲染）可见——
/// AsyncLocal 的值替换不会反向传播，但对象引用的可变修改会。
/// </para>
/// </summary>
public static class OperationLogScope
{
    private static readonly AsyncLocal<Dictionary<string, object?>?> Current = new();

    /// <summary>确保当前异步流存在变量字典（幂等）。ActionFilter 在方法调用前调用。</summary>
    public static IDictionary<string, object?> Begin()
    {
        return Current.Value ??= new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>注册/覆盖一个模板变量。方法体外调用时无作用域则忽略。</summary>
    public static void Set(string key, object? value)
    {
        // C# 不支持 null 条件索引赋值（dict?[k]=v 非法），先取再判空
        var variables = Current.Value;
        if (variables != null)
        {
            variables[key] = value;
        }
    }

    /// <summary>读取变量（含 ActionFilter 建立的作用域，键名忽略大小写）；无作用域返回 false。</summary>
    public static bool TryGet(string key, out object? value)
    {
        value = null;
        return Current.Value != null && Current.Value.TryGetValue(key, out value);
    }

}
