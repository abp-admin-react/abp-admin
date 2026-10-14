using System;
using System.Threading.Tasks;
using AbpAdmin.Ledger.Integration.Contracts;
using AbpAdmin.Ledger.Services;
using AbpAdmin.Ledger.Services.Dtos;
using Shouldly;
using Volo.Abp;
using Volo.Abp.MultiTenancy;
using Xunit;

namespace AbpAdmin.Ledger;

/// <summary>租户隔离：凭证/余额/调账互不可见；幂等键按租户独立。</summary>
public class TenantIsolationTests : LedgerScenarioTestBase
{
    private readonly Guid _tenantA = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private readonly Guid _tenantB = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
    private readonly Guid _user = Guid.NewGuid();

    [Fact]
    public async Task Posting_And_Balance_Are_Isolated_Per_Tenant()
    {
        Guid voucherA;
        using (GetRequiredService<ICurrentTenant>().Change(_tenantA))
        {
            voucherA = await Ledger.PostAsync(CreditPay("T-1", 50m, _user));
            (await UserBalance(FinanceAccountCodes.Unearned, _user)).ShouldBe(50m);
        }

        using (GetRequiredService<ICurrentTenant>().Change(_tenantB))
        {
            (await UserBalance(FinanceAccountCodes.Unearned, _user)).ShouldBe(0m);
            (await VouchersAsync("T-1")).ShouldBeEmpty();
            await Ledger.PostAsync(CreditPay("T-1", 7m, _user)); // 同 BizRefId 不同租户 → 独立凭证
            (await UserBalance(FinanceAccountCodes.Unearned, _user)).ShouldBe(7m);
            (await VouchersAsync("T-1")).Count.ShouldBe(1);
        }

        using (GetRequiredService<ICurrentTenant>().Change(_tenantA))
        {
            (await UserBalance(FinanceAccountCodes.Unearned, _user)).ShouldBe(50m);
            (await GetRequiredService<FinVoucherAppService>().GetAsync(voucherA)).Amount.ShouldBe(50m);
        }
    }

    [Fact]
    public async Task Cross_Tenant_Voucher_Get_Is_NotFound()
    {
        Guid id;
        using (GetRequiredService<ICurrentTenant>().Change(_tenantA))
        {
            id = await Ledger.PostAsync(CreditPay("T-2", 1m, _user));
        }

        using (GetRequiredService<ICurrentTenant>().Change(_tenantB))
        {
            var ex = await Should.ThrowAsync<BusinessException>(() => GetRequiredService<FinVoucherAppService>().GetAsync(id));
            ex.Code.ShouldBe(FinanceErrorCodes.VoucherNotFound);
        }
    }

    [Fact]
    public async Task Manual_Adjust_Is_Tenant_Scoped()
    {
        Guid id;
        using (GetRequiredService<ICurrentTenant>().Change(_tenantA))
        using (As(TestUsers.AccountantA))
        {
            id = (await GetRequiredService<FinManualAdjustAppService>().CreateAsync(new CreateUpdateFinManualAdjustDto
            {
                Amount = 1m, Currency = Cny, DebitAccountCode = FinanceAccountCodes.ChannelClearing,
                DebitOwnerType = FinOwnerType.Channel, DebitOwnerId = "default"
            })).Id;
        }

        using (GetRequiredService<ICurrentTenant>().Change(_tenantB))
        using (As(TestUsers.AccountantA))
        {
            var ex = await Should.ThrowAsync<BusinessException>(() => GetRequiredService<FinManualAdjustAppService>().GetAsync(id));
            ex.Code.ShouldBe(FinanceErrorCodes.AdjustNotFound);
        }
    }
}
