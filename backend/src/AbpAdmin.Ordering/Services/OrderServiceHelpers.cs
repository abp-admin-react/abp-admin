using System;
using System.Linq;
using System.Threading.Tasks;
using AbpAdmin.Ordering.Data;
using AbpAdmin.Ordering.Entities;
using Microsoft.EntityFrameworkCore;
using AbpAdmin.Ordering.Services.Dtos;
using AbpAdmin.Ordering.Settings;
using Volo.Abp;
using Volo.Abp.Timing;

namespace AbpAdmin.Ordering.Services;

/// <summary>
/// 订单服务共享助手：用户端与管理端两个应用服务跑同一张表，列表过滤、明细映射、
/// Host 守卫、单号生成各自只有一份（评审 M 级：双份漂移会让用户视图与 Host 视图分叉）。
/// </summary>
internal static class OrderServiceHelpers
{
    /// <summary>订单号唯一生成规则：ORD + 毫秒级时间戳 + 6 位随机数（唯一索引兜底）。</summary>
    public static string NextOrderNo(IClock clock)
    {
        return "ORD" + clock.Now.ToString("yyyyMMddHHmmssfff")
               + System.Random.Shared.Next(100000, 1000000)
                   .ToString(System.Globalization.CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// 用户可输入的文本列（Subject、退款 Note）拒收控制字符：PG 的 text 列不接受 NUL，
    /// 放过去会是 500 而不是业务错误（对抗测试实证：subject 含 \u0000 → SqlState 22021）。
    /// </summary>
    public static bool HasControlChars(string text)
    {
        foreach (var c in text)
        {
            if (char.IsControl(c))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>这两套表没有租户列：租户身份调用直接拒绝（计划第 2 节）。</summary>
    public static void ThrowIfNotHost(Guid? tenantId)
    {
        if (tenantId != null)
        {
            throw new BusinessException(OrderingErrorCodes.HostOnly);
        }
    }

    public static OrderAnomalyLineDto Map(OrdPaymentAnomalyLine line)
    {
        return new OrderAnomalyLineDto
        {
            Id = line.Id,
            Amount = line.Amount == null ? null : AbpAdmin.Collection.Contracts.CollectionAmounts.Format(line.Amount.Value),
            Currency = line.Currency?.ToString(),
            CollectionId = line.CollectionId,
            ChannelTradeNo = line.ChannelTradeNo,
            Note = line.Note,
            CreationTime = line.CreationTime,
        };
    }

    /// <summary>列表分页钳制（用户端与管理端共用）：偏移不小于 0，页大小落在 (0, cap]，缺省用 def。</summary>
    public static (int Skip, int Take) NormalizePaging(OrderListFilterDto filter, int def, int cap)
    {
        var skip = Math.Max(0, filter.SkipCount);
        var take = filter.MaxResultCount <= 0 ? def : Math.Min(filter.MaxResultCount, cap);
        return (skip, take);
    }

    /// <summary>订单异常明细读取（用户端与管理端共用，单一出处——两类视图不分叉，评审 M 级）。</summary>
    /// <summary>一批订单的异常明细一次查回分组（用户列表页的退款/多笔到账可见性用）。</summary>
    public static async Task<System.Collections.Generic.Dictionary<Guid, System.Collections.Generic.List<Dtos.OrderAnomalyLineDto>>>
        LoadAnomalyLinesBatchedAsync(OrderingDbContext dbContext, System.Collections.Generic.IEnumerable<Guid> orderIds)
    {
        var ids = orderIds.ToList();
        if (ids.Count == 0)
        {
            return new();
        }

        var lines = await dbContext.PaymentAnomalyLines.AsNoTracking()
            .Where(l => ids.Contains(l.OrderId))
            .OrderBy(l => l.CreationTime)
            .ToListAsync();
        return lines
            .GroupBy(l => l.OrderId)
            .ToDictionary(g => g.Key, g => g.Select(Map).ToList());
    }

    public static async Task<System.Collections.Generic.List<Dtos.OrderAnomalyLineDto>> LoadAnomalyLinesAsync(
        OrderingDbContext dbContext, Guid orderId)
    {
        var lines = await dbContext.PaymentAnomalyLines.AsNoTracking()
            .Where(l => l.OrderId == orderId)
            .OrderBy(l => l.CreationTime)
            .ToListAsync();
        return lines.Select(Map).ToList();
    }

    /// <summary>OrderListFilterDto（用户端与管理端共用契约）的过滤规则，单一出处。</summary>
    public static IQueryable<OrdOrder> ApplyFilter(IQueryable<OrdOrder> query, OrderListFilterDto filter)
    {
        if (!string.IsNullOrEmpty(filter.OrderType) && Enum.TryParse<OrderType>(filter.OrderType, out var type))
        {
            query = query.Where(o => o.OrderType == type);
        }

        if (!string.IsNullOrEmpty(filter.Status) && Enum.TryParse<OrderStatus>(filter.Status, out var status))
        {
            query = query.Where(o => o.Status == status);
        }

        if (filter.HasAnomaly == true)
        {
            query = query.Where(o => o.PaymentAnomaly);
        }

        if (filter.UserId != null)
        {
            query = query.Where(o => o.UserId == filter.UserId);
        }

        return query;
    }
}
