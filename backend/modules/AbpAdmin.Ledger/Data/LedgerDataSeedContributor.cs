using System;
using System.Collections.Generic;
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
/// 种子：系统科目 + demo 过账规则 + 当前 Open 期间。幂等（按 Code / BizType+Phase+Version 判重）；manual-adjust 规则 F1 升 v2（v1 置 Deprecated）。
/// </summary>
public class LedgerDataSeedContributor : IDataSeedContributor, ITransientDependency
{
    private readonly IRepository<FinAccount, Guid> _accounts;
    private readonly IRepository<FinPostingRule, Guid> _rules;
    private readonly IRepository<FinPeriod, Guid> _periods;
    private readonly IGuidGenerator _guids;
    private readonly ICurrentTenant _currentTenant;

    public LedgerDataSeedContributor(
        IRepository<FinAccount, Guid> accounts,
        IRepository<FinPostingRule, Guid> rules,
        IRepository<FinPeriod, Guid> periods,
        IGuidGenerator guids,
        ICurrentTenant currentTenant)
    {
        _accounts = accounts;
        _rules = rules;
        _periods = periods;
        _guids = guids;
        _currentTenant = currentTenant;
    }

    public async Task SeedAsync(DataSeedContext context)
    {
        using (_currentTenant.Change(context.TenantId))
        {
            await SeedAccountsAsync();
            await SeedPostingRulesAsync();
            await SeedCurrentPeriodAsync();
        }
    }

    private async Task SeedAccountsAsync()
    {
        var seeds = new (string Code, string Name, FinAccountType Type, FinNormalSide Side)[]
        {
            (FinanceAccountCodes.OnChainAsset, "链上资产", FinAccountType.Asset, FinNormalSide.Debit),
            (FinanceAccountCodes.ChannelClearing, "渠道清算", FinAccountType.Asset, FinNormalSide.Debit),
            (FinanceAccountCodes.UserAvailable, "用户余额-可用", FinAccountType.Liability, FinNormalSide.Credit),
            (FinanceAccountCodes.UserFrozen, "用户余额-冻结", FinAccountType.Liability, FinNormalSide.Credit),
            (FinanceAccountCodes.AgentPayable, "代理应付", FinAccountType.Liability, FinNormalSide.Credit),
            (FinanceAccountCodes.Unearned, "预收", FinAccountType.Liability, FinNormalSide.Credit),
            (FinanceAccountCodes.OpeningEquity, "期初权益", FinAccountType.Equity, FinNormalSide.Credit),
            (FinanceAccountCodes.ManualAdjust, "人工调整", FinAccountType.Equity, FinNormalSide.Credit),
            (FinanceAccountCodes.RentIncome, "租赁收入", FinAccountType.Income, FinNormalSide.Credit),
            (FinanceAccountCodes.ServiceIncome, "服务收入", FinAccountType.Income, FinNormalSide.Credit),
            (FinanceAccountCodes.CommissionExpense, "返佣支出", FinAccountType.Expense, FinNormalSide.Debit),
        };

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

    private async Task SeedPostingRulesAsync()
    {
        var rules = new List<(string BizType, string Phase, string Desc, string Json, int Version)>
        {
            (FinanceBizTypes.DemoCredit, FinanceBizTypes.Phases.Pay,
                "先充后用：借渠道清算 / 贷预收",
                """[{"debit":"1002","credit":"2205","note":"pay→unearned"}]""", 1),
            (FinanceBizTypes.DemoCredit, FinanceBizTypes.Phases.Recognize,
                "按用量/批次确认：借预收 / 贷服务收入",
                """[{"debit":"2205","credit":"6002","note":"recognize"}]""", 1),
            (FinanceBizTypes.DemoRent, FinanceBizTypes.Phases.Pay,
                "付租进预收",
                """[{"debit":"2201","credit":"2205","note":"rent pay"}]""", 1),
            (FinanceBizTypes.DemoRent, FinanceBizTypes.Phases.Recognize,
                "按日/按期确认租赁收入",
                """[{"debit":"2205","credit":"6001","note":"rent recognize"}]""", 1),
            (FinanceBizTypes.DemoRent, FinanceBizTypes.Phases.Refund,
                "租金退款（红冲对应 Phase，模板占位）",
                """[{"debit":"2205","credit":"2201","note":"rent refund"}]""", 1),
            (FinanceBizTypes.DemoCommission, FinanceBizTypes.Phases.Accrue,
                "佣金计提：借返佣支出 / 贷代理应付（必带 AgentId）",
                """[{"debit":"6601","credit":"2203","note":"commission accrue","requireAgentId":true}]""", 1),
            (FinanceBizTypes.DemoCommission, FinanceBizTypes.Phases.Settle,
                "佣金结算",
                """[{"debit":"2203","credit":"1002","note":"commission settle"}]""", 1),
            (FinanceBizTypes.Payment, FinanceBizTypes.Phases.Settled,
                "支付门面转发：借渠道清算 / 贷可用或预收",
                """[{"debit":"1002","credit":"2201","note":"payment settled"}]""", 1),
            (FinanceBizTypes.Wallet, FinanceBizTypes.Phases.Freeze,
                "冻结：借可用 / 贷冻结",
                """[{"debit":"2201","credit":"2202","note":"wallet freeze"}]""", 1),
            (FinanceBizTypes.ManualAdjust, FinanceBizTypes.Phases.Post,
                "人工调账过账：借贷双方取自调账单（贷方缺省 3101 人工调整）",
                """[{"debit":"*","credit":"*","note":"manual-adjust"}]""", 2),
        };

        foreach (var r in rules)
        {
            if (!await _rules.AnyAsync(x =>
                    x.BizType == r.BizType && x.Phase == r.Phase && x.Version == r.Version))
            {
                await _rules.InsertAsync(
                    new FinPostingRule(_guids.Create(), r.BizType, r.Phase, r.Json, version: r.Version, description: r.Desc),
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
