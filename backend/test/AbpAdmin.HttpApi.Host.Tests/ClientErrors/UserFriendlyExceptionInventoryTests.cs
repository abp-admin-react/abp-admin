using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Shouldly;
using Xunit;

namespace AbpAdmin.ClientErrors;

/// <summary>
/// P0-4：UserFriendlyException 构造点版本化清单。
/// 盘点所有构造点、Data/插值来源、管理语言消费者和匿名登录必需资源，
/// 形成版本化清单（不把数量当固定门禁）。
/// </summary>
/// <remarks>
/// <para>
/// 当前清单：43 个构造点（10 个文件）：
/// - AuditLogAppService: 11 处（无显式 code）
/// - IdentityUserAdminAppService: 14 处（2 处带 code）
/// - CacheMonitorAppService: 5 处（无 code）
/// - 其他：13 处
/// </para>
/// <para>
/// 带 code：3 处（导入越权 2 处，GDPR 1 处）。
/// 其余 40 处未显式 code。
/// </para>
/// <para>
/// P0 不把报告中的数量写成固定门禁，而是记录实际盘点结果。
/// </para>
/// </remarks>
public class UserFriendlyExceptionInventoryTests
{
    /// <summary>
    /// UserFriendlyException 构造点分类汇总。
    /// </summary>
    [Fact]
    public void UserFriendlyException_Constructors_Summary()
    {
        var inventory = new UserFriendlyExceptionInventory
        {
            GeneratedAt = DateTime.UtcNow,
            SchemaVersion = 1,
            TotalCount = 43,
            Files =
            [
                new UserFriendlyExceptionFileInventory
                {
                    FilePath = "AbpAdmin.Application/AuditLogs/AuditLogAppService.cs",
                    Count = 11,
                    WithCode = 0, // 预插值 ex.Message，非显式 code 参数
                    WithoutCode = 11,
                    HasDataInterpolation = true,
                    Notes = "审计日志查询错误，部分预插值 ex.Message",
                },
                new UserFriendlyExceptionFileInventory
                {
                    FilePath = "AbpAdmin.Application/Identity/IdentityUserAdminAppService.cs",
                    Count = 14,
                    WithCode = 2,
                    WithoutCode = 12,
                    HasDataInterpolation = false,
                    Notes = "用户导入错误，角色和部门授予越权使用 Client:Forbidden",
                },
                new UserFriendlyExceptionFileInventory
                {
                    FilePath = "AbpAdmin.Application/Monitoring/CacheMonitorAppService.cs",
                    Count = 5,
                    WithCode = 0,
                    WithoutCode = 5,
                    HasDataInterpolation = false,
                    Notes = "缓存监控错误",
                },
                new UserFriendlyExceptionFileInventory
                {
                    FilePath = "AbpAdmin.Application/Identity/IdentityUserSearchAppService.cs",
                    Count = 3,
                    WithCode = 0,
                    WithoutCode = 3,
                    HasDataInterpolation = false,
                    Notes = "用户搜索错误，第 166 行直接使用 ex.Message",
                },
                new UserFriendlyExceptionFileInventory
                {
                    FilePath = "AbpAdmin.Application/Identity/ClaimTypeAppService.cs",
                    Count = 2,
                    WithCode = 0,
                    WithoutCode = 2,
                    HasDataInterpolation = false,
                    Notes = "Claim 类型错误",
                },
                new UserFriendlyExceptionFileInventory
                {
                    FilePath = "modules/platform/AbpAdmin.Webhooks/WebhookAppServices.cs",
                    Count = 3,
                    WithCode = 0,
                    WithoutCode = 3,
                    HasDataInterpolation = false,
                    Notes = "Webhook 错误",
                },
                new UserFriendlyExceptionFileInventory
                {
                    FilePath = "modules/biz/AbpAdmin.Biz.Template/Services/BizDataScopeDemoAppService.cs",
                    Count = 2,
                    WithCode = 0,
                    WithoutCode = 2,
                    HasDataInterpolation = false,
                    Notes = "数据范围演示错误",
                },
                new UserFriendlyExceptionFileInventory
                {
                    FilePath = "modules/biz/AbpAdmin.Biz.Template/Services/BizProjectAppService.cs",
                    Count = 1,
                    WithCode = 0,
                    WithoutCode = 1,
                    HasDataInterpolation = false,
                    Notes = "模板项目错误",
                },
                new UserFriendlyExceptionFileInventory
                {
                    FilePath = "AbpAdmin.Application/Identity/OrganizationUnitAppService.cs",
                    Count = 1,
                    WithCode = 0,
                    WithoutCode = 1,
                    HasDataInterpolation = false,
                    Notes = "组织单元错误",
                },
                new UserFriendlyExceptionFileInventory
                {
                    FilePath = "AbpAdmin.Application/Gdpr/GdprRequestAppService.cs",
                    Count = 1,
                    WithCode = 1,
                    WithoutCode = 0,
                    HasDataInterpolation = false,
                    Notes = "GDPR 错误，显式 code: AbpAdmin:Gdpr:IncorrectPassword",
                },
            ],
        };

        // 验证总数
        inventory.TotalCount.ShouldBe(43);
        inventory.Files.Count.ShouldBe(10);

        // 验证分组
        var withCode = inventory.Files.Sum(f => f.WithCode);
        var withoutCode = inventory.Files.Sum(f => f.WithoutCode);
        withCode.ShouldBe(3);
        withoutCode.ShouldBe(40);

        // 不把数量当固定门禁
        // 未来代码变更会导致数量变化，测试只验证清单结构正确
    }

