using System;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using AbpAdmin.Collection.Balances;
using AbpAdmin.Collection.Contracts;
using AbpAdmin.Collection.Data;
using AbpAdmin.Collection.Entities;
using AbpAdmin.Collection.Ledger;
using AbpAdmin.Collection.Permissions;
using AbpAdmin.Collection.Services;
using AbpAdmin.Ledger.Data;
using AbpAdmin.Ledger.Entities;
using AbpAdmin.Ledger.Integration.Contracts;
using AbpAdmin.Ledger.Settings;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Shouldly;
using Volo.Abp;
using Volo.Abp.Authorization;
using Volo.Abp.Authorization.Permissions;
using Volo.Abp.EntityFrameworkCore;
using Volo.Abp.Identity;
using Volo.Abp.Modularity;
using Volo.Abp.MultiTenancy;
using Volo.Abp.Security.Claims;
using Volo.Abp.SettingManagement;
using Volo.Abp.Uow;
using Xunit;

namespace AbpAdmin.Collection;

[DependsOn(typeof(CollectionTestModule))]
public class BalanceAdjustmentTestModule : AbpModule
{
    public override void ConfigureServices(ServiceConfigurationContext context)
    {
        context.Services.Replace(ServiceDescriptor.Transient<IAuthorizationService, AbpAuthorizationService>());
        context.Services.Replace(ServiceDescriptor.Transient<IAbpAuthorizationService, AbpAuthorizationService>());
        context.Services.Replace(ServiceDescriptor.Transient<IMethodInvocationAuthorizationService, MethodInvocationAuthorizationService>());
        context.Services.Replace(ServiceDescriptor.Singleton<IPermissionChecker, BalanceAdjustmentTestPermissionChecker>());
        context.Services.AddSingleton<AdjustmentPostingFailure>();
        context.Services.Replace(ServiceDescriptor.Transient<CollectionLedgerPoster, AdjustmentTestLedgerPoster>());
    }
}

public sealed class AdjustmentPostingFailure
{
    public bool FailAfterPosting { get; set; }
}

public class AdjustmentTestLedgerPoster(ILedgerIntegrationService ledger, AdjustmentPostingFailure failure)
    : CollectionLedgerPoster(ledger)
{
    public override async Task<Guid> PostBalanceAdjustmentAsync(Guid adjustmentId, Guid userId,
        CollectionCurrency currency, decimal signedAmount, string reason, Guid reviewerId)
    {
        var voucherId = await base.PostBalanceAdjustmentAsync(adjustmentId, userId, currency, signedAmount, reason, reviewerId);
        if (failure.FailAfterPosting) throw new InvalidOperationException("Injected failure after ledger writes");
        return voucherId;
    }
}

public class BalanceAdjustmentTestPermissionChecker(ICurrentPrincipalAccessor accessor) : IPermissionChecker
{
    public Task<bool> IsGrantedAsync(string name) => IsGrantedAsync(accessor.Principal, name);

    public Task<bool> IsGrantedAsync(ClaimsPrincipal? principal, string name) => Task.FromResult(
        principal?.HasClaim("balance-adjustment-test-denied", "true") != true &&
        (name != CollectionPermissions.BalanceAdjustments.ApproveOwn ||
            principal?.HasClaim("balance-adjustment-test-self", "true") == true));

    public Task<MultiplePermissionGrantResult> IsGrantedAsync(string[] names) => IsGrantedAsync(accessor.Principal, names);

    public async Task<MultiplePermissionGrantResult> IsGrantedAsync(ClaimsPrincipal? principal, string[] names)
    {
        var result = new MultiplePermissionGrantResult();
        foreach (var name in names)
        {
            result.Result[name] = await IsGrantedAsync(principal, name)
                ? PermissionGrantResult.Granted
                : PermissionGrantResult.Prohibited;
        }
        return result;
    }
}

public class CollectionBalanceAdjustmentTests : AbpAdminTestBase<BalanceAdjustmentTestModule>
{
    private static readonly Guid Applicant = Guid.Parse("60000000-0000-4000-8000-000000000101");
    private static readonly Guid Reviewer = Guid.Parse("60000000-0000-4000-8000-000000000102");

