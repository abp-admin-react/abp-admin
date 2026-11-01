namespace AbpAdmin.Account;

/// <summary>
/// 图形验证码 DTO（自 Application 层的 CaptchaImageAppService.cs 归位到 Contracts，
/// 供客户端与 HttpApi.Client 复用，见重构报告问题 25）。
/// </summary>
public class CaptchaImageDto
{
    /// <summary>验证码标识，提交时随答案一起回传。</summary>
    public System.Guid Id { get; set; }

    /// <summary>data:image/png;base64,... 形式的图片。</summary>
    public string ImageDataUrl { get; set; } = default!;
}