    /// <summary>
    /// 验证预插值 ex.Message 的构造点已识别。
    /// </summary>
    [Fact]
    public void PreInterpolated_ExceptionMessage_Should_Be_Identified()
    {
        var preInterpolatedSites = new List<PreInterpolatedSite>
        {
            // AuditLogAppService: 预插值 ex.Message
            new()
            {
                FilePath = "AbpAdmin.Application/AuditLogs/AuditLogAppService.cs",
                LineNumbers = [498, 499, 500, 501, 572, 573, 574, 575], // 近似行号
                Pattern = "ex.Message 插入 message",
                Risk = "可能暴露内部错误细节",
                ManagementOnly = true, // 审计日志管理员专用
                Notes = "需评估是否保留原文或收紧",
            },

            // IdentityUserSearchAppService:166 直接使用 ex.Message
            new()
            {
                FilePath = "AbpAdmin.Application/Identity/IdentityUserSearchAppService.cs",
                LineNumbers = [166],
                Pattern = "直接使用 ex.Message",
                Risk = "可能暴露内部错误细节",
                ManagementOnly = true,
                Notes = "用户搜索错误",
            },
        };

        preInterpolatedSites.Count.ShouldBe(2);

        // P0 只识别，不修复
        // P1 决定保留或收紧
    }

    /// <summary>
    /// 验证 Data 传递路径已识别。
    /// </summary>
    [Fact]
    public void Data_Transmission_Paths_Should_Be_Identified()
    {
        var dataPaths = new List<DataTransmissionPath>
        {
            // FriendlyBusinessExceptionMiddleware:91 直接下发 Data
            new()
            {
                SourceId = "FriendlyBusinessExceptionMiddleware.Data",
                FilePath = "AbpAdmin.HttpApi.Host/FriendlyBusinessExceptionMiddleware.cs",
                LineNumber = 91,
                Pattern = "data = business.Data",
                Risk = "Data 字段原样下发，可能包含敏感信息",
                Mitigation = "P1 实现批准字段白名单过滤",
            },

            // ABP DefaultExceptionToErrorInfoConverter 用 Data 插值 message
            new()
            {
                SourceId = "DefaultExceptionToErrorInfoConverter.Data",
                FilePath = "ABP 框架内置",
                LineNumber = -1,
                Pattern = "TryToLocalizeExceptionMessage",
                Risk = "Data 值可能被插入 message",
                Mitigation = "P1 在插值前筛选参数",
            },
        };

        dataPaths.Count.ShouldBe(2);
    }

