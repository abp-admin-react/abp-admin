using System;
using System.Threading.Tasks;
using AbpAdmin.Ordering.Data;
using AbpAdmin.Ordering.Entities;
using AbpAdmin.Ordering.Settings;
using Microsoft.EntityFrameworkCore;
using Volo.Abp;
using Volo.Abp.Domain.Services;
using Volo.Abp.EntityFrameworkCore;
using Volo.Abp.Settings;
using Volo.Abp.Timing;

namespace AbpAdmin.Ordering.Quotes;

/// <summary>单个支付方式的费率四元组（S 结算底线 / F 固定费 / f 固定费抽取 / r 比例费率）+ 汇率快照。</summary>
public record ChannelQuoteConfig(
    decimal SettlementFloor,
    decimal FixedFee,
    decimal FixedFeeRate,
    decimal PercentRate,
    decimal UsdCnyRate);

/// <summary>
/// 报价管理（计划第 4.2 节）。同一订单、同一支付方式、Active 的报价只有一行：
/// 仍有效的直接返回；已过 ExpiresAt 的在同一事务里标 Expired 再插入新行——
/// 过期作业只扫漏网，不能作为用户能重新报价的前提。
/// Stripe 的报价有效期夹在 30 分钟和 24 小时之间（保存设置时拒绝越界 + 此处钳制）。
/// 汇率每个报价只读一次设置链（快照与 X_min 比较同源同值）。
/// </summary>
public class QuoteManager : DomainService
{
    /// <summary>Stripe 会话有效期下限（创建后至少 30 分钟，计划第 3.1/4.2 节）。
    /// 订单侧在发起支付前据此判断是否需要强制换新报价（StartPayAsync）。</summary>
    public static readonly TimeSpan StripeMinimumValidity = TimeSpan.FromMinutes(30);

    private static readonly TimeSpan StripeMaximumValidity = TimeSpan.FromHours(24);

    private readonly IDbContextProvider<OrderingDbContext> _dbContextProvider;
    private readonly ISettingProvider _settingProvider;
    private readonly IClock _clock;

    public QuoteManager(
        IDbContextProvider<OrderingDbContext> dbContextProvider,
        ISettingProvider settingProvider,
        IClock clock)
    {
        _dbContextProvider = dbContextProvider;
        _settingProvider = settingProvider;
        _clock = clock;
    }

    public virtual async Task<OrdQuote> GetOrCreateActiveQuoteAsync(OrdOrder order, OrderingPayChannel method)
    {
        if (order.OrderType != OrderType.Business)
        {
            throw new BusinessException(OrderingErrorCodes.InvalidOrderStatus)
                .WithData("OrderId", order.Id);
        }

        var dbContext = await _dbContextProvider.GetDbContextAsync();
        var now = _clock.Now;

        var active = await dbContext.Quotes.FirstOrDefaultAsync(q =>
            q.OrderId == order.Id && q.PayMethod == method && q.Status == QuoteStatus.Active);

        if (active != null && active.ExpiresAt > now)
        {
            // 仍有效：直接返回，不重复报价（部分唯一索引也容不下第二行 Active）
            return active;
        }

        if (active != null)
        {
            // 插入新报价的同一事务里，先把该订单该方式已过 ExpiresAt 的 Active 行标成过期
            active.Status = QuoteStatus.Expired;
            await dbContext.SaveChangesAsync();
        }

        return await InsertNewQuoteAsync(order, method, now);
    }

