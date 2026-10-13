using System;
using System.Linq;
using System.Threading.Tasks;
using AbpAdmin.Collection.Data;
using AbpAdmin.Ordering.Data;
using AbpAdmin.Ordering.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Volo.Abp.Data;
using Volo.Abp.EntityFrameworkCore;
using Volo.Abp.Uow;
using Xunit;

namespace AbpAdmin.Ordering;

/// <summary>
/// M1 的第一条测试（计划第 7、8 节）：两个 DbContext 都声明连接串名 Default，
/// 并在同一工作单元里写入两边的行，失败时两边都回滚。这项没通过之前不写订单状态机——
/// 连接串名不是 Default，或同一工作单元不能一起回滚，就算失败。
/// </summary>
public class SharedTransactionRollbackTests : AbpAdminTestBase<SharedTransactionTestModule>
{
    [Fact]
    public async Task Both_Contexts_Declare_Default_Connection_String_Name()
    {
        var orderingAttr = typeof(OrderingDbContext).GetCustomAttributes(typeof(ConnectionStringNameAttribute), inherit: false)
            .Cast<ConnectionStringNameAttribute>().Single();
        var collectionAttr = typeof(CollectionDbContext).GetCustomAttributes(typeof(ConnectionStringNameAttribute), inherit: false)
            .Cast<ConnectionStringNameAttribute>().Single();

        // 连接串名必须是字面 "Default"：否则 ABP 不会让它们共享同一个数据库事务，
        // 扣款 + 标支付「一起提交或一起回滚」静默消失（计划第 2 节）
        orderingAttr.Name.ShouldBe("Default");
        collectionAttr.Name.ShouldBe("Default");
    }

    [Fact]
    public async Task Rows_On_Both_Sides_Commit_Together()
    {
        var orderId = Guid.NewGuid();
        var collectionId = Guid.NewGuid();

        using (var scope = ServiceProvider.CreateScope())
        {
            var uowManager = scope.ServiceProvider.GetRequiredService<IUnitOfWorkManager>();
            using var uow = uowManager.Begin(requiresNew: true, isTransactional: true);
            await InsertOrderRowAsync(scope, orderId);
            await InsertCollectionRowAsync(scope, collectionId, orderId);
            await uow.CompleteAsync();
        }

        // 提交之后另一个 scope 两边都能读到（与「CreateAsync 返回后另一个 scope 能读到 Pending 行」同族）
        using (var scope = ServiceProvider.CreateScope())
        {
            using var readUow = scope.ServiceProvider.GetRequiredService<IUnitOfWorkManager>().Begin();
            var ordering = scope.ServiceProvider.GetRequiredService<IDbContextProvider<OrderingDbContext>>();
            var collection = scope.ServiceProvider.GetRequiredService<IDbContextProvider<CollectionDbContext>>();
            (await (await ordering.GetDbContextAsync()).Orders.IgnoreQueryFilters()
                .AnyAsync(o => o.Id == orderId)).ShouldBeTrue();
            (await (await collection.GetDbContextAsync()).Collections.IgnoreQueryFilters()
                .AnyAsync(c => c.Id == collectionId)).ShouldBeTrue();
        }
    }

    [Fact]
    public async Task Failure_Rolls_Back_Both_Sides()
    {
        var orderId = Guid.NewGuid();
        var collectionId = Guid.NewGuid();

        using (var scope = ServiceProvider.CreateScope())
        {
            var uowManager = scope.ServiceProvider.GetRequiredService<IUnitOfWorkManager>();
            using var uow = uowManager.Begin(requiresNew: true, isTransactional: true);
            await InsertOrderRowAsync(scope, orderId);
            await InsertCollectionRowAsync(scope, collectionId, orderId);

            // 不 Complete 直接 Dispose（等价于「更新失败抛出」路径）：两边一起回滚
        }

        using (var scope = ServiceProvider.CreateScope())
        {
            using var readUow = scope.ServiceProvider.GetRequiredService<IUnitOfWorkManager>().Begin();
            var ordering = scope.ServiceProvider.GetRequiredService<IDbContextProvider<OrderingDbContext>>();
            var collection = scope.ServiceProvider.GetRequiredService<IDbContextProvider<CollectionDbContext>>();
            (await (await ordering.GetDbContextAsync()).Orders.IgnoreQueryFilters()
                .AnyAsync(o => o.Id == orderId)).ShouldBeFalse();
            (await (await collection.GetDbContextAsync()).Collections.IgnoreQueryFilters()
                .AnyAsync(c => c.Id == collectionId)).ShouldBeFalse();
        }
    }

    private static async Task InsertOrderRowAsync(IServiceScope scope, Guid orderId)
    {
        var ordering = await scope.ServiceProvider
            .GetRequiredService<IDbContextProvider<OrderingDbContext>>()
            .GetDbContextAsync();
        await ordering.Orders.AddAsync(new OrdOrder(orderId)
        {
            OrderNo = "ORD-TEST-" + orderId.ToString("N")[..8],
            UserId = Guid.NewGuid(),
            OrderType = OrderType.Business,
            Subject = "shared-txn",
            Amount = 10m,
            Currency = "USD",
            Status = OrderStatus.PendingPayment,
            ExpiresAt = DateTime.UtcNow.AddHours(1),
            CreationTime = DateTime.UtcNow,
        });
        await ordering.SaveChangesAsync();
    }

    private static async Task InsertCollectionRowAsync(IServiceScope scope, Guid collectionId, Guid orderId)
    {
        var collection = await scope.ServiceProvider
            .GetRequiredService<IDbContextProvider<CollectionDbContext>>()
            .GetDbContextAsync();
        await collection.Collections.AddAsync(new Collection.Entities.ColCollection(collectionId)
        {
            CollectionNo = "COL-TEST-" + collectionId.ToString("N")[..8],
            UserId = Guid.NewGuid(),
            BusinessType = OrderingConsts.OrderBusinessType,
            BusinessId = orderId,
            Channel = Collection.Contracts.CollectionChannel.WechatNative,
            Currency = Collection.Contracts.CollectionCurrency.Cny,
            Amount = 72m,
            Status = Collection.Contracts.CollectionStatus.Pending,
            ExpiresAt = DateTime.UtcNow.AddHours(1),
            CreationTime = DateTime.UtcNow,
        });
        await collection.SaveChangesAsync();
    }
}
