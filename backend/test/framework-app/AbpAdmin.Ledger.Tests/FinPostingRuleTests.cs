using System.Linq;
using System.Threading.Tasks;
using AbpAdmin.Ledger.Data;
using AbpAdmin.Ledger.Integration.Contracts;
using AbpAdmin.Ledger.Services;
using AbpAdmin.Ledger.Services.Dtos;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Volo.Abp.EntityFrameworkCore;
using Xunit;

namespace AbpAdmin.Ledger;

/// <summary>A2b 过账规则列表：默认排除 Deprecated。</summary>
public class FinPostingRuleTests : LedgerScenarioTestBase
{
    private FinPostingRuleAppService Rules => GetRequiredService<FinPostingRuleAppService>();

    [Fact]
    public async Task List_Excludes_Deprecated_By_Default()
    {
        using (As(TestUsers.Boss))
        {
            var before = await Rules.GetListAsync(new FinPostingRuleListInput());
            before.Items.ShouldNotBeEmpty();
            before.Items.ShouldAllBe(r => r.Status != FinPostingRuleStatus.Deprecated);

            var targetId = before.Items.First().Id;
            await RunInNewUowAsync(async sp =>
            {
                var db = await sp.GetRequiredService<IDbContextProvider<LedgerDbContext>>().GetDbContextAsync();
                var row = await db.PostingRules.FirstAsync(r => r.Id == targetId);
                row.Status = FinPostingRuleStatus.Deprecated;
            });

            var defaults = await Rules.GetListAsync(new FinPostingRuleListInput());
            defaults.Items.ShouldNotContain(r => r.Id == targetId);
            defaults.Items.ShouldAllBe(r => r.Status != FinPostingRuleStatus.Deprecated);

            var all = await Rules.GetListAsync(new FinPostingRuleListInput { IncludeDeprecated = true });
            all.Items.ShouldContain(r => r.Id == targetId && r.Status == FinPostingRuleStatus.Deprecated);

            // 还原，避免污染同进程后续用例
            await RunInNewUowAsync(async sp =>
            {
                var db = await sp.GetRequiredService<IDbContextProvider<LedgerDbContext>>().GetDbContextAsync();
                var row = await db.PostingRules.FirstAsync(r => r.Id == targetId);
                row.Status = FinPostingRuleStatus.Approved;
            });
        }
    }
}