    /// <summary>
    /// 强制换新报价：把该订单该方式的 Active 行标 Expired（无论是否到期）并插入新行。
    /// Stripe 的报价「已过 30 分钟线但未到 ExpiresAt」时 GetOrCreateActiveQuoteAsync 仍返回旧行，
    /// 而 CreateAsync 会按剩余不足 30 分钟拒绝——调用方（StartPayAsync）用本方法先换新报价，
    /// 否则出现每笔支付必败且无新报价可拿的窗口（计划第 3.1/4.2 节）。
    /// </summary>
    public virtual async Task<OrdQuote> RefreshAsync(OrdOrder order, OrderingPayChannel method)
    {
        if (order.OrderType != OrderType.Business)
        {
            throw new BusinessException(OrderingErrorCodes.InvalidOrderStatus)
                .WithData("OrderId", order.Id);
        }

        var dbContext = await _dbContextProvider.GetDbContextAsync();
        var now = _clock.Now;

        var active = await dbContext.Quotes.FirstOrDefaultAsync(q =>
            q.OrderId == order.Id && q.PayMethod == method && q.Status == QuoteStatus.Active);
        if (active != null)
        {
            active.Status = QuoteStatus.Expired;
            await dbContext.SaveChangesAsync();
        }

        return await InsertNewQuoteAsync(order, method, now);
    }

    private async Task<OrdQuote> InsertNewQuoteAsync(OrdOrder order, OrderingPayChannel method, DateTime now)
    {
        var dbContext = await _dbContextProvider.GetDbContextAsync();
        var (config, currency, basis) = await ResolveMethodBasisAsync(order, method);
        AssertNotBelowMinimum(order, method, config);

        var expiresAt = now + await GetValidityAsync();
        if (method == OrderingPayChannel.StripeCheckout)
        {
            // Stripe 的报价有效期夹在 30 分钟与 24 小时之间（计划第 4.2 节）
            var min = now.Add(StripeMinimumValidity);
            var max = now.Add(StripeMaximumValidity);
            expiresAt = expiresAt < min ? min : (expiresAt > max ? max : expiresAt);
        }

        var quote = new OrdQuote(GuidGenerator.Create())
        {
            OrderId = order.Id,
            PayMethod = method,
            PayCurrency = currency,
            PayAmount = QuoteFeeMath.ComputePayAmount(basis, config.PercentRate, config.FixedFee),
            UsdCnyRate = config.UsdCnyRate,
            Status = QuoteStatus.Active,
            ExpiresAt = expiresAt,
            CreationTime = now,
        };

        await dbContext.Quotes.AddAsync(quote);
        await dbContext.SaveChangesAsync();
        return quote;
    }

    /// <summary>按方式读费率并算报价基数（微信/支付宝 = Amount × UsdCnyRate；Stripe/余额 = Amount）。</summary>
    private async Task<(ChannelQuoteConfig Config, AbpAdmin.Collection.Contracts.CollectionCurrency Currency, decimal Basis)> ResolveMethodBasisAsync(
        OrdOrder order, OrderingPayChannel method)
    {
        var usdCny = await GetDecimalAsync(OrderingSettings.Quote.UsdCnyRate, OrderingSettings.Defaults.UsdCnyRateValue);
        var amount = order.Amount ?? throw new BusinessException(OrderingErrorCodes.InvalidOrderStatus)
            .WithData("OrderId", order.Id);

        return method switch
        {
            OrderingPayChannel.WechatNative => (await GetConfigAsync(
                OrderingSettings.Quote.WechatSettlementFloor, OrderingSettings.Quote.WechatFixedFee,
                OrderingSettings.Quote.WechatFixedFeeRate, OrderingSettings.Quote.WechatPercentRate, usdCny),
                AbpAdmin.Collection.Contracts.CollectionCurrency.Cny, amount * usdCny),
            OrderingPayChannel.AlipayPrecreate => (await GetConfigAsync(
                OrderingSettings.Quote.AlipaySettlementFloor, OrderingSettings.Quote.AlipayFixedFee,
                OrderingSettings.Quote.AlipayFixedFeeRate, OrderingSettings.Quote.AlipayPercentRate, usdCny),
                AbpAdmin.Collection.Contracts.CollectionCurrency.Cny, amount * usdCny),
            OrderingPayChannel.StripeCheckout => (await GetConfigAsync(
                OrderingSettings.Quote.StripeSettlementFloor, OrderingSettings.Quote.StripeFixedFee,
                OrderingSettings.Quote.StripeFixedFeeRate, OrderingSettings.Quote.StripePercentRate, usdCny),
                AbpAdmin.Collection.Contracts.CollectionCurrency.Usd, amount),
            OrderingPayChannel.BalanceUsdt => (await GetConfigAsync(
                OrderingSettings.Quote.BalanceUsdtSettlementFloor, OrderingSettings.Quote.BalanceUsdtFixedFee,
                OrderingSettings.Quote.BalanceUsdtFixedFeeRate, OrderingSettings.Quote.BalanceUsdtPercentRate, usdCny),
                AbpAdmin.Collection.Contracts.CollectionCurrency.Usdt, amount),
            OrderingPayChannel.BalanceUsdc => (await GetConfigAsync(
                OrderingSettings.Quote.BalanceUsdcSettlementFloor, OrderingSettings.Quote.BalanceUsdcFixedFee,
                OrderingSettings.Quote.BalanceUsdcFixedFeeRate, OrderingSettings.Quote.BalanceUsdcPercentRate, usdCny),
                AbpAdmin.Collection.Contracts.CollectionCurrency.Usdc, amount),
            _ => throw new ArgumentOutOfRangeException(nameof(method), method, null),
        };
    }

