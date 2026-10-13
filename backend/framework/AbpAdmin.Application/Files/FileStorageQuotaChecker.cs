using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AbpAdmin.Features;
using EasyAbp.FileManagement.Files;
using Microsoft.Extensions.DependencyInjection;
using Volo.Abp;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Features;
using Volo.Abp.Linq;
using Volo.Abp.MultiTenancy;
using Volo.Abp.Uow;
using File = EasyAbp.FileManagement.Files.File;

namespace AbpAdmin.Files;

/// <summary>
/// T4.6：创建文件前按租户 Feature 校验存储配额。0 或未配置表示不限制。
/// 授权 handler 是单例，这里也按单例注册，每次检查自己开 scope + 新 UoW，避免 captive DbContext，
/// 也避免 GetQueryableAsync 在没有环境工作单元时拿到已释放的上下文。
/// 占用统计走 SQL SUM（只选 ByteSize，且显式限定当前租户以利用 (TenantId, FileType) 索引），
/// 禁止把全部 RegularFile 实体拉进内存。
///
/// 并发取舍（check-then-act 竞态）：
/// 同一租户的检查用进程内信号量串行化，缩小「SUM 检查 → 文件行落库」的竞态窗口，
/// 同时避免并发上传对同一租户触发的多路全表聚合。但文件行在外层请求的 UoW 提交时才写入，
/// 本进程锁覆盖不到提交点——严格防超限需要记账表（TenantStorageUsage.UsedBytes）
/// + 原子 UPDATE 判定，属于中期方案；
/// 多实例部署下进程锁同样不足。当前接受「软超限」：极小概率的少量超额，
/// 由配额阈值与后台对账兜底。
/// </summary>
public class FileStorageQuotaChecker : ISingletonDependency
{
    private static readonly ConcurrentDictionary<Guid, SemaphoreSlim> TenantGates = new();

    private readonly IServiceScopeFactory _scopeFactory;

    public FileStorageQuotaChecker(IServiceScopeFactory scopeFactory)
    {
        _scopeFactory = scopeFactory;
    }

    /// <param name="incomingBytes">本次将写入的字节数。&lt;=0 视为无增量，直接通过。</param>
    /// <exception cref="BusinessException">已用 + 增量超过配额。</exception>
    public virtual async Task CheckAsync(long incomingBytes)
    {
        if (incomingBytes <= 0)
        {
            return;
        }

        // ICurrentTenant 基于 AsyncLocal，单例注册，子 scope 内解析到的是同一租户上下文
        using (var probeScope = _scopeFactory.CreateScope())
        {
            var tenantId = probeScope.ServiceProvider.GetRequiredService<ICurrentTenant>().Id;
            // ConcurrentDictionary 不接受 null key：Host（TenantId=null）上传会抛 ArgumentNullException → 500。
            // 用哨兵键让 Host 与各租户共享同一把/各自的锁语义不变。
            var gate = TenantGates.GetOrAdd(tenantId ?? Guid.Empty, _ => new SemaphoreSlim(1, 1));
            await gate.WaitAsync();
            try
            {
                await CheckInternalAsync(tenantId, incomingBytes);
            }
            finally
            {
                gate.Release();
            }
        }
    }

    private async Task CheckInternalAsync(Guid? tenantId, long incomingBytes)
    {
        using var scope = _scopeFactory.CreateScope();
        var uowManager = scope.ServiceProvider.GetRequiredService<IUnitOfWorkManager>();
        using var uow = uowManager.Begin(requiresNew: true);
        var featureChecker = scope.ServiceProvider.GetRequiredService<IFeatureChecker>();
        var fileRepository = scope.ServiceProvider.GetRequiredService<IRepository<File, Guid>>();
        var asyncExecuter = scope.ServiceProvider.GetRequiredService<IAsyncQueryableExecuter>();

        var quotaText = await featureChecker.GetOrNullAsync(AbpAdminFeatures.FileManagementStorageQuotaBytes);
        if (string.IsNullOrWhiteSpace(quotaText) || !long.TryParse(quotaText, out var quota) || quota <= 0)
        {
            return;
        }

        var queryable = await fileRepository.GetQueryableAsync();
        // 显式限定租户：与多租户过滤器等价（防止过滤器被上游禁用后 SUM 成全局值），
        // 并让聚合查询可走 (TenantId, FileType) 索引
        var used = await asyncExecuter.SumAsync(
            queryable
                .Where(x => x.TenantId == tenantId && x.FileType == FileType.RegularFile)
                .Select(x => x.ByteSize));
        if (used + incomingBytes > quota)
        {
            throw new BusinessException(AbpAdminDomainErrorCodes.Files.StorageQuotaExceeded)
                .WithData("Used", used)
                .WithData("Quota", quota);
        }

        await uow.CompleteAsync();
    }
}
