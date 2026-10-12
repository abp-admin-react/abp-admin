using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AbpAdmin.Ordering.Data;
using AbpAdmin.Ordering.Entities;
using AbpAdmin.Ordering.Orders;
using AbpAdmin.Ordering.Quotes;
using AbpAdmin.Ordering.Services.Dtos;
using AbpAdmin.Ordering.Settings;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Volo.Abp;
using Volo.Abp.Application.Dtos;
using Volo.Abp.Application.Services;
using Volo.Abp.EntityFrameworkCore;
using Volo.Abp.Settings;
using Volo.Abp.Timing;
using Volo.Abp.Uow;

namespace AbpAdmin.Ordering.Services;

/// <summary>
/// 用户侧订单应用服务（/api/app/order）。只查 CurrentUser.Id 的订单；
/// 这两套表没有租户列——租户身份调用直接拒绝（Host 守卫，计划第 2 节）。
/// 发起支付的方法用非事务 UoW：渠道调用（取消/创建的 HTTP 部分）不得坐在数据库事务里，
/// 事务边界由 OrderPaymentManager 显式控制（余额扣款 + 标支付的 requiresNew 工作单元）。
/// </summary>
[Authorize]
public class OrderAppService : ApplicationService
{
    private readonly IDbContextProvider<OrderingDbContext> _dbContextProvider;
    private readonly QuoteManager _quoteManager;
    private readonly OrderPaymentManager _orderPaymentManager;
    private readonly ISettingProvider _settingProvider;
    private readonly IClock _clock;
    private readonly IUnitOfWorkManager _unitOfWorkManager;

    public OrderAppService(
        IDbContextProvider<OrderingDbContext> dbContextProvider,
        QuoteManager quoteManager,
        OrderPaymentManager orderPaymentManager,
        ISettingProvider settingProvider,
        IClock clock,
        IUnitOfWorkManager unitOfWorkManager)
    {
        _dbContextProvider = dbContextProvider;
        _quoteManager = quoteManager;
        _orderPaymentManager = orderPaymentManager;
        _settingProvider = settingProvider;
        _clock = clock;
        _unitOfWorkManager = unitOfWorkManager;
    }

    /// <summary>创建业务单（待支付）。商品目录、库存、物流不在本计划范围。</summary>
    public virtual async Task<OrderDto> CreateAsync(CreateOrderInput input)
    {
        AssertHost();

        var amount = AbpAdmin.Collection.Contracts.CollectionAmounts.Parse(input.Amount);
        // 业务单标价 numeric(18,2)：两位小数 + 正数 + 不超列上限，越界给业务错误而不是 PG 22003 500
        if (amount <= 0 || decimal.Round(amount, 2) != amount || amount >= 10_000_000_000_000_000m)
        {
            throw new BusinessException(OrderingErrorCodes.InvalidOrderInput)
                .WithData("Amount", input.Amount);
        }

        if (string.IsNullOrWhiteSpace(input.Subject)
            || input.Subject.Trim().Length > OrderingConsts.MaxSubjectLength
            || OrderServiceHelpers.HasControlChars(input.Subject))
        {
            throw new BusinessException(OrderingErrorCodes.InvalidOrderInput)
                .WithData("Subject", "Subject is required, at most " + OrderingConsts.MaxSubjectLength + " chars, control characters not allowed.");
        }

        var dbContext = await _dbContextProvider.GetDbContextAsync();
        var order = new OrdOrder(GuidGenerator.Create())
        {
            OrderNo = OrderServiceHelpers.NextOrderNo(_clock),
            UserId = CurrentUser.Id!.Value,
            OrderType = OrderType.Business,
            Subject = input.Subject.Trim(),
            Amount = amount,
            Currency = "USD",
            Status = OrderStatus.PendingPayment,
            ExpiresAt = _clock.Now.AddHours(await GetExpiryHoursAsync()),
            CreationTime = _clock.Now,
        };

        await dbContext.Orders.AddAsync(order);
        await dbContext.SaveChangesAsync();
        return Map(order, null);
    }

    public virtual async Task<OrderDto> GetAsync(Guid id)
    {
        AssertHost();

        var dbContext = await _dbContextProvider.GetDbContextAsync();
        var order = await dbContext.Orders.AsNoTracking()
            .FirstOrDefaultAsync(o => o.Id == id && o.UserId == CurrentUser.Id!.Value);
        if (order == null)
        {
            throw new BusinessException(OrderingErrorCodes.OrderNotFound).WithData("OrderId", id);
        }

        var linesByOrder = await OrderServiceHelpers.LoadAnomalyLinesBatchedAsync(dbContext, new[] { order.Id });
        return Map(order, linesByOrder.GetValueOrDefault(order.Id));
    }