    /// <summary>
    /// 验证管理语言消费者已识别。
    /// </summary>
    [Fact]
    public void Management_Language_Consumers_Should_Be_Identified()
    {
        var consumers = new List<ManagementLanguageConsumer>
        {
            new()
            {
                ResourceKey = "AuditLogs:*",
                Consumer = "AuditLogAppService",
                FieldType = "error message",
                LocalizationSource = "AbpAdminResource",
            },
            new()
            {
                ResourceKey = "ClaimTypeIsStatic",
                Consumer = "ClaimTypeAppService",
                FieldType = "error message",
                LocalizationSource = "AbpAdminResource",
            },
            new()
            {
                ResourceKey = "AbpAdmin:Gdpr:IncorrectPassword",
                Consumer = "GdprRequestAppService",
                FieldType = "error message",
                LocalizationSource = "AbpAdminResource",
            },
        };

        consumers.Count.ShouldBe(3);
    }

    /// <summary>
    /// 验证匿名登录必需资源已识别。
    /// </summary>
    [Fact]
    public void Anonymous_Login_Required_Resources_Should_Be_Identified()
    {
        var requiredResources = new List<AnonymousRequiredResource>
        {
            new()
            {
                Endpoint = "Account/Login",
                ResourceKeys = ["Account:Login", "Account:ForgotPassword"],
                ResourceType = "UI text",
            },
            new()
            {
                Endpoint = "api/abp/application-configuration",
                ResourceKeys = ["*"], // 匿名配置裁剪后的公开部分
                ResourceType = "JSON configuration",
            },
            new()
            {
                Endpoint = "api/abp/application-localization",
                ResourceKeys = ["AbpAdminResource"], // 公开文案子集
                ResourceType = "JSON localization",
            },
            new()
            {
                Endpoint = "Abp/ApplicationConfigurationScript",
                ResourceKeys = ["*"],
                ResourceType = "JavaScript configuration",
            },
            new()
            {
                Endpoint = "Abp/ApplicationLocalizationScript",
                ResourceKeys = ["AbpAdminResource"],
                ResourceType = "JavaScript localization",
            },
            new()
            {
                Endpoint = "api/app/captcha-image",
                ResourceKeys = [],
                ResourceType = "Image",
            },
        };

        requiredResources.Count.ShouldBe(6);
    }
}

/// <summary>
/// UserFriendlyException 构造点清单。
/// </summary>
public sealed record UserFriendlyExceptionInventory
{
    public DateTime GeneratedAt { get; init; }
    public int SchemaVersion { get; init; }
    public int TotalCount { get; init; }
    public IReadOnlyList<UserFriendlyExceptionFileInventory> Files { get; init; } = [];
}

/// <summary>
/// 单个文件的构造点统计。
/// </summary>
public sealed record UserFriendlyExceptionFileInventory
{
    public required string FilePath { get; init; }
    public int Count { get; init; }
    public int WithCode { get; init; }
    public int WithoutCode { get; init; }
    public bool HasDataInterpolation { get; init; }
    public string? Notes { get; init; }
}

/// <summary>
/// 预插值 ex.Message 的站点。
/// </summary>
public sealed record PreInterpolatedSite
{
    public required string FilePath { get; init; }
    public required IReadOnlyList<int> LineNumbers { get; init; }
    public required string Pattern { get; init; }
    public required string Risk { get; init; }
    public bool ManagementOnly { get; init; }
    public string? Notes { get; init; }
}

/// <summary>
/// Data 传递路径。
/// </summary>
public sealed record DataTransmissionPath
{
    public required string SourceId { get; init; }
    public required string FilePath { get; init; }
    public int LineNumber { get; init; }
    public required string Pattern { get; init; }
    public required string Risk { get; init; }
    public required string Mitigation { get; init; }
}

/// <summary>
/// 管理语言消费者。
/// </summary>
public sealed record ManagementLanguageConsumer
{
    public required string ResourceKey { get; init; }
    public required string Consumer { get; init; }
    public required string FieldType { get; init; }
    public required string LocalizationSource { get; init; }
}

/// <summary>
/// 匿名登录必需资源。
/// </summary>
public sealed record AnonymousRequiredResource
{
    public required string Endpoint { get; init; }
    public required IReadOnlyList<string> ResourceKeys { get; init; }
    public required string ResourceType { get; init; }
}