    [Theory]
    [InlineData(CollectionCurrency.Usdt)]
    [InlineData(CollectionCurrency.Usdc)]
    public async Task Credit_And_Debit_Update_Wallet_Flow_Voucher_And_Projection(CollectionCurrency currency)
    {
        var target = await CreateUserAsync();
        var credit = await ApplyAsync(target, "12.123456789012345678", currency);
        credit.BalanceAfter.ShouldBe("12.123456789012345678");
        var debit = await ApplyAsync(target, "-2.123456789012345678", currency);
        debit.BalanceAfter.ShouldBe("10");

        await ReadAsync(async (db, ledger) =>
        {
            var wallet = await db.Balances.SingleAsync(row => row.UserId == target);
            wallet.Balance.ShouldBe(10m);
            wallet.Frozen.ShouldBe(0m);
            var flow = await db.BalanceTxns.SingleAsync(row => row.Id == debit.BalanceTxnId);
            flow.Action.ShouldBe(BalanceActions.ManualAdjust);
            flow.Amount.ShouldBe(-2.123456789012345678m);
            flow.BalanceAfter.ShouldBe(10m);
            flow.BusinessId.ShouldBe(debit.Id);
            var voucher = await ledger.Vouchers.SingleAsync(row => row.Id == debit.LedgerVoucherId);
            voucher.BizType.ShouldBe(FinanceBizTypes.WalletAdjustment);
            voucher.UserId.ShouldBe(target);
            var entries = await ledger.Entries.Where(row => row.VoucherId == voucher.Id).ToListAsync();
            entries.Count.ShouldBe(2);
            entries.Sum(row => row.Debit).ShouldBe(2.123456789012345678m);
            entries.Sum(row => row.Credit).ShouldBe(entries.Sum(row => row.Debit));
            foreach (var adjustment in new[] { credit, debit })
            {
                var lines = await (from entry in ledger.Entries
                    join sub in ledger.SubAccounts on entry.SubAccountId equals sub.Id
                    join account in ledger.Accounts on sub.AccountId equals account.Id
                    where entry.VoucherId == adjustment.LedgerVoucherId
                    select new { account.Code, sub.OwnerType, sub.OwnerId, sub.Currency, entry.Debit, entry.Credit }).ToListAsync();
                lines.Count.ShouldBe(2);
                var userLine = lines.Single(line => line.Code == FinanceAccountCodes.UserAvailable);
                var offset = lines.Single(line => line.Code == "3101");
                userLine.OwnerType.ShouldBe(FinOwnerType.User);
                userLine.OwnerId.ShouldBe(target.ToString());
                userLine.Currency.ShouldBe(CollectionLedgerPoster.CurrencyCode(currency));
                offset.OwnerType.ShouldBe(FinOwnerType.Platform);
                offset.Currency.ShouldBe(userLine.Currency);
                var signedAmount = decimal.Parse(adjustment.Amount, System.Globalization.CultureInfo.InvariantCulture);
                userLine.Credit.ShouldBe(signedAmount > 0 ? signedAmount : 0);
                userLine.Debit.ShouldBe(signedAmount < 0 ? -signedAmount : 0);
                offset.Debit.ShouldBe(userLine.Credit);
                offset.Credit.ShouldBe(userLine.Debit);
            }
            var userProjection = await (from sub in ledger.SubAccounts
                join account in ledger.Accounts on sub.AccountId equals account.Id
                join projection in ledger.BalanceProjections on sub.Id equals projection.SubAccountId
                where account.Code == FinanceAccountCodes.UserAvailable && sub.OwnerId == target.ToString()
                select projection.Balance).SingleAsync();
            userProjection.ShouldBe(wallet.Balance);
        });
    }

    [Fact]
    public async Task Request_Replay_Reuses_The_Application_And_Conflicting_Content_Is_Rejected()
    {
        var target = await CreateUserAsync();
        var input = Input(target, "30.00");
        var first = await AsAsync(Applicant, service => service.CreateAsync(input));
        input.Amount = "30";
        var replays = await Task.WhenAll(
            AsAsync(Applicant, service => service.CreateAsync(input)),
            AsAsync(Applicant, service => service.CreateAsync(input)));
        var repeated = replays[0];
        replays[1].Id.ShouldBe(first.Id);
        repeated.Id.ShouldBe(first.Id);
        input.Amount = "31";
        var conflict = await Should.ThrowAsync<BusinessException>(() => AsAsync(Applicant, service => service.CreateAsync(input)));
        conflict.Code.ShouldBe(CollectionBalanceAdjustmentErrorCodes.RequestConflict);
        await ReadAsync(async (db, ledger) =>
        {
            (await db.BalanceAdjustments.CountAsync()).ShouldBe(1);
            (await db.BalanceTxns.CountAsync()).ShouldBe(0);
            (await ledger.Vouchers.CountAsync(row => row.BizType == FinanceBizTypes.WalletAdjustment)).ShouldBe(0);
        });
    }

