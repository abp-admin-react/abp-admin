using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading.Tasks;
using AbpAdmin.Ledger.Entities;
using AbpAdmin.Ledger.Integration.Contracts;
using AbpAdmin.Ledger.Posting;
using Microsoft.Extensions.Configuration;
using Volo.Abp;
using Volo.Abp.Data;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Guids;
using Volo.Abp.MultiTenancy;
using Volo.Abp.Uow;

namespace AbpAdmin.Ledger.Data;

/// <summary>
/// 种子：汇总所有 <see cref="ILedgerSeedProvider"/> 的科目与过账模板 + 当前 Open 期间。
/// 幂等：科目按 Code 判重，模板按 BizType+Phase+Version 判重；已存在的行不改不删，只影响新库。
/// </summary>
public class LedgerDataSeedContributor : IDataSeedContributor, ITransientDependency
{
    /// <summary>演示账的固定演示用户（仅演示数据引用；非真实登录账号）。</summary>
    public static readonly Guid DemoUserId = Guid.Parse("3a244f32-0000-0000-0000-00000000e2e1");
    private readonly IRepository<FinAccount, Guid> _accounts;
    private readonly IRepository<FinPostingRule, Guid> _rules;
    private readonly IRepository<FinPeriod, Guid> _periods;
    private readonly IGuidGenerator _guids;
    private readonly ICurrentTenant _currentTenant;
    private readonly IEnumerable<ILedgerSeedProvider> _providers;
    private readonly LedgerPostingEngine _engine;
    private readonly IConfiguration _configuration;
    private readonly IAbpHostEnvironment _environment;
    private readonly IUnitOfWorkManager _unitOfWorkManager;

    public LedgerDataSeedContributor(
        IRepository<FinAccount, Guid> accounts,
        IRepository<FinPostingRule, Guid> rules,
        IRepository<FinPeriod, Guid> periods,
        IGuidGenerator guids,
        ICurrentTenant currentTenant,
        IEnumerable<ILedgerSeedProvider> providers,
        LedgerPostingEngine engine,
        IConfiguration configuration,
        IAbpHostEnvironment environment,
        IUnitOfWorkManager unitOfWorkManager)
    {
        _accounts = accounts;
        _rules = rules;
        _periods = periods;
        _guids = guids;
        _currentTenant = currentTenant;
        _providers = providers;
        _engine = engine;
        _configuration = configuration;
        _environment = environment;
        _unitOfWorkManager = unitOfWorkManager;
    }

    public async Task SeedAsync(DataSeedContext context)
    {
        // 自包含事务作用域：基线科目/模板/期间与演示过账同事务可见，且满足过账引擎的事务要求
        using var uow = _unitOfWorkManager.Begin(
            new AbpUnitOfWorkOptions
            {
                IsTransactional = true,
                IsolationLevel = IsolationLevel.RepeatableRead,
            },
            requiresNew: true);
        using (_currentTenant.Change(context.TenantId))
        {
            var providers = _providers.ToList();
            await SeedAccountsAsync(providers.SelectMany(p => p.Accounts));
            await SeedPostingRulesAsync(providers.SelectMany(p => p.Rules));
            await SeedCurrentPeriodAsync();
            await SeedDemoVouchersAsync();
            await uow.CompleteAsync();
        }
    }

    private async Task SeedAccountsAsync(IEnumerable<LedgerAccountSeed> seeds)
    {
        foreach (var s in seeds)
        {
            if (await _accounts.AnyAsync(x => x.Code == s.Code))
            {
                continue;
            }

            // ABP 的 EF 管线只自动加租户查询过滤，不在保存时自动盖 TenantId（过账引擎同理显式传）。
            // 漏盖时租户侧种子落到 NULL 作用域，与 host 行在 NULLS NOT DISTINCT 唯一索引上相撞。
            var acc = new FinAccount(_guids.Create(), s.Code, s.Name, s.Type, s.Side, isSystem: true)
                { TenantId = _currentTenant.Id };
            await _accounts.InsertAsync(acc, autoSave: true);
        }
    }

    private async Task SeedPostingRulesAsync(IEnumerable<LedgerRuleSeed> rules)
    {
        foreach (var r in rules)
        {
            if (!await _rules.AnyAsync(x =>
                    x.BizType == r.BizType && x.Phase == r.Phase && x.Version == r.Version))
            {
                await _rules.InsertAsync(
                    new FinPostingRule(_guids.Create(), r.BizType, r.Phase, r.TemplateJson, version: r.Version, description: r.Description)
                        { TenantId = _currentTenant.Id },
                    autoSave: true);
            }

            // 新版本发布后旧版本置 Deprecated（历史凭证仍记着当时的 RuleVersion）
            var older = await _rules.GetListAsync(x =>
                x.BizType == r.BizType && x.Phase == r.Phase && x.Version < r.Version
                && x.Status != FinPostingRuleStatus.Deprecated);
            foreach (var old in older)
            {
                old.Status = FinPostingRuleStatus.Deprecated;
                await _rules.UpdateAsync(old, autoSave: true);
            }
        }
    }

    private async Task SeedDemoVouchersAsync()
    {
        // 演示账（OCR 需求：净库开箱可测）：走引擎幂等过账，重复种子返回原凭证。
        // 门控三态（优先级从高到低）：Finance:SeedDemoData 显式 true 强制开（生产演示库）、
        // 显式 false 强制关（测试库注入它——Ledger 测试的余额断言按用例自建分录精确计账，
        // 演示凭证同科目过账会把断言全部抬高（CI 首次全量实测：期望 100 实测 1350=100+1250））、
        // 缺省随 IsDevelopment()（本地净库 dotnet run 自动带演示账）。
        var configured = _configuration["Finance:SeedDemoData"];
        if (string.Equals(configured, "false", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }
        if (!_environment.IsDevelopment()
            && !string.Equals(configured, "true", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        await _engine.PostAsync(new FinanceBizEvent(
            FinanceBizTypes.DemoCredit, "DEMO-CREDIT-001", FinanceBizTypes.Phases.Pay, "CNY", 1250m,
            UserId: DemoUserId,
            Dimensions: new Dictionary<string, string> { ["channel"] = "wechat" },
            Summary: "演示：微信收款 1250（进预收）"));
        await _engine.PostAsync(new FinanceBizEvent(
            FinanceBizTypes.DemoCredit, "DEMO-CREDIT-001", FinanceBizTypes.Phases.Recognize, "CNY", 800m,
            UserId: DemoUserId,
            Dimensions: new Dictionary<string, string> { ["channel"] = "wechat" },
            Summary: "演示：确认收入 800（预收转收入）"));
        await _engine.PostAsync(new FinanceBizEvent(
            FinanceBizTypes.DemoCredit, "DEMO-CREDIT-002", FinanceBizTypes.Phases.Pay, "CNY", 360m,
            UserId: DemoUserId,
            Dimensions: new Dictionary<string, string> { ["channel"] = "alipay" },
            Summary: "演示：支付宝收款 360（进预收）"));
    }

    private async Task SeedCurrentPeriodAsync()
    {
        var now = DateTime.UtcNow; // 与 AbpClockOptions.Kind=Utc 一致
        if (await _periods.AnyAsync(x => x.Year == now.Year && x.Month == now.Month))
        {
            return;
        }

        await _periods.InsertAsync(
            new FinPeriod(_guids.Create(), now.Year, now.Month) { TenantId = _currentTenant.Id },
            autoSave: true);
    }
}