    /// <summary>
    /// X_min 校验（下限与渠道上限各管一头，计划第 4.2 节）：分母小于等于 0 → 配置非法；
    /// 分子小于等于 0 → 没有下限；微信、支付宝的 X_min 是 CNY，与 Amount × UsdCnyRate 比较，
    /// 不能拿 CNY 下限直接和 USD 金额比（表驱动测试钉住，计划第 8 节）。
    /// </summary>
    private static void AssertNotBelowMinimum(OrdOrder order, OrderingPayChannel method, ChannelQuoteConfig config)
    {
        if (!QuoteFeeMath.TryComputeXMin(
                config.SettlementFloor, config.FixedFee, config.FixedFeeRate, config.PercentRate,
                out var xMin, out var error))
        {
            throw new BusinessException(OrderingErrorCodes.InvalidQuoteConfig).WithData("Error", error ?? "invalid fee config");
        }

        if (xMin == null)
        {
            return;
        }

        var comparable = method is OrderingPayChannel.WechatNative or OrderingPayChannel.AlipayPrecreate
            ? order.Amount!.Value * config.UsdCnyRate
            : order.Amount!.Value;

        if (comparable < xMin.Value)
        {
            throw new BusinessException(OrderingErrorCodes.BelowMinimumAmount)
                .WithData("Minimum", xMin.Value)
                .WithData("Method", method);
        }
    }

    private async Task<ChannelQuoteConfig> GetConfigAsync(
        string settlementFloorKey, string fixedFeeKey, string fixedFeeRateKey, string percentRateKey, decimal usdCny)
    {
        return new ChannelQuoteConfig(
            await GetDecimalAsync(settlementFloorKey, 0m),
            await GetDecimalAsync(fixedFeeKey, 0m),
            await GetDecimalAsync(fixedFeeRateKey, 0m),
            await GetDecimalAsync(percentRateKey, OrderingSettings.Defaults.DefaultPercentRateValue),
            usdCny);
    }

    private async Task<TimeSpan> GetValidityAsync()
    {
        var minutes = (int)await GetDecimalAsync(
            OrderingSettings.Quote.ValidityMinutes, OrderingSettings.Defaults.ValidityMinutes);
        if (minutes < OrderingSettings.Defaults.ValidityMinutes)
        {
            // 微信、支付宝报价的有效期不得短于用户完成扫码支付所需的时间（计划第 4.2 节）
            minutes = OrderingSettings.Defaults.ValidityMinutes;
        }

        return TimeSpan.FromMinutes(minutes);
    }

    /// <summary>读十进制设置；空值/非法值回落默认（业务代码不直接 decimal.Parse，计划第 2 节）。</summary>
    private async Task<decimal> GetDecimalAsync(string key, decimal fallback)
    {
        var raw = await _settingProvider.GetOrNullAsync(key);
        return AbpAdmin.Collection.Contracts.CollectionAmounts.TryParse(raw, out var value) ? value : fallback;
    }
}
