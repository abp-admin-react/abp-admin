using System;
using System.Threading.Tasks;
using Volo.Abp.Identity;
using Volo.Abp.Uow;

namespace AbpAdmin.TokenGateway.Core;

/// <summary>
/// 凭证消费时的身份账户状态门（NV-08）：hb-/hbs- 凭证一经签发不随身份状态联动
/// （签发时刻查过 IsActive 不代表此刻仍有效），停用/锁定/已删账户的存量凭证
/// 可继续调用 /v1/* 与钱包面。KeyStore 在解析出 userId 后必须过这道门，
/// 让「身份被停用」即刻等于「凭证失效」，不再依赖运营手工删 TgApiKeys 行。
/// 生产实现查 Identity 用户库；单测夹具注入桩。
/// </summary>
public interface ITgUserGate
{
    /// <summary>用户必须存在、未停用、未处于锁定期；否则凭证按无效键拒绝（与未知 Key 同错，不泄露状态差异）。</summary>
    Task<bool> IsUsableAsync(Guid userId);
}

/// <summary>生产实现：Identity 用户库 PK 查询（走主键索引，热路径可承受）。</summary>
public class IdentityUserGate(IIdentityUserRepository users, IUnitOfWorkManager uowManager) : ITgUserGate
{
    public async Task<bool> IsUsableAsync(Guid userId)
    {
        // KeyStore 走自己的 DbContextFactory（无环境 UoW）；宿主控制器同样不套 UoW。
        // Identity 仓储需要 UoW 上下文：无环境 UoW 时开短事务自持，有则直接用。
        if (uowManager.Current != null)
        {
            return await CheckAsync(userId);
        }

        using var uow = uowManager.Begin();
        var usable = await CheckAsync(userId);
        await uow.CompleteAsync();
        return usable;
    }

    private async Task<bool> CheckAsync(Guid userId)
    {
        var user = await users.FindAsync(userId);
        if (user is null || !user.IsActive)
        {
            return false;
        }

        return !(user.LockoutEnd.HasValue && user.LockoutEnd.Value > DateTimeOffset.UtcNow);
    }
}
