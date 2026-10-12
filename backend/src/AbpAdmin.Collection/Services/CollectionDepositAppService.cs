using System;
using System.Linq;
using System.Threading.Tasks;
using AbpAdmin.Collection.Chain;
using AbpAdmin.Collection.Contracts;
using AbpAdmin.Collection.Data;
using AbpAdmin.Collection.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Volo.Abp;
using Volo.Abp.Application.Dtos;
using Volo.Abp.Application.Services;
using Volo.Abp.EntityFrameworkCore;
using Volo.Abp.Timing;
using Volo.Abp.Uow;

namespace AbpAdmin.Collection.Services;

/// <summary>我的充值地址（含充值是否开放与用户可读说明）。</summary>
public class MyDepositAddressDto
{
    public string Address { get; set; } = default!;

    public bool DepositEnabled { get; set; }

    public string? Notice { get; set; }
}

/// <summary>我的进款单行（链上转入，含跳过类终态——用户页写明最低额口径）。</summary>
public class MyDepositDto
{
    public string TxHash { get; set; } = default!;

    public string Symbol { get; set; } = default!;

    /// <summary>十进制字符串；PrecisionOverflow 时为 null。</summary>
    public string? Amount { get; set; }

    public string RawAmount { get; set; } = default!;

    public string Status { get; set; } = default!;

    public long BlockNumber { get; set; }

    public DateTime CreationTime { get; set; }
}

/// <summary>
/// 用户侧充值接口（计划第 5.7 节）：地址派生与进款单查询只看 CurrentUser.Id；
/// 这套表没有租户列，租户身份直接拒绝。管理端链上监控（按哈希查进款）随管理页排期。
/// </summary>
[Authorize]
public class CollectionDepositAppService : ApplicationService
{
    private readonly IDbContextProvider<CollectionDbContext> _dbContextProvider;
    private readonly DepositAddressManager _depositAddressManager;
    private readonly Microsoft.Extensions.Configuration.IConfiguration _configuration;
    private readonly IClock _clock;

    public CollectionDepositAppService(
        IDbContextProvider<CollectionDbContext> dbContextProvider,
        DepositAddressManager depositAddressManager,
        Microsoft.Extensions.Configuration.IConfiguration configuration,
        IClock clock)
    {
        _dbContextProvider = dbContextProvider;
        _depositAddressManager = depositAddressManager;
        _configuration = configuration;
        _clock = clock;
    }

    /// <summary>我的 BSC 充值地址（首次调用会派生；扫链未跑过一轮时按计划口径拒绝/说明未开放）。</summary>
    public virtual async Task<MyDepositAddressDto> GetMyDepositAddressAsync()
    {
        AssertHost();
        var networkId = await _depositAddressManager
            .WithMasterKey(_configuration["Collection:MasterKey"])
            .ResolveBscNetworkIdAsync();
        var (address, enabled, notice) = await _depositAddressManager
            .GetOrDeriveAsync(networkId, CurrentUser.Id!.Value);
        return new MyDepositAddressDto { Address = address, DepositEnabled = enabled, Notice = notice };
    }

    /// <summary>我的进款单（倒序分页；含各代币符号映射）。</summary>
    public virtual async Task<PagedResultDto<MyDepositDto>> GetMyDepositOrdersAsync(int skipCount, int maxResultCount)
    {
        AssertHost();
        var userId = CurrentUser.Id!.Value;
        if (maxResultCount <= 0 || maxResultCount > 100)
        {
            maxResultCount = 20;
        }

        using var uow = UnitOfWorkManager.Begin(requiresNew: true, isTransactional: false);
        var dbContext = await _dbContextProvider.GetDbContextAsync();
        var query = dbContext.DepositOrders.AsNoTracking()
            .Where(o => o.UserId == userId);

        var total = await query.CountAsync();
        var symbols = await dbContext.Tokens.AsNoTracking().ToDictionaryAsync(t => t.Id, t => t.Symbol);
        var items = await query
            .OrderByDescending(o => o.CreationTime)
            .Skip(Math.Max(0, skipCount))
            .Take(maxResultCount)
            .ToListAsync();

        await uow.CompleteAsync();
        return new PagedResultDto<MyDepositDto>(
            total,
            items.Select(o => new MyDepositDto
            {
                TxHash = o.TxHash,
                Symbol = symbols.TryGetValue(o.TokenId, out var s) ? s : "?",
                Amount = o.Amount == null ? null : CollectionAmounts.Format(o.Amount.Value),
                RawAmount = o.RawAmount,
                Status = o.Status.ToString(),
                BlockNumber = o.BlockNumber,
                CreationTime = o.CreationTime,
            }).ToList());
    }

    private void AssertHost()
    {
        if (CurrentTenant.Id != null)
        {
            throw new BusinessException(CollectionErrorCodes.HostOnly);
        }
    }
}
