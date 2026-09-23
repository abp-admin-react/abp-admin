using System;
using AbpAdmin.RateLimiting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace AbpAdmin.ModuleConfigurators;

/// <summary>
/// 操作限流策略与服务配置（自 AbpAdminDomainModule 拆出）。
/// 问题7 修复：策略参数改由 <see cref="OperationRateLimitingSettings"/> 强类型绑定
/// （默认值集中在该类），appsettings.json 显式列出全部键，消除代码/json 双轨默认值漂移。
/// </summary>
internal static class RateLimitingConfigurator
{
    public static void ConfigureOperationRateLimiting(this IServiceCollection services, IConfiguration configuration)
    {
        var settings = configuration.GetSection(OperationRateLimitingSettings.SectionName)
                                    .Get<OperationRateLimitingSettings>() ?? new OperationRateLimitingSettings();

        // T2.5: 操作限流配置
        services.Configure<AbpAdminOperationRateLimitingOptions>(options =>
        {
            // 全局开关改由 "OperationRateLimiting:IsEnabled" 配置节绑定（默认 true）——
            // 原先硬编码 true，注释却声称可在 Host 层覆盖，属失实注释。
            // 开发环境想关掉时在 appsettings.Development.json 里置 "OperationRateLimiting": { "IsEnabled": false }。
            options.IsEnabled = settings.IsEnabled;

            // 登录策略：按 IP 15 分钟 20 次 + 按邮箱 15 分钟 5 次。
            // round3 接线：消费者在 Login 页面（AbpAdminLoginModel.OnPostAsync 密码分支直调 Checker），
            // 策略名引用常量防漂移。
            options.AddPolicy(OperationRateLimitingPolicyNames.Login, policy => policy
                .AddRule(r => r.PartitionByClientIp()
                    .WithFixedWindow(TimeSpan.FromMinutes(settings.Login.IpDurationMinutes), settings.Login.IpMaxCount)
                    .WithName("LoginByIp"))
                .AddRule(r => r.PartitionByEmail()
                    .WithFixedWindow(TimeSpan.FromMinutes(settings.Login.EmailDurationMinutes), settings.Login.EmailMaxCount)
                    .WithName("LoginByEmail")));

            // 短信验证码策略：按手机号 1 小时 3 次（租户隔离）
            options.AddPolicy(OperationRateLimitingPolicyNames.SmsVerificationCode, policy => policy
                .AddRule(r => r.PartitionByPhoneNumber()
                    .WithFixedWindow(TimeSpan.FromHours(settings.SmsVerificationCode.DurationHours), settings.SmsVerificationCode.MaxCount)
                    .WithMultiTenancy()));

            // 短信通知策略（NotificationService fan-out 等运营短信）：与验证码分开限流，
            // 未打 Purpose=VerificationCode 标记的消息都走这里（见 SmsSenderResolver / SmsRateLimitingPolicies）
            options.AddPolicy(OperationRateLimitingPolicyNames.SmsNotification, policy => policy
                .AddRule(r => r.PartitionByPhoneNumber()
                    .WithFixedWindow(TimeSpan.FromHours(settings.SmsNotification.DurationHours), settings.SmsNotification.MaxCount)
                    .WithMultiTenancy()));

            // T2.7 邮件验证码（邮箱确认/无密码登录）发码侧：按邮箱 1 小时 5 次（租户隔离）。
            // round3 补 IP 并联规则：原先只按邮箱分区，攻击者换 IP 即可定向烧光受害者发码配额、
            // 锁死其 OTP 接收路径；IP 规则（与 Login 策略同构）限制单 IP 在窗口内的总发码请求数，
            // 阈值放宽到 30 是给共享出口 NAT 的正常用户留余量。
            options.AddPolicy(OperationRateLimitingPolicyNames.EmailVerificationCode, policy => policy
                .AddRule(r => r.PartitionByEmail()
                    .WithFixedWindow(TimeSpan.FromHours(settings.EmailVerificationCode.DurationHours), settings.EmailVerificationCode.MaxCount)
                    .WithMultiTenancy())
                .AddRule(r => r.PartitionByClientIp()
                    .WithFixedWindow(TimeSpan.FromHours(settings.EmailVerificationCode.DurationHours), settings.EmailVerificationCode.IpMaxCount)
                    .WithName("EmailVerificationCodeByIp")));

            // T2.7 校验侧独立策略（防暴力猜码）：按邮箱/手机号 10 分钟 5 次。
            // 与发码侧分开，发新码只重置校验侧，避免「重发即重置发码限额」导致发码限流失效。
            // round3 各补 IP 并联规则（动机同上：单靠目标分区可被换 IP 定向烧配额锁死受害者验码路径）。
            options.AddPolicy(OperationRateLimitingPolicyNames.EmailVerificationCodeVerify, policy => policy
                .AddRule(r => r.PartitionByEmail()
                    .WithFixedWindow(TimeSpan.FromMinutes(settings.VerificationCodeVerify.DurationMinutes), settings.VerificationCodeVerify.MaxCount)
                    .WithMultiTenancy())
                .AddRule(r => r.PartitionByClientIp()
                    .WithFixedWindow(TimeSpan.FromMinutes(settings.VerificationCodeVerify.DurationMinutes), settings.VerificationCodeVerify.IpMaxCount)
                    .WithName("EmailVerificationCodeVerifyByIp")));
            options.AddPolicy(OperationRateLimitingPolicyNames.SmsVerificationCodeVerify, policy => policy
                .AddRule(r => r.PartitionByPhoneNumber()
                    .WithFixedWindow(TimeSpan.FromMinutes(settings.VerificationCodeVerify.DurationMinutes), settings.VerificationCodeVerify.MaxCount)
                    .WithMultiTenancy())
                .AddRule(r => r.PartitionByClientIp()
                    .WithFixedWindow(TimeSpan.FromMinutes(settings.VerificationCodeVerify.DurationMinutes), settings.VerificationCodeVerify.IpMaxCount)
                    .WithName("SmsVerificationCodeVerifyByIp")));

            // T2.7 双因素验证码：发码侧与校验侧（分区值是用户 Id）
            options.AddPolicy(OperationRateLimitingPolicyNames.TwoFactorCode, policy => policy
                .AddRule(r => r.PartitionByParameter()
                    .WithFixedWindow(TimeSpan.FromMinutes(settings.TwoFactorCode.DurationMinutes), settings.TwoFactorCode.MaxCount)
                    .WithMultiTenancy()));
            options.AddPolicy(OperationRateLimitingPolicyNames.TwoFactorCodeVerify, policy => policy
                .AddRule(r => r.PartitionByParameter()
                    .WithFixedWindow(TimeSpan.FromMinutes(settings.VerificationCodeVerify.DurationMinutes), settings.VerificationCodeVerify.MaxCount)
                    .WithMultiTenancy()));

            // 审计日志导出策略：按当前用户 1 天 10 次（租户隔离）。
            // round3 接线：异步路径打在 AuditLogAppService.EnqueueExportAsync 特性上，
            // 同步路径（AuditLogExportController.ExportAsync）由 Controller 直调 Checker。
            options.AddPolicy(OperationRateLimitingPolicyNames.AuditLogExport, policy => policy
                .AddRule(r => r.PartitionByCurrentUser()
                    .WithFixedWindow(TimeSpan.FromDays(settings.AuditLogExport.DurationDays), settings.AuditLogExport.MaxCount)
                    .WithMultiTenancy()));

            // T4.6 匿名分享下载策略：按 IP 放宽限流（默认 1 分钟 30 次）。token 是 256-bit 随机无法枚举，
            // 防的是脚本高频打有效 token 消耗下载次数/打满带宽；阈值放宽是给浏览器多 range 请求留余量。
            // 不做租户隔离：匿名请求进下载前租户上下文尚未确定。
            options.AddPolicy(OperationRateLimitingPolicyNames.FileShareDownload, policy => policy
                .AddRule(r => r.PartitionByClientIp()
                    .WithFixedWindow(TimeSpan.FromMinutes(settings.FileShareDownload.IpDurationMinutes), settings.FileShareDownload.IpMaxCount)
                    .WithName("FileShareDownloadByIp")));

            // GDPR 匿名个人数据下载策略（与 FileShareDownload 同型）：按 IP 默认 1 分钟 10 次。
            // token 同为 256-bit 加密安全随机、且是单次消费凭据，防的不是枚举而是
            // 脚本高频消耗下载 token/带宽与 ZIP 打包开销；匿名请求租户未定，不做租户隔离。
            options.AddPolicy(OperationRateLimitingPolicyNames.GdprDownload, policy => policy
                .AddRule(r => r.PartitionByClientIp()
                    .WithFixedWindow(TimeSpan.FromMinutes(settings.GdprDownload.IpDurationMinutes), settings.GdprDownload.IpMaxCount)
                    .WithName("GdprDownloadByIp")));

            // round4：匿名图形验证码取图策略：按 IP 默认 5 分钟 60 次。此前该端点完全无限流——
            // 每次调用都做一次 Skia 渲染 + 写一条 5 分钟 TTL 的分布式缓存条目，匿名可刷 CPU 与
            // 共享缓存（开 Redis 时放大为网络+内存）。阈值放宽：登录页每次加载取一张图，
            // 60/5min 足够共享出口 NAT 的正常用户；匿名请求租户未定，不做租户隔离。
            options.AddPolicy(OperationRateLimitingPolicyNames.CaptchaImage, policy => policy
                .AddRule(r => r.PartitionByClientIp()
                    .WithFixedWindow(TimeSpan.FromMinutes(settings.CaptchaImage.IpDurationMinutes), settings.CaptchaImage.IpMaxCount)
                    .WithName("CaptchaImageByIp")));
        });

        // T2.5: 注册操作限流核心服务
        services.AddTransient<IOperationRateLimitingChecker, OperationRateLimitingChecker>();
        services.AddTransient<IOperationRateLimitingStore, DistributedCacheOperationRateLimitingStore>();
        services.AddTransient<IOperationRateLimitingFormatter, DefaultOperationRateLimitingFormatter>();
        services.AddTransient<IOperationRateLimitingPolicyProvider, DefaultOperationRateLimitingPolicyProvider>();
        services.AddTransient<FixedWindowRateLimitingRule>();
        // 问题5 修复：「特性查找 + 分区参数解析」的唯一实现，供 DI 拦截器（Application 侧
        // [OperationRateLimiting] 特性拦截）消费。round3 删除了从未注册进 MvcOptions 的
        // OperationRateLimitingActionFilter（死基建、暗示 Controller 特性可用的假能力）——
        // MVC 侧没有 Filter 消费该特性，Controller 上标注无效，限流只经 AppService 拦截器或直调 Checker 生效。
        // 注册走 ITransientDependency 约定扫描即可（类上已标），不再显式 AddTransient——
        // 双重注册虽无害（Transient），但与其余服务的注册风格不一致。

        // T2.5: 注册七种分区解析器
        services.AddTransient<RateLimiting.PartitionKeyResolvers.ParameterOperationRateLimitingPartitionKeyResolver>();
        services.AddTransient<RateLimiting.PartitionKeyResolvers.CurrentUserOperationRateLimitingPartitionKeyResolver>();
        services.AddTransient<RateLimiting.PartitionKeyResolvers.CurrentTenantOperationRateLimitingPartitionKeyResolver>();
        services.AddTransient<RateLimiting.PartitionKeyResolvers.ClientIpOperationRateLimitingPartitionKeyResolver>();
        services.AddTransient<RateLimiting.PartitionKeyResolvers.EmailOperationRateLimitingPartitionKeyResolver>();
        services.AddTransient<RateLimiting.PartitionKeyResolvers.PhoneNumberOperationRateLimitingPartitionKeyResolver>();
    }
}