    public virtual async Task<PagedResultDto<OrderDto>> GetListAsync(OrderListFilterDto filter)
    {
        AssertHost();

        var dbContext = await _dbContextProvider.GetDbContextAsync();
        var query = dbContext.Orders.AsNoTracking()
            .Where(o => o.UserId == CurrentUser.Id!.Value);

        query = OrderServiceHelpers.ApplyFilter(query, filter);

        var total = await query.CountAsync();
        // 真分页（评审遗留核实为真问题：原先硬顶 50 无 Skip，第 51 笔起永远取不到）
        var (skip, take) = OrderServiceHelpers.NormalizePaging(filter, def: 20, cap: 100);
        var items = await query
            .OrderByDescending(o => o.CreationTime)
            .Skip(skip)
            .Take(take)
            .ToListAsync();

        // 本页异常明细批量装配（退款记录/多笔到账告警，用户侧自己的单自己可见）
        var linesByOrder = await OrderServiceHelpers.LoadAnomalyLinesBatchedAsync(dbContext, items.Select(o => o.Id));

        return new PagedResultDto<OrderDto>(total,
            items.Select(o => Map(o, linesByOrder.GetValueOrDefault(o.Id))).ToList());
    }

    /// <summary>
    /// 按余额流水 Id 反查自己的订单号（流水页「关联订单」跳转用）：
    /// 扣款/回冲流水 → 订单。只查 CurrentUser 名下的单，不泄露他人订单号。
    /// </summary>
    public virtual async Task<List<OrderRefDto>> GetOrderRefsByBalanceTxnAsync(List<Guid> balanceTxnIds)
    {
        AssertHost();
        if (balanceTxnIds == null || balanceTxnIds.Count == 0)
        {
            return new List<OrderRefDto>();
        }

        // 去重 + 上限（合法调用方每页 ≤200；不设防则客户端可拉出任意大的 IN 探测）
        var probeIds = balanceTxnIds.Distinct().Take(200).ToList();

        var dbContext = await _dbContextProvider.GetDbContextAsync();
        var refs = await dbContext.Orders.AsNoTracking()
            .Where(o => o.UserId == CurrentUser.Id!.Value && o.BalanceTxnId != null
                        && probeIds.Contains(o.BalanceTxnId.Value))
            .Select(o => new OrderRefDto { BalanceTxnId = o.BalanceTxnId!.Value, OrderId = o.Id, OrderNo = o.OrderNo })
            .ToListAsync();
        return refs;
    }

    /// <summary>获取（必要时创建）某支付方式的有效报价。</summary>
    public virtual async Task<QuoteDto> GetQuoteAsync(Guid orderId, string payMethod)
    {
        AssertHost();

        var order = await GetOwnOrderAsync(orderId);
        var quote = await _quoteManager.GetOrCreateActiveQuoteAsync(order, ParsePayMethod(payMethod));
        return Map(quote);
    }

    /// <summary>
    /// 发起支付（换渠道，或余额）。换渠道/改余额之前先取消旧收款；取消返回已完成则不建新单、不扣款
    ///（计划第 3.1、6 节）。渠道调用在 requiresNew 提交之后、扣款工作单元打开之前完成。
    /// 事务边界显式控制：方法内开 requiresNew 非事务工作单元——Auto API 的 UnitOfWork 中间件
    /// 按 HTTP 动词（POST=事务）建环境 UoW，方法级 [UnitOfWork] 特性不改变已建 UoW 的选项，
    /// 报价写库会被请求后段抛出的渠道异常连带回滚（E2E 实测）。显式 requiresNew 之后行为不再
    /// 依赖特性传导：报价与支付字段各条语句即时提交，渠道 HTTP 不坐在数据库事务里；
    /// 余额路径仍由 OrderPaymentManager 自带 requiresNew 事务保证扣款+标支付原子。
    /// </summary>
    [UnitOfWork(isTransactional: false)]
    public virtual async Task<StartPayResultDto> StartPayAsync(Guid orderId, string payMethod)
    {
        AssertHost();

        using var uow = _unitOfWorkManager.Begin(requiresNew: true, isTransactional: false);
        var order = await GetOwnOrderAsync(orderId);
        var method = ParsePayMethod(payMethod);
        var quote = await _quoteManager.GetOrCreateActiveQuoteAsync(order, method);

        // 报价过期后不能支付，过期之后立刻重新报价——翻状态由 QuoteManager 内部完成；
        ///Stripe 另有一条：剩余时间不足 30 分钟时 CreateAsync 会拒绝且不插行（计划第 3.1 节），
        ///而 GetOrCreateActiveQuoteAsync 只按「未过期」返回旧 Active 报价，不主动刷新——
        ///不在这里强制换新报价的话，报价会在「已过 30 分钟线但未到 ExpiresAt」的窗口里
        ///每笔支付都失败且无新报价可拿（评审 M 级）
        if (method == OrderingPayChannel.StripeCheckout
            && quote.ExpiresAt - _clock.Now < Quotes.QuoteManager.StripeMinimumValidity)
        {
            quote = await _quoteManager.RefreshAsync(order, method);
        }

        var result = method.IsBalance()
            ? await _orderPaymentManager.PayWithBalanceAsync(order, quote)
            : await _orderPaymentManager.StartChannelPayAsync(order, quote);

        await uow.CompleteAsync();
        return new StartPayResultDto
        {
            Outcome = result.Outcome.ToString(),
            Order = Map(result.Order, null),
            ChannelPayload = result.Collection?.ChannelPayload,
            CollectionId = result.Collection?.Id,
        };
    }

