using System;
using System.Linq;
using System.Threading.Tasks;
using AbpAdmin.Ledger.Integration.Contracts;
using AbpAdmin.Ledger.Services;
using AbpAdmin.Ledger.Services.Dtos;
using Shouldly;
using Volo.Abp;
using Volo.Abp.Authorization;
using Xunit;

namespace AbpAdmin.Ledger;

/// <summary>科目管理：GET 列表/过滤/权限；CRUD、系统科目只读、有分录不可删只能停用。</summary>
public class FinAccountTests : LedgerScenarioTestBase
{
    private FinAccountAppService Accounts => GetRequiredService<FinAccountAppService>();

    private static CreateUpdateFinAccountDto New(string code, string name = "测试科目") => new()
    {
        Code = code, Name = name, Type = FinAccountType.Expense, Direction = FinNormalSide.Debit,
    };

    [Fact]
    public async Task List_Returns_Seeded_With_Dto_Fields_And_Filters()
    {
        using (As(TestUsers.Boss))
        {
            var all = await Accounts.GetListAsync(new FinAccountListInput { MaxResultCount = 100 });
            all.TotalCount.ShouldBeGreaterThan(0);
            var cc = all.Items.Single(a => a.Code == FinanceAccountCodes.ChannelClearing);
            cc.IsSystem.ShouldBeTrue();
            cc.IsActive.ShouldBeTrue();
            cc.HasEntries.ShouldBeFalse();

            var byCode = await Accounts.GetListAsync(new FinAccountListInput { Code = FinanceAccountCodes.ChannelClearing });
            byCode.Items.ShouldAllBe(a => a.Code.Contains(FinanceAccountCodes.ChannelClearing));
            var byType = await Accounts.GetListAsync(new FinAccountListInput { Type = cc.Type, MaxResultCount = 100 });
            byType.Items.ShouldAllBe(a => a.Type == cc.Type);
            (await Accounts.GetListAsync(new FinAccountListInput { IsActive = false })).TotalCount.ShouldBe(0);
            var page = await Accounts.GetListAsync(new FinAccountListInput { MaxResultCount = 1 });
            page.Items.Count.ShouldBe(1);
            page.TotalCount.ShouldBe(all.TotalCount);
        }
    }

    [Fact]
    public async Task List_HasEntries_True_After_Posting()
    {
        using (As(TestUsers.AccountantA))
        {
            var adj = GetRequiredService<FinManualAdjustAppService>();
            var id = (await adj.CreateAsync(new CreateUpdateFinManualAdjustDto
            {
                Amount = 5m, Currency = Cny, DebitAccountCode = FinanceAccountCodes.ChannelClearing,
                DebitOwnerType = FinOwnerType.Channel, DebitOwnerId = "default", Reason = "x",
            })).Id;
            await adj.SubmitAsync(id);
            using (As(TestUsers.Boss)) { await adj.ApproveAsync(id); }
            var cc = (await Accounts.GetListAsync(new FinAccountListInput { Code = FinanceAccountCodes.ChannelClearing })).Items
                .Single(a => a.Code == FinanceAccountCodes.ChannelClearing);
            cc.HasEntries.ShouldBeTrue();
        }
    }

    [Fact]
    public async Task List_Permissions()
    {
        using (As(TestUsers.AccountantA)) { (await Accounts.GetListAsync(new FinAccountListInput())).TotalCount.ShouldBeGreaterThan(0); }
        using (As(TestUsers.Agent)) { await Should.ThrowAsync<AbpAuthorizationException>(() => Accounts.GetListAsync(new FinAccountListInput())); }
        using (As(TestUsers.Nobody)) { await Should.ThrowAsync<AbpAuthorizationException>(() => Accounts.GetListAsync(new FinAccountListInput())); }
    }

    [Fact]
    public async Task Crud_Disable_Enable_Delete_Happy_Path()
    {
        using (As(TestUsers.AccountantA))
        {
            var a = await Accounts.CreateAsync(New("T9001"));
            a.IsSystem.ShouldBeFalse();
            a.IsActive.ShouldBeTrue();
            var u = await Accounts.UpdateAsync(a.Id, New("T9002", "改名"));
            u.Code.ShouldBe("T9002");
            u.Name.ShouldBe("改名");
            (await Accounts.DisableAsync(a.Id)).IsActive.ShouldBeFalse();
            (await Accounts.GetListAsync(new FinAccountListInput { IsActive = false })).Items.ShouldContain(x => x.Id == a.Id);
            (await Accounts.EnableAsync(a.Id)).IsActive.ShouldBeTrue();
            await Accounts.DeleteAsync(a.Id);
            (await Accounts.GetListAsync(new FinAccountListInput { Code = "T9002" })).TotalCount.ShouldBe(0);
        }
    }