    [Fact]
    public async Task Concurrent_Duplicate_Approvals_Produce_One_Flow_And_One_Voucher()
    {
        var target = await CreateUserAsync();
        var request = await AsAsync(Applicant, service => service.CreateAsync(Input(target, "25")));
        var approved = await Task.WhenAll(
            AsAsync(Reviewer, service => service.ApproveAsync(request.Id)),
            AsAsync(Reviewer, service => service.ApproveAsync(request.Id)),
            AsAsync(Reviewer, service => service.ApproveAsync(request.Id)));
        approved.Select(row => row.LedgerVoucherId).Distinct().Count().ShouldBe(1);
        approved.Select(row => row.BalanceTxnId).Distinct().Count().ShouldBe(1);
        await ReadAsync(async (db, ledger) =>
        {
            (await db.Balances.SingleAsync(row => row.UserId == target)).Balance.ShouldBe(25m);
            (await db.BalanceTxns.CountAsync(row => row.BusinessId == request.Id)).ShouldBe(1);
            (await ledger.Vouchers.CountAsync(row => row.BizRefId == request.Id.ToString("D"))).ShouldBe(1);
            (await ledger.Entries.CountAsync(row => row.VoucherId == approved[0].LedgerVoucherId)).ShouldBe(2);
        });
    }

    [Fact]
    public async Task Concurrent_Deductions_Cannot_Overdraw_The_Wallet()
    {
        var target = await CreateUserAsync();
        await ApplyAsync(target, "100");
        var first = await AsAsync(Applicant, service => service.CreateAsync(Input(target, "-70")));
        var second = await AsAsync(Applicant, service => service.CreateAsync(Input(target, "-70")));
        var outcomes = await Task.WhenAll(TryApproveAsync(first.Id), TryApproveAsync(second.Id));
        outcomes.Count(value => value).ShouldBe(1);
        await ReadAsync(async (db, ledger) =>
        {
            (await db.Balances.SingleAsync(row => row.UserId == target)).Balance.ShouldBe(30m);
            (await db.BalanceTxns.CountAsync(row => row.Action == BalanceActions.ManualAdjust)).ShouldBe(2);
            (await db.BalanceAdjustments.CountAsync(row => row.Status == CollectionBalanceAdjustmentStatus.Pending)).ShouldBe(1);
            (await ledger.Vouchers.CountAsync(row => row.BizType == FinanceBizTypes.WalletAdjustment)).ShouldBe(2);
        });
    }

    [Fact]
    public async Task Approval_Competing_With_An_Order_Debit_Uses_The_Same_Balance_Lock()
    {
        var target = await CreateUserAsync();
        await ApplyAsync(target, "100");
        var request = await AsAsync(Applicant, service => service.CreateAsync(Input(target, "-80")));
        var debit = TryOrderDebitAsync(target, 40m);
        var results = await Task.WhenAll(TryApproveAsync(request.Id), debit);
        results.Count(value => value).ShouldBe(1);
        await ReadAsync(async (db, _) =>
        {
            var balance = (await db.Balances.SingleAsync(row => row.UserId == target)).Balance;
            balance.ShouldBe(results[0] ? 20m : 60m);
            (await db.BalanceTxns.CountAsync()).ShouldBe(2);
        });
    }

    [Fact]
    public async Task Insufficient_Available_Balance_Preserves_Frozen_Balance_And_Leaves_Request_Pending()
    {
        var target = await CreateUserAsync();
        await ApplyAsync(target, "100");
        await WithUnitOfWorkAsync(new AbpUnitOfWorkOptions { IsTransactional = true }, async () =>
        {
            var withdrawal = new ColWithdrawOrder(Guid.NewGuid())
            {
                UserId = target,
                Currency = CollectionCurrency.Usdt,
                Amount = 90m,
            };
            await GetRequiredService<CollectionBalanceManager>().FreezeAsync(target, CollectionCurrency.Usdt, 90m, withdrawal.Id);
            await GetRequiredService<CollectionLedgerPoster>().PostFreezeAsync(withdrawal);
        });
        var request = await AsAsync(Applicant, service => service.CreateAsync(Input(target, "-20")));
        var error = await Should.ThrowAsync<BusinessException>(() => AsAsync(Reviewer, service => service.ApproveAsync(request.Id)));
        error.Code.ShouldBe(CollectionErrorCodes.InsufficientBalance);
        await ReadAsync(async (db, ledger) =>
        {
            var balance = await db.Balances.SingleAsync(row => row.UserId == target);
            balance.Balance.ShouldBe(10m);
            balance.Frozen.ShouldBe(90m);
            (await db.BalanceAdjustments.SingleAsync(row => row.Id == request.Id)).Status.ShouldBe(CollectionBalanceAdjustmentStatus.Pending);
            (await db.BalanceTxns.CountAsync(row => row.BusinessId == request.Id)).ShouldBe(0);
            (await ledger.Vouchers.CountAsync(row => row.BizRefId == request.Id.ToString("D"))).ShouldBe(0);
        });
    }

