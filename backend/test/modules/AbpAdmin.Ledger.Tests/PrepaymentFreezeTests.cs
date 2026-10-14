using System;
using System.Threading.Tasks;
using AbpAdmin.Ledger.Integration.Contracts;
using AbpAdmin.Ledger.Settings;
using PAccount = EasyAbp.PaymentService.Prepayment.Accounts.Account;
using Shouldly;
using Volo.Abp;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.SettingManagement;
using Xunit;

namespace AbpAdmin.Ledger;

/// <summary>定稿 §7：FreezeWrites=true 时 Prepayment 账户写入失败并整事务回滚。</summary>
public class PrepaymentFreezeTests : LedgerScenarioTestBase
{
    [Fact]
    public async Task Freeze_Blocks_Prepayment_Account_Insert()
    {
        await GetRequiredService<ISettingManager>().SetGlobalAsync(LedgerSettings.PrepaymentFreezeWrites, "true");
        try
        {
            var ex = await Should.ThrowAsync<BusinessException>(() => WithUnitOfWorkAsync(async () =>
            {
                var repo = GetRequiredService<IRepository<PAccount, Guid>>();
                await repo.InsertAsync(new PAccount(Guid.NewGuid(), null, "default", Guid.NewGuid(), 0m, 0m), autoSave: true);
            }));
            ex.Code.ShouldBe(FinanceErrorCodes.PrepaymentWritesFrozen);
        }
        finally
        {
            await GetRequiredService<ISettingManager>().SetGlobalAsync(LedgerSettings.PrepaymentFreezeWrites, "false");
        }
    }

    [Fact]
    public async Task Unfrozen_Allows_Prepayment_Account_Insert()
    {
        await GetRequiredService<ISettingManager>().SetGlobalAsync(LedgerSettings.PrepaymentFreezeWrites, "false");
        var id = Guid.NewGuid();
        await WithUnitOfWorkAsync(async () =>
        {
            var repo = GetRequiredService<IRepository<PAccount, Guid>>();
            await repo.InsertAsync(new PAccount(id, null, "default", Guid.NewGuid(), 1m, 0m), autoSave: true);
        });

        await WithUnitOfWorkAsync(async () =>
        {
            (await GetRequiredService<IRepository<PAccount, Guid>>().FindAsync(id)).ShouldNotBeNull();
        });
    }
}
