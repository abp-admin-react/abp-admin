using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Threading.Tasks;
using Volo.Abp;
using Volo.Abp.Application.Services;

namespace AbpAdmin.Account;

/// <summary>
/// T2.7 Account Pro 缺口：邮箱与手机号验证、无密码登录、防枚举、双因素、模拟登录、自助注册开关。
/// </summary>
public interface IAccountProAppService : IApplicationService
{
    // ========== 1. 邮箱与手机号验证 ==========

    /// <summary>
    /// 发送邮箱确认验证码。防枚举：对未知/锁定账户返回相同形状响应但不发邮件。
    /// </summary>
    Task SendEmailConfirmationCodeAsync(SendEmailConfirmationCodeInput input);

    /// <summary>
    /// 发送手机号确认验证码。防枚举：对未知/锁定账户返回相同形状响应但不发短信。
    /// </summary>
    Task SendPhoneNumberConfirmationCodeAsync(SendPhoneNumberConfirmationCodeInput input);

    /// <summary>
    /// 校验邮箱确认验证码。成功则标记 EmailConfirmed = true。
    /// </summary>
    Task ConfirmEmailAsync(ConfirmEmailInput input);

    /// <summary>
    /// 校验手机号确认验证码。成功则标记 PhoneNumberConfirmed = true。
    /// </summary>
    Task ConfirmPhoneNumberAsync(ConfirmPhoneNumberInput input);

    // ========== 2. 无密码登录（验证码 / Magic Link） ==========

    /// <summary>
    /// 发送无密码登录凭据（验证码 + Magic Link，或仅其一，由 PasswordlessLoginMode 设置控制）。
    /// 防枚举：对未知/锁定账户返回相同形状响应但不发邮件。
    /// </summary>
    Task SendPasswordlessLoginCodeAsync(SendPasswordlessLoginCodeInput input);

    /// <summary>
    /// 消费邮件中的 Magic Link（或兜底验证码）换取登录令牌。匿名端点，凭据一次性。
    /// </summary>
    Task<ImpersonationResultDto> LoginWithMagicLinkAsync(LoginWithMagicLinkInput input);

    // ========== 3. 双因素认证 ==========

    /// <summary>
    /// 获取当前用户的双因素认证状态。
    /// </summary>
    Task<TwoFactorStatusDto> GetTwoFactorStatusAsync();

    /// <summary>
    /// 启用或禁用当前用户的双因素认证。
    /// </summary>
    Task SetTwoFactorEnabledAsync(SetTwoFactorEnabledInput input);

    /// <summary>
    /// 发送双因素验证码到当前用户已确认的邮箱或手机号（收件人固定为当前登录用户）。
    /// </summary>
    Task SendTwoFactorCodeAsync(SendTwoFactorCodeInput input);

    /// <summary>
    /// 校验当前用户的双因素验证码。
    /// </summary>
    Task VerifyTwoFactorCodeAsync(VerifyTwoFactorCodeInput input);

    // ========== 4. 模拟登录 ==========

    /// <summary>
    /// 以指定租户身份模拟登录（host 管理员专用）。
    /// </summary>
    Task<ImpersonationResultDto> ImpersonateTenantAsync(ImpersonateTenantInput input);

    /// <summary>
    /// 以指定用户身份模拟登录（管理员专用）。
    /// </summary>
    Task<ImpersonationResultDto> ImpersonateUserAsync(ImpersonateUserInput input);

    /// <summary>
    /// 返回原身份。必须提供带 impersonator claim 的当前令牌，否则 403。
    /// </summary>
    Task<ImpersonationResultDto> BackToMyAccountAsync();

    /// <summary>
    /// T4.2：进入有效权限委托（当前用户必须是委托目标）。
    /// </summary>
    Task<ImpersonationResultDto> StartDelegationAsync(Guid delegationId);

    // ========== 5. 自助注册开关 ==========

    /// <summary>
    /// 检查当前是否允许自助注册。由框架设置 AccountSettingNames.IsSelfRegistrationEnabled 控制。
    /// </summary>
    Task<bool> IsSelfRegistrationEnabledAsync();
}

// ========== DTOs ==========