    [Fact]
    public async Task Ledger_Failure_Rolls_Back_Wallet_Flow_And_Approval()
    {
        var target = await CreateUserAsync();
        await ApplyAsync(target, "50");
        var request = await AsAsync(Applicant, service => service.CreateAsync(Input(target, "-10")));
        await GetRequiredService<ISettingManager>().SetGlobalAsync(LedgerSettings.Enabled, "false");
        try
        {
            var error = await Should.ThrowAsync<BusinessException>(() => AsAsync(Reviewer, service => service.ApproveAsync(request.Id)));
            error.Code.ShouldBe(FinanceErrorCodes.LedgerDisabled);
            await ReadAsync(async (db, ledger) =>
            {
                (await db.Balances.SingleAsync(row => row.UserId == target)).Balance.ShouldBe(50m);
                (await db.BalanceTxns.CountAsync(row => row.BusinessId == request.Id)).ShouldBe(0);
                (await db.BalanceAdjustments.SingleAsync(row => row.Id == request.Id)).Status.ShouldBe(CollectionBalanceAdjustmentStatus.Pending);
                (await ledger.Vouchers.CountAsync(row => row.BizRefId == request.Id.ToString("D"))).ShouldBe(0);
                (await ledger.Entries.CountAsync(entry => ledger.Vouchers.Any(voucher =>
                    voucher.Id == entry.VoucherId && voucher.BizType == FinanceBizTypes.WalletAdjustment))).ShouldBe(2);
            });
        }
        finally
        {
            await GetRequiredService<ISettingManager>().SetGlobalAsync(LedgerSettings.Enabled, "true");
        }
    }

    [Fact]
    public async Task Failure_After_Ledger_Writes_Rolls_Back_Both_Contexts()
    {
        var target = await CreateUserAsync();
        await ApplyAsync(target, "50");
        var request = await AsAsync(Applicant, service => service.CreateAsync(Input(target, "-10")));
        System.Collections.Generic.Dictionary<Guid, decimal>? before = null;
        var beforeVouchers = 0;
        var beforeEntries = 0;
        await ReadAsync(async (_, ledger) =>
        {
            before = await ledger.BalanceProjections.ToDictionaryAsync(row => row.Id, row => row.Balance);
            beforeVouchers = await ledger.Vouchers.CountAsync();
            beforeEntries = await ledger.Entries.CountAsync();
        });
        GetRequiredService<AdjustmentPostingFailure>().FailAfterPosting = true;
        await Should.ThrowAsync<InvalidOperationException>(() =>
            AsAsync(Reviewer, service => service.ApproveAsync(request.Id)));
        GetRequiredService<AdjustmentPostingFailure>().FailAfterPosting = false;
        await ReadAsync(async (db, ledger) =>
        {
            (await db.Balances.SingleAsync(row => row.UserId == target)).Balance.ShouldBe(50m);
            (await db.BalanceTxns.CountAsync(row => row.BusinessId == request.Id)).ShouldBe(0);
            (await db.BalanceAdjustments.SingleAsync(row => row.Id == request.Id)).Status.ShouldBe(CollectionBalanceAdjustmentStatus.Pending);
            (await ledger.Vouchers.CountAsync()).ShouldBe(beforeVouchers);
            (await ledger.Entries.CountAsync()).ShouldBe(beforeEntries);
            var after = await ledger.BalanceProjections.ToDictionaryAsync(row => row.Id, row => row.Balance);
            after.Count.ShouldBe(before!.Count);
            foreach (var entry in before) after[entry.Key].ShouldBe(entry.Value);
        });
    }

