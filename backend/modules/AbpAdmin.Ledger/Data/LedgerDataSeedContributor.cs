using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AbpAdmin.Ledger.Entities;
using AbpAdmin.Ledger.Integration.Contracts;
using Volo.Abp.Data;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Guids;
using Volo.Abp.MultiTenancy;

namespace AbpAdmin.Ledger.Data;

/// <summary>
/// 种子：汇总所有 <see cref="ILedgerSeedProvider"/> 的科目与过账模板 + 当前 Open 期间。
/// 幂等：科目按 Code 判重，模板按 BizType+Phase+Version 判重；已存在的行不改不删，只影响新库。
/// </summary>
public class LedgerDataSeedContributor : IDataSeedContributor, ITransientDependency
{
    private readonly IRepository<FinAccount, Guid> _accounts;
    private readonly IRepository<FinPostingRule, Guid> _rules;
    private readonly IRepository<FinPeriod, Guid> _periods;
    private readonly IGuidGenerator _guids;
    private readonly ICurrentTenant _currentTenant;
    private readonly IEnumerable<ILedgerSeedProvider> _providers;

    public LedgerDataSeedContributor(
        IRepository<FinAccount, Guid> accounts,
        IRepository<FinPostingRule, Guid> rules,
        IRepository<FinPeriod, Guid> periods,
        IGuidGenerator guids,
        ICurrentTenant currentTenant,
        IEnumerable<ILedgerSeedProvider> providers)
    {
        _accounts = accounts;
        _rules = rules;
        _periods = periods;
        _guids = guids;
        _currentTenant = currentTenant;
        _providers = providers;
    }

    public async Task SeedAsync(DataSeedContext context)
    {
        using (_currentTenant.Change(context.TenantId))
        {
            var providers = _providers.ToList();
            await SeedAccountsAsync(providers.SelectMany(p => p.Accounts));
            await SeedPostingRulesAsync(providers.SelectMany(p => p.Rules));
            await SeedCurrentPeriodAsync();
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

            await _accounts.InsertAsync(
                new FinAccount(_guids.Create(), s.Code, s.Name, s.Type, s.Side, isSystem: true),
                autoSave: true);
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
                    new FinPostingRule(_guids.Create(), r.BizType, r.Phase, r.TemplateJson, version: r.Version, description: r.Description),
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

    private async Task SeedCurrentPeriodAsync()
    {
        var now = DateTime.UtcNow; // 与 AbpClockOptions.Kind=Utc 一致
        if (await _periods.AnyAsync(x => x.Year == now.Year && x.Month == now.Month))
        {
            return;
        }

        await _periods.InsertAsync(
            new FinPeriod(_guids.Create(), now.Year, now.Month),
            autoSave: true);
    }
}
