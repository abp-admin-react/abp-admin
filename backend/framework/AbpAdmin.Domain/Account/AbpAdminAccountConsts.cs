namespace AbpAdmin.Account;

/// <summary>
/// 账户相关安全/行为参数的集中定义（重构报告问题 22）：
/// 防枚举延迟区间、错误展示上限、锁定年限、恢复码数量、无密码登录令牌有效期边界、默认委托时长。
 /// 散落的魔法数字统一收口到此处，便于调整与文档化。
/// </summary>
public static class AbpAdminAccountConsts
{
    /// <summary>防枚举延迟区间（毫秒），模拟正常发码路径耗时。</summary>
    public const int AntiEnumerationDelayMinMs = 200;

    /// <summary>防枚举延迟区间（毫秒）。</summary>
    public const int AntiEnumerationDelayMaxMs = 500;

    /// <summary>用户导入结果只向前端返回前 N 条错误，完整明细走失败报告文件。</summary>
    public const int ImportErrorDisplayLimit = 50;

    /// <summary>
    /// 用户导入行数硬上限：无上限时一个高压缩比 xlsx（zip 解压放大）可让
    /// MiniExcel 一次性物化数百万行 DTO 造成内存/CPU 峰值（全局 200MB 请求体上限拦不住解压后的行数）。
    /// </summary>
    public const int ImportMaxRowCount = 5000;

    /// <summary>管理员冻结用户时的锁定年限（近似永久）。</summary>
    public const int LockoutDurationYears = 100;

    /// <summary>启用 Authenticator 时生成的恢复码数量。</summary>
    public const int RecoveryCodeCount = 10;

    /// <summary>无密码登录令牌有效期默认值（秒）。</summary>
    public const int PasswordlessTokenLifetimeDefaultSeconds = 90;

    /// <summary>无密码登录令牌有效期下限（秒）。</summary>
    public const int PasswordlessTokenLifetimeMinSeconds = 30;

    /// <summary>无密码登录令牌有效期上限（秒）。</summary>
    public const int PasswordlessTokenLifetimeMaxSeconds = 86400;

    /// <summary>权限委托的默认时长（天），前端委托表单同步使用。</summary>
    public const int DefaultDelegationDays = 7;
}