    [Fact]
    public async Task Self_Approval_Permission_Cannot_Be_Automatically_Granted_To_Admin_Role()
    {
        var permission = await GetRequiredService<IPermissionDefinitionManager>()
            .GetAsync(CollectionPermissions.BalanceAdjustments.ApproveOwn);
        permission.Providers.ShouldBe([UserPermissionValueProvider.ProviderName]);
    }

    [Fact]
    public async Task Rejection_Is_Idempotent_And_Cannot_Be_Approved()
    {
        var target = await CreateUserAsync();
        var request = await AsAsync(Applicant, service => service.CreateAsync(Input(target, "50")));
        var input = new RejectCollectionBalanceAdjustmentInput { Reason = "依据不足" };
        var rejected = await AsAsync(Reviewer, service => service.RejectAsync(request.Id, input));
        var repeated = await AsAsync(Reviewer, service => service.RejectAsync(request.Id, input));
        repeated.ReviewedAt.ShouldBe(rejected.ReviewedAt);
        repeated.ReviewNote.ShouldBe(input.Reason);
        var error = await Should.ThrowAsync<BusinessException>(() => AsAsync(Reviewer, service => service.ApproveAsync(request.Id)));
        error.Code.ShouldBe(CollectionBalanceAdjustmentErrorCodes.InvalidStatus);
        await ReadAsync(async (db, ledger) =>
        {
            (await db.Balances.CountAsync()).ShouldBe(0);
            (await db.BalanceTxns.CountAsync()).ShouldBe(0);
            (await ledger.Vouchers.CountAsync(row => row.BizType == FinanceBizTypes.WalletAdjustment)).ShouldBe(0);
        });
    }

    [Fact]
    public async Task Competing_Approve_And_Reject_Commit_Only_One_Decision()
    {
        var target = await CreateUserAsync();
        var request = await AsAsync(Applicant, service => service.CreateAsync(Input(target, "15")));
        async Task<bool> DecideAsync(bool approve)
        {
            try
            {
                if (approve)
                {
                    await AsAsync(Reviewer, service => service.ApproveAsync(request.Id));
                }
                else
                {
                    await AsAsync(Reviewer, service => service.RejectAsync(request.Id,
                        new RejectCollectionBalanceAdjustmentInput { Reason = "驳回" }));
                }
                return true;
            }
            catch (BusinessException error) when (error.Code == CollectionBalanceAdjustmentErrorCodes.InvalidStatus)
            {
                return false;
            }
        }

        var decisions = await Task.WhenAll(DecideAsync(true), DecideAsync(false));
        decisions.Count(decision => decision).ShouldBe(1);
        await ReadAsync(async (db, ledger) =>
        {
            var result = await db.BalanceAdjustments.SingleAsync(row => row.Id == request.Id);
            var approved = result.Status == CollectionBalanceAdjustmentStatus.Approved;
            (await db.BalanceTxns.CountAsync(row => row.BusinessId == request.Id)).ShouldBe(approved ? 1 : 0);
            (await ledger.Vouchers.CountAsync(row => row.BizRefId == request.Id.ToString("D"))).ShouldBe(approved ? 1 : 0);
        });
    }

    [Fact]
    public async Task Self_Review_Requires_Extra_Permission()
    {
        var target = await CreateUserAsync();
        var request = await AsAsync(Applicant, service => service.CreateAsync(Input(target, "1")));
        await Should.ThrowAsync<AbpAuthorizationException>(() => AsAsync(Applicant, service => service.ApproveAsync(request.Id)));
        var approved = await AsAsync(Applicant, service => service.ApproveAsync(request.Id), allowSelf: true);
        approved.Status.ShouldBe(CollectionBalanceAdjustmentStatus.Approved);
    }

    [Fact]
    public async Task Host_User_And_Endpoint_Permissions_Are_Required()
    {
        var target = await CreateUserAsync();
        await Should.ThrowAsync<AbpAuthorizationException>(() => AsAsync(Applicant,
            service => service.CreateAsync(Input(target, "1")), denied: true));
        using var tenant = GetRequiredService<ICurrentTenant>().Change(Guid.NewGuid());
        var error = await Should.ThrowAsync<BusinessException>(() => AsAsync(Applicant,
            service => service.CreateAsync(Input(target, "1"))));
        error.Code.ShouldBe(CollectionErrorCodes.HostOnly);
    }