    /// <summary>
    /// 用户在页面上取消（CloseReason = UserCancel；只对待支付订单）。取消要先调渠道关单
    ///（HTTP 不进数据库事务），同样用显式 requiresNew 非事务工作单元。
    /// </summary>
    [UnitOfWork(isTransactional: false)]
    public virtual async Task<OrderDto> CancelAsync(Guid id)
    {
        AssertHost();

        using var uow = _unitOfWorkManager.Begin(requiresNew: true, isTransactional: false);
        var order = await GetOwnOrderAsync(id);
        await _orderPaymentManager.CancelByUserAsync(order);
        await uow.CompleteAsync();
        return Map(await GetOwnOrderAsync(id), null);
    }

    // ── 内部 ─────────────────────────────────────────────────────────────────────────────

    protected virtual void AssertHost()
    {
        // 这两套表没有租户列：租户身份调用直接拒绝（计划第 2 节；规则单一出处）
        OrderServiceHelpers.ThrowIfNotHost(CurrentTenant.Id);
    }

    private async Task<OrdOrder> GetOwnOrderAsync(Guid id)
    {
        var dbContext = await _dbContextProvider.GetDbContextAsync();
        var order = await dbContext.Orders.AsNoTracking()
            .FirstOrDefaultAsync(o => o.Id == id && o.UserId == CurrentUser.Id!.Value);
        return order ?? throw new BusinessException(OrderingErrorCodes.OrderNotFound).WithData("OrderId", id);
    }

    private async Task<int> GetExpiryHoursAsync()
    {
        var raw = await _settingProvider.GetOrNullAsync(OrderingSettings.Order.ExpiryHours);
        if (!AbpAdmin.Collection.Contracts.CollectionAmounts.TryParse(raw, out var hours) || hours < 1m)
        {
            return OrderingSettings.Defaults.ExpiryHours;
        }

        // 配置成 0.5 这类小数时截断为 0 会造出「落地即过期」的订单——向上取整到 1 小时
        return hours >= int.MaxValue ? int.MaxValue : Math.Max(1, (int)Math.Ceiling(hours));
    }

    private static OrderingPayChannel ParsePayMethod(string payMethod)
    {
        // IsDefined 挡住未定义数值（Enum.TryParse 对 "99" 这类数字串返回 true，评审 M 级）
        if (!Enum.TryParse<OrderingPayChannel>(payMethod, ignoreCase: true, out var method)
            || !Enum.IsDefined(typeof(OrderingPayChannel), method))
        {
            throw new BusinessException(OrderingErrorCodes.PayMethodUnavailable).WithData("Method", payMethod);
        }

        return method;
    }

    private static QuoteDto Map(OrdQuote quote)
    {
        return new QuoteDto
        {
            Id = quote.Id,
            OrderId = quote.OrderId,
            PayMethod = quote.PayMethod.ToString(),
            PayCurrency = quote.PayCurrency.ToString(),
            PayAmount = AbpAdmin.Collection.Contracts.CollectionAmounts.Format(quote.PayAmount),
            UsdCnyRate = AbpAdmin.Collection.Contracts.CollectionAmounts.Format(quote.UsdCnyRate),
            Status = quote.Status.ToString(),
            ExpiresAt = quote.ExpiresAt,
        };
    }

    internal static OrderDto Map(OrdOrder order, System.Collections.Generic.List<OrderAnomalyLineDto>? lines)
    {
        return new OrderDto
        {
            Id = order.Id,
            OrderNo = order.OrderNo,
            UserId = order.UserId,
            OrderType = order.OrderType.ToString(),
            Subject = order.Subject,
            Amount = order.Amount == null ? null : AbpAdmin.Collection.Contracts.CollectionAmounts.Format(order.Amount.Value),
            Currency = order.Currency,
            Status = order.Status.ToString(),
            PayChannel = order.PayChannel?.ToString(),
            PayAmount = order.PayAmount == null ? null : AbpAdmin.Collection.Contracts.CollectionAmounts.Format(order.PayAmount.Value),
            PayCurrency = order.PayCurrency?.ToString(),
            PaidAt = order.PaidAt,
            ChannelTradeNo = order.ChannelTradeNo,
            FulfilledAt = order.FulfilledAt,
            ClosedAt = order.ClosedAt,
            RefundedAmount = order.RefundedAmount == null ? null : AbpAdmin.Collection.Contracts.CollectionAmounts.Format(order.RefundedAmount.Value),
            QuoteId = order.QuoteId,
            CollectionId = order.CollectionId,
            BalanceTxnId = order.BalanceTxnId,
            ExpiresAt = order.ExpiresAt,
            CloseReason = order.CloseReason?.ToString(),
            PaymentAnomaly = order.PaymentAnomaly,
            CreationTime = order.CreationTime,
            AnomalyLines = lines,
        };
    }
}