public class SendEmailConfirmationCodeInput
{
    [Required]
    [EmailAddress]
    public string Email { get; set; } = string.Empty;
}

public class SendPhoneNumberConfirmationCodeInput
{
    [Required]
    [Phone]
    public string PhoneNumber { get; set; } = string.Empty;
}

public class ConfirmEmailInput
{
    [Required]
    [EmailAddress]
    public string Email { get; set; } = string.Empty;

    [Required]
    public string Code { get; set; } = string.Empty;
}

public class ConfirmPhoneNumberInput
{
    [Required]
    [Phone]
    public string PhoneNumber { get; set; } = string.Empty;

    [Required]
    public string Code { get; set; } = string.Empty;
}

public class SendPasswordlessLoginCodeInput
{
    [Required]
    [EmailAddress]
    public string Email { get; set; } = string.Empty;
}

public class LoginWithMagicLinkInput
{
    [Required]
    [EmailAddress]
    public string Email { get; set; } = string.Empty;

    /// <summary>邮件链接携带的一次性 magic link token，与 Code 二选一（缺省校验在服务端方法内，
    /// 复用 InvalidMagicLink 错误码，避免 Contracts 层硬编码校验文案绕开本地化）。</summary>
    public string? MagicLinkToken { get; set; }

    /// <summary>邮件正文中的 6 位验证码，与 MagicLinkToken 二选一（链接过期/换设备时的兜底路径）。</summary>
    public string? Code { get; set; }

    /// <summary>链接自带的租户名（host 邮件为空）。服务端据此建立租户上下文，链接自包含。</summary>
    public string? TenantName { get; set; }
}

public class SendTwoFactorCodeInput
{
    /// <summary>
    /// 发送目标：Email 或 Phone。为空时优先 Email，其次 Phone。
    /// 收件人固定为当前登录用户（服务端取 CurrentUser），入参不携带 UserId，防止越权给他人发码。
    /// </summary>
    public string? Provider { get; set; }
}

/// <summary>
/// 双因素状态。注意：EmailConfirmed/PhoneNumberConfirmed 同时是「联系方式确认状态」的唯一
/// 客户端出口（个人中心确认闭环消费）——确认概念暂寄居本 DTO，2FA 端点若收紧权限或收窄字段
/// 会影响确认闭环（前端 abp/account.ts 有对应注释）；拆分归属留待契约轮。
/// </summary>
public class TwoFactorStatusDto
{
    public bool TwoFactorEnabled { get; set; }
    public bool EmailConfirmed { get; set; }
    public bool PhoneNumberConfirmed { get; set; }
}

public class SetTwoFactorEnabledInput
{
    // bool 是不可空值类型，[Required] 永远通过，属无效特性，已移除（见重构报告问题 24）
    public bool Enabled { get; set; }

    /// <summary>
    /// 关闭 2FA 时必填：当前有效的双因素验证码（与 DisableAuthenticatorAsync 验码语义对齐，
    /// 防止会话被劫持后保护被直接关闭）。启用时不需要。
    /// </summary>
    public string? Code { get; set; }

    /// <summary>验证码通道：Email 或 Phone。为空时优先 Email，其次 Phone。</summary>
    public string? Provider { get; set; }
}

public class VerifyTwoFactorCodeInput
{
    [Required]
    public string Code { get; set; } = string.Empty;

    /// <summary>
    /// 发送目标：Email 或 Phone。为空时优先 Email，其次 Phone。
    /// 校验对象固定为当前登录用户（服务端取 CurrentUser），入参不携带 UserId。
    /// </summary>
    public string? Provider { get; set; }
}

public class ImpersonateTenantInput
{
    // Guid 是不可空值类型，[Required] 永远通过，属无效特性，已移除（见重构报告问题 24）
    public Guid TenantId { get; set; }
}

public class ImpersonateUserInput
{
    // Guid 是不可空值类型，[Required] 永远通过，属无效特性，已移除
    public Guid UserId { get; set; }
}

public class ImpersonationResultDto
{
    public string AccessToken { get; set; } = string.Empty;
    public string TokenType { get; set; } = "Bearer";
    public int ExpiresIn { get; set; }
    public string? RefreshToken { get; set; }
}
