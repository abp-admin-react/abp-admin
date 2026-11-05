using System;
using System.Collections.Generic;

namespace AbpAdmin.ClientErrors;

/// <summary>
/// 框架基础公开码定义（方案 3.2）。
/// P0 阶段固定命名，不改变运行时行为。
/// </summary>
/// <remarks>
/// <para>
/// 公开码用于页面流程判断，不要求与内部码一一对应。
/// 允许多个内部码映射到同一公开码。
/// </para>
/// <para>
/// 命名规则：Client:{SemanticName}，语义明确且稳定。
/// 客户端新字段统一 lowerCamelCase；管理端既有字段保持兼容。
/// </para>
/// <para>
/// 支持语言：zh-Hans、zh-Hant、en（首期静态客户支持集）。
/// </para>
/// </remarks>
public static class ClientErrorCodes
{
    /// <summary>
    /// 需要登录/会话不可用。
    /// HTTP 401；客户会话协调器负责登录恢复。
    /// </summary>
    public const string AuthenticationRequired = "Client:AuthenticationRequired";

    /// <summary>
    /// 没有权限。
    /// HTTP 403；保留会话，不当成退出登录。
    /// </summary>
    public const string Forbidden = "Client:Forbidden";

    /// <summary>
    /// 输入不满足公开校验规则。
    /// 保持端点校验状态；表单优先接管。
    /// </summary>
    public const string ValidationFailed = "Client:ValidationFailed";

    /// <summary>
    /// 资源不存在或不可见。
    /// 保持端点既有隐藏资源规则；不暴露实体类型和主键。
    /// </summary>
    public const string NotFound = "Client:NotFound";

    /// <summary>
    /// 请求过于频繁。
    /// HTTP 429；保留安全 Retry-After/等待秒数。
    /// </summary>
    public const string RateLimited = "Client:RateLimited";

    /// <summary>
    /// 数据状态已变化/并发冲突。
    /// 保持原状态映射；提示刷新，不暴露数据库约束名。
    /// </summary>
    public const string Conflict = "Client:Conflict";

    /// <summary>
    /// 未公开登记的业务失败。
    /// 保留原业务 HTTP 状态，通用提示。
    /// </summary>
    public const string OperationFailed = "Client:OperationFailed";

    /// <summary>
    /// 未知系统故障。
    /// 按正常异常状态映射返回，通用提示。
    /// </summary>
    public const string Unexpected = "Client:Unexpected";
}

/// <summary>
/// 公开错误分类（CustomerErrorKind）。
/// 客户端用于分类展示和流程分支。
/// </summary>
public enum PublicErrorKind
{
    Business,
    Validation,
    Authentication,
    Forbidden,
    NotFound,
    RateLimited,
    Conflict,
    Server,
}

/// <summary>
/// 基础公开码与 PublicErrorKind 映射（固定，模块不可重新定义）。
/// </summary>
public static class ClientErrorKindMapping
{
    private static readonly Dictionary<string, PublicErrorKind> CodeToKind = new()
    {
        [ClientErrorCodes.AuthenticationRequired] = PublicErrorKind.Authentication,
        [ClientErrorCodes.Forbidden] = PublicErrorKind.Forbidden,
        [ClientErrorCodes.ValidationFailed] = PublicErrorKind.Validation,
        [ClientErrorCodes.NotFound] = PublicErrorKind.NotFound,
        [ClientErrorCodes.RateLimited] = PublicErrorKind.RateLimited,
        [ClientErrorCodes.Conflict] = PublicErrorKind.Conflict,
        [ClientErrorCodes.OperationFailed] = PublicErrorKind.Business,
        [ClientErrorCodes.Unexpected] = PublicErrorKind.Server,
    };

    /// <summary>
    /// 根据公开码获取分类。
    /// </summary>
    public static PublicErrorKind GetKind(string code)
    {
        return CodeToKind.TryGetValue(code, out var kind) ? kind : PublicErrorKind.Business;
    }

    /// <summary>
    /// 验证基础码映射一致性。
    /// 启动时调用，防止模块重复定义基础码 kind。
    /// </summary>
    public static void Validate()
    {
        foreach (var (code, kind) in CodeToKind)
        {
            // 基础码必须存在
            if (string.IsNullOrEmpty(code))
            {
                throw new InvalidOperationException("Client error code cannot be null or empty.");
            }

            // 映射必须明确
            if (kind == default)
            {
                throw new InvalidOperationException($"Client error code '{code}' has invalid kind mapping.");
            }
        }
    }
}

/// <summary>
/// 管理兼容数据白名单。
/// P0 阶段记录已批准字段，P1 实现过滤。
/// </summary>
public static class ManagementCompatibilityDataWhitelist
{
    /// <summary>
    /// 限流错误批准字段。
    /// </summary>
    public static class RateLimiting
    {
        /// <summary>
        /// Retry-After 秒数（管理端 PascalCase）。
        /// </summary>
        public const string RetryAfterSeconds = "RetryAfterSeconds";

        /// <summary>
        /// 所有批准字段。
        /// </summary>
        public static readonly IReadOnlyList<string> ApprovedFields = [RetryAfterSeconds];
    }

    /// <summary>
    /// 图片繁忙错误批准字段。
    /// 无 data 字段，只有 Retry-After 响应头。
    /// </summary>
    public static class ImageProcessingBusy
    {
        public static readonly IReadOnlyList<string> ApprovedFields = [];
    }

    /// <summary>
    /// 文件上传拒绝批准字段。
    /// </summary>
    public static class FileUploadRejection
    {
        public const string FileName = "fileName";
        public const string Reason = "reason";

        public static readonly IReadOnlyList<string> ApprovedFields = [FileName, Reason];
    }
}

/// <summary>
/// 客户端错误字段命名约定（方案 3.1）。
/// </summary>
public static class ClientErrorFieldNames
{
    /// <summary>
    /// 关联 ID（客户端 lowerCamelCase）。
    /// </summary>
    public const string CorrelationId = "correlationId";

    /// <summary>
    /// 等待秒数（客户端 lowerCamelCase）。
    /// 管理端兼容：RetryAfterSeconds。
    /// </summary>
    public const string RetryAfterSeconds = "retryAfterSeconds";

    /// <summary>
    /// 错误码（直接使用公开码）。
    /// </summary>
    public const string Code = "code";

    /// <summary>
    /// 错误消息（安全文案或通用提示）。
    /// </summary>
    public const string Message = "message";

    /// <summary>
    /// 详情（客户端始终 null）。
    /// </summary>
    public const string Details = "details";

    /// <summary>
    /// 数据字典（仅包含批准字段）。
    /// </summary>
    public const string Data = "data";

    /// <summary>
    /// 校验错误列表。
    /// </summary>
    public const string ValidationErrors = "validationErrors";

    /// <summary>
    /// 校验错误成员列表。
    /// </summary>
    public const string Members = "members";
}