    [Fact]
    public async Task Missing_Target_User_Is_Rejected()
    {
        var error = await Should.ThrowAsync<BusinessException>(() => AsAsync(Applicant,
            service => service.CreateAsync(Input(Guid.NewGuid(), "1"))));
        error.Code.ShouldBe(CollectionBalanceAdjustmentErrorCodes.UserNotFound);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-0.000")]
    [InlineData("1e2")]
    [InlineData("1,000")]
    [InlineData("1.0000000000000000001")]
    [InlineData("10000000000")]
    [InlineData("-79228162514264337593543950335")]
    public async Task Invalid_Amounts_Are_Rejected_Without_Parsing_Overflow(string amount)
    {
        var error = await Should.ThrowAsync<BusinessException>(() => AsAsync(Applicant,
            service => service.CreateAsync(Input(Guid.NewGuid(), amount))));
        error.Code.ShouldBe(CollectionBalanceAdjustmentErrorCodes.InvalidInput);
    }

    private async Task<CollectionBalanceAdjustmentDto> ApplyAsync(Guid target, string amount,
        CollectionCurrency currency = CollectionCurrency.Usdt)
    {
        var input = Input(target, amount);
        input.Currency = currency;
        var request = await AsAsync(Applicant, service => service.CreateAsync(input));
        return await AsAsync(Reviewer, service => service.ApproveAsync(request.Id));
    }

    private async Task<bool> TryApproveAsync(Guid id)
    {
        try
        {
            await AsAsync(Reviewer, service => service.ApproveAsync(id));
            return true;
        }
        catch (BusinessException exception) when (exception.Code == CollectionErrorCodes.InsufficientBalance)
        {
            return false;
        }
    }

    private async Task<bool> TryOrderDebitAsync(Guid target, decimal amount)
    {
        using var scope = ServiceProvider.CreateScope();
        try
        {
            await scope.ServiceProvider.GetRequiredService<CollectionBalanceManager>()
                .DebitAsync("adjustment-race-order", Guid.NewGuid(), target, CollectionCurrency.Usdt, amount);
            return true;
        }
        catch (BusinessException exception) when (exception.Code == CollectionErrorCodes.InsufficientBalance)
        {
            return false;
        }
    }

    private async Task<T> AsAsync<T>(Guid userId, Func<CollectionBalanceAdjustmentAppService, Task<T>> action,
        bool allowSelf = false, bool denied = false)
    {
        using var scope = ServiceProvider.CreateScope();
        var claims = new[]
        {
            new Claim(AbpClaimTypes.UserId, userId.ToString()),
            new Claim(AbpClaimTypes.UserName, userId.ToString("N")),
            new Claim("balance-adjustment-test-self", allowSelf.ToString().ToLowerInvariant()),
            new Claim("balance-adjustment-test-denied", denied.ToString().ToLowerInvariant()),
        };
        using var principal = scope.ServiceProvider.GetRequiredService<ICurrentPrincipalAccessor>()
            .Change(new ClaimsPrincipal(new ClaimsIdentity(claims, "Test")));
        return await action(scope.ServiceProvider.GetRequiredService<CollectionBalanceAdjustmentAppService>());
    }

    private async Task<Guid> CreateUserAsync()
    {
        var id = Guid.NewGuid();
        using var scope = ServiceProvider.CreateScope();
        using var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWorkManager>().Begin(requiresNew: true, isTransactional: true);
        var repository = scope.ServiceProvider.GetRequiredService<IIdentityUserRepository>();
        await repository.InsertAsync(new IdentityUser(id, "adjust-" + id.ToString("N"), id + "@example.test"));
        await uow.CompleteAsync();
        return id;
    }

    private async Task ReadAsync(Func<CollectionDbContext, LedgerDbContext, Task> action)
    {
        using var scope = ServiceProvider.CreateScope();
        using var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWorkManager>().Begin(requiresNew: true, isTransactional: false);
        var db = await scope.ServiceProvider.GetRequiredService<IDbContextProvider<CollectionDbContext>>().GetDbContextAsync();
        var ledger = await scope.ServiceProvider.GetRequiredService<IDbContextProvider<LedgerDbContext>>().GetDbContextAsync();
        await action(db, ledger);
        await uow.CompleteAsync();
    }

    private static CreateCollectionBalanceAdjustmentInput Input(Guid userId, string amount) => new()
    {
        RequestId = Guid.NewGuid(),
        UserId = userId,
        Currency = CollectionCurrency.Usdt,
        Amount = amount,
        Reason = "核对后的用户余额调整",
    };
}