    [Fact]
    public async Task Duplicate_Code_And_Bad_Parent_Rejected()
    {
        using (As(TestUsers.AccountantA))
        {
            await Accounts.CreateAsync(New("T9101"));
            (await Should.ThrowAsync<BusinessException>(() => Accounts.CreateAsync(New("T9101"))))
                .Code.ShouldBe(FinanceErrorCodes.AccountCodeDuplicate);
            var bad = New("T9102"); bad.ParentId = Guid.NewGuid();
            (await Should.ThrowAsync<BusinessException>(() => Accounts.CreateAsync(bad)))
                .Code.ShouldBe(FinanceErrorCodes.AccountNotFound);
        }
    }

    [Fact]
    public async Task System_Accounts_Are_Read_Only()
    {
        using (As(TestUsers.AccountantA))
        {
            var sys = (await Accounts.GetListAsync(new FinAccountListInput { MaxResultCount = 100 })).Items.First(a => a.IsSystem);
            foreach (var act in new Func<Task>[]
                     {
                         () => Accounts.UpdateAsync(sys.Id, New(sys.Code, "x")),
                         () => Accounts.DeleteAsync(sys.Id),
                         () => Accounts.DisableAsync(sys.Id),
                         () => Accounts.EnableAsync(sys.Id),
                     })
            {
                (await Should.ThrowAsync<BusinessException>(act)).Code.ShouldBe(FinanceErrorCodes.AccountSystemReadOnly);
            }
        }
    }

    [Fact]
    public async Task Account_With_Entries_Cannot_Be_Deleted_Only_Disabled()
    {
        using (As(TestUsers.AccountantA))
        {
            var a = await Accounts.CreateAsync(New("T9201"));
            var adj = GetRequiredService<FinManualAdjustAppService>();
            var id = (await adj.CreateAsync(new CreateUpdateFinManualAdjustDto
            {
                Amount = 3m, Currency = Cny, DebitAccountCode = "T9201",
                DebitOwnerType = FinOwnerType.Platform, DebitOwnerId = "", Reason = "x",
            })).Id;
            await adj.SubmitAsync(id);
            using (As(TestUsers.Boss)) { await adj.ApproveAsync(id); }

            (await Accounts.GetAsync(a.Id)).HasEntries.ShouldBeTrue();
            (await Should.ThrowAsync<BusinessException>(() => Accounts.DeleteAsync(a.Id)))
                .Code.ShouldBe(FinanceErrorCodes.AccountHasEntries);
            var changeType = New("T9201"); changeType.Direction = FinNormalSide.Credit;
            (await Should.ThrowAsync<BusinessException>(() => Accounts.UpdateAsync(a.Id, changeType)))
                .Code.ShouldBe(FinanceErrorCodes.AccountHasEntries);
            (await Accounts.UpdateAsync(a.Id, New("T9201", "只改名"))).Name.ShouldBe("只改名");
            (await Accounts.DisableAsync(a.Id)).IsActive.ShouldBeFalse();
        }
    }

    [Fact]
    public async Task Write_Requires_Account_Manage()
    {
        Guid id;
        using (As(TestUsers.AccountantA)) { id = (await Accounts.CreateAsync(New("T9301"))).Id; }
        foreach (var u in new[] { TestUsers.Boss, TestUsers.Agent })
        {
            using (As(u))
            {
                await Should.ThrowAsync<AbpAuthorizationException>(() => Accounts.CreateAsync(New("T9302")));
                await Should.ThrowAsync<AbpAuthorizationException>(() => Accounts.UpdateAsync(id, New("T9301", "x")));
                await Should.ThrowAsync<AbpAuthorizationException>(() => Accounts.DeleteAsync(id));
                await Should.ThrowAsync<AbpAuthorizationException>(() => Accounts.DisableAsync(id));
                await Should.ThrowAsync<AbpAuthorizationException>(() => Accounts.EnableAsync(id));
            }
        }
    }
}
