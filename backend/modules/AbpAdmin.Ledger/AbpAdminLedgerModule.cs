using System;
using System.Net;
using AbpAdmin.Data;
using AbpAdmin.EntityFrameworkCore;
using AbpAdmin.Ledger.Configuration;
using AbpAdmin.Ledger.Data;
using AbpAdmin.Ledger.Integration.Contracts;
using AbpAdmin.Ledger.Localization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Volo.Abp.AspNetCore.ExceptionHandling;
using Volo.Abp.AspNetCore.Mvc;
using Volo.Abp.Authorization;
using Volo.Abp.EntityFrameworkCore;
using Volo.Abp.Localization;
using Volo.Abp.Localization.ExceptionHandling;
using Volo.Abp.Modularity;
using Volo.Abp.VirtualFileSystem;

namespace AbpAdmin.Ledger;

/// <summary>
/// 瘦 Ledger 模块：表骨架 + 权限 + 种子；过账引擎（幂等/锁/红冲）、调账五态、Prepayment 停写。
/// 规则 Provider 外置 / ETO；薄 Payment 门面。
/// </summary>
[DependsOn(
    typeof(AbpAdminEntityFrameworkCoreModule),
    typeof(AbpAdminLedgerIntegrationContractsModule),
    typeof(AbpAspNetCoreMvcModule),
    typeof(AbpAuthorizationModule),
    typeof(AbpLocalizationModule)
)]
public class AbpAdminLedgerModule : AbpModule
{
    public override void ConfigureServices(ServiceConfigurationContext context)
    {
        var hostConfiguration = context.Services.GetConfiguration();
        var moduleBaseline = new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("Configuration/Ledger.json", optional: false, reloadOnChange: false)
            .Build();

        if (!moduleBaseline.GetSection(LedgerOptions.SectionName).Exists())
        {
            throw new InvalidOperationException(
                $"Configuration/Ledger.json 缺少根节 \"{LedgerOptions.SectionName}\"。");
        }

        context.Services.Configure<LedgerOptions>(options =>
        {
            moduleBaseline.GetSection(LedgerOptions.SectionName).Bind(options);
            hostConfiguration.GetSection(LedgerOptions.SectionName).Bind(options);
        });

        context.Services.AddAbpDbContext<LedgerDbContext>(options =>
        {
            options.AddDefaultRepositories(includeAllEntities: true);
        });

        Configure<AbpDbContextOptions>(options =>
        {
            options.Configure<LedgerDbContext>(contextOptions =>
            {
                contextOptions.UseNpgsql(npgsql =>
                {
                    npgsql.MigrationsHistoryTable(LedgerConsts.SchemaHistoryTable);
                });
            });
        });

        context.Services.AddTransient<IAbpAdminDbSchemaMigrator, LedgerDbSchemaMigrator>();

        Configure<AbpAspNetCoreMvcOptions>(options =>
        {
            options.ConventionalControllers.Create(typeof(AbpAdminLedgerModule).Assembly);
        });

        Configure<AbpLocalizationOptions>(options =>
        {
            options.Resources
                .Add<LedgerResource>("en")
                .AddVirtualJson("/Localization/Ledger");

            options.Resources
                .Get<EasyAbp.Abp.SettingUi.Localization.SettingUiResource>()
                .AddVirtualJson("/Localization/Ledger");
        });

        Configure<AbpVirtualFileSystemOptions>(options =>
        {
            options.FileSets.AddEmbedded<AbpAdminLedgerModule>();
        });

        Configure<AbpExceptionLocalizationOptions>(options =>
        {
            options.MapCodeNamespace(FinanceErrorCodes.Namespace, typeof(LedgerResource));
        });

        // 定稿 §5.1 错误码 → HTTP 状态（前端按码 + 状态双重判断）
        Configure<AbpExceptionHttpStatusCodeOptions>(options =>
        {
            options.Map(FinanceErrorCodes.AdjustSelfApproveNotAllowed, HttpStatusCode.Forbidden);
            options.Map(FinanceErrorCodes.AdjustSubmitDenied, HttpStatusCode.Forbidden);
            options.Map(FinanceErrorCodes.AdjustRejectDenied, HttpStatusCode.Forbidden);
            options.Map(FinanceErrorCodes.AdjustReverseDenied, HttpStatusCode.Forbidden);
            options.Map(FinanceErrorCodes.AdjustCreateDenied, HttpStatusCode.Forbidden);
            options.Map(FinanceErrorCodes.AdjustApproveDenied, HttpStatusCode.Forbidden);
            options.Map(FinanceErrorCodes.AdjustNotApplicant, HttpStatusCode.Forbidden);
            options.Map(FinanceErrorCodes.AdjustReopenDenied, HttpStatusCode.Forbidden);
            options.Map(FinanceErrorCodes.AdjustInvalidStatus, HttpStatusCode.BadRequest);
            options.Map(FinanceErrorCodes.AdjustInvalidInput, HttpStatusCode.BadRequest);
            options.Map(FinanceErrorCodes.ReconcileInvalidRange, HttpStatusCode.BadRequest);
            options.Map(FinanceErrorCodes.AdjustRejectReasonRequired, HttpStatusCode.BadRequest);
            options.Map(FinanceErrorCodes.AdjustNotFound, HttpStatusCode.NotFound);
            options.Map(FinanceErrorCodes.IdempotencyConflict, HttpStatusCode.Conflict);
            options.Map(FinanceErrorCodes.UnbalancedVoucher, HttpStatusCode.BadRequest);
            options.Map(FinanceErrorCodes.LedgerDisabled, HttpStatusCode.Forbidden);
            options.Map(FinanceErrorCodes.PostingRuleNotFound, HttpStatusCode.BadRequest);
            options.Map(FinanceErrorCodes.InvalidEvent, HttpStatusCode.BadRequest);
            options.Map(FinanceErrorCodes.AgentIdRequired, HttpStatusCode.BadRequest);
            options.Map(FinanceErrorCodes.AccountNotFound, HttpStatusCode.BadRequest);
            options.Map(FinanceErrorCodes.AccountSystemReadOnly, HttpStatusCode.BadRequest);
            options.Map(FinanceErrorCodes.AccountHasEntries, HttpStatusCode.BadRequest);
            options.Map(FinanceErrorCodes.AccountCodeDuplicate, HttpStatusCode.BadRequest);
            options.Map(FinanceErrorCodes.AccountHasChildren, HttpStatusCode.BadRequest);
            options.Map(FinanceErrorCodes.InsufficientBalance, HttpStatusCode.BadRequest);
            options.Map(FinanceErrorCodes.VoucherNotFound, HttpStatusCode.NotFound);
            options.Map(FinanceErrorCodes.VoucherNotReversible, HttpStatusCode.BadRequest);
            options.Map(FinanceErrorCodes.PrepaymentWritesFrozen, HttpStatusCode.Forbidden);
        });
    }
}
