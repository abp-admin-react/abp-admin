using System;
using System.Linq;
using System.Threading.Tasks;
using AbpAdmin.Collection.Data;
using AbpAdmin.ScheduledJobs;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Volo.Abp.DependencyInjection;
using Volo.Abp.EntityFrameworkCore;
using Volo.Abp.Uow;

namespace AbpAdmin.Collection.Jobs;

/// <summary>
/// 流水合计作业（计划第 5.5 节「每日作业：按用户和币种，流水之和等于余额行。不等则告警」）。
/// 三个方向都查：流水侧对不上、余额行对不上、只有一边有记录——任何一侧漂移都会从另一侧暴露。
/// M2 只落 WRN 日志（告警清单「账本不平」的落点；站内信告警链路按计划随 M5/M6 接）。
/// Host 专用（HostOnlyScheduledJobTypes），种子只给 Host 插行且默认停用。
/// </summary>
// 除接口外暴露具体类型：宿主按 JobType 挑接口实现，测试/管理端直调 QueryMismatchesAsync（类代理兼容虚方法）
[ExposeServices(typeof(IScheduledJobHandler), typeof(BalanceAuditJobHandler))]
public class BalanceAuditJobHandler : IScheduledJobHandler, ITransientDependency
{
    public string JobType => ScheduledJobConsts.BalanceAuditJobType;

    public string DisplayNameKey => "ScheduledJobType:BalanceAudit";

    private readonly IDbContextProvider<CollectionDbContext> _dbContextProvider;
    private readonly ILogger<BalanceAuditJobHandler> _logger;

    public BalanceAuditJobHandler(
        IDbContextProvider<CollectionDbContext> dbContextProvider,
        ILogger<BalanceAuditJobHandler> logger)
    {
        _dbContextProvider = dbContextProvider;
        _logger = logger;
    }

    [UnitOfWork(isTransactional: false)]
    public virtual async Task ExecuteAsync(ScheduledJobContext context)
    {
        foreach (var row in await QueryMismatchesAsync())
        {
            // 账本不平（告警清单）：WRN 落日志，带齐两侧数值供对账
            _logger.LogWarning(
                "账本不平：用户 {UserId} {Currency} 可用 {SumTxns}≠{Balance}，冻结 {SumFrozen}≠{Frozen}（{Side}）",
                row.UserId, row.Currency, row.SumTxns, row.Balance, row.SumFrozen, row.Frozen,
                row.OnlyOnBalanceSide ? "仅余额侧有记录" : "两侧都有记录");
        }
    }

    /// <summary>
    /// 账本不平行（FULL JOIN 三向：流水侧对不上 / 余额侧对不上 / 只有一边有记录）。
    /// 提现 M1 两桶口径：可用桶 = 流水合计剔除 WithdrawSettle（出账行动可用，只动冻结）；
    /// 冻结桶 = Freeze 记 +、Unfreeze 记 −、Settle 记 +Amount（Amount 为 −出账额）。
    /// 提取成可查询单元：作业只负责告警，检测谓词由测试直接钉住（原先只测「不抛」，
    /// 检测逻辑退化成恒空也照样绿——评审测试债）
    /// </summary>
    [UnitOfWork(isTransactional: false)]
    public virtual async Task<System.Collections.Generic.List<AuditRow>> QueryMismatchesAsync()
    {
        var dbContext = await _dbContextProvider.GetDbContextAsync();
        return await dbContext.Database
            .SqlQueryRaw<AuditRow>(@"SELECT COALESCE(t.""UserId"", b.""UserId"") AS ""UserId"",
                                       COALESCE(t.""Currency"", b.""Currency"") AS ""Currency"",
                                       COALESCE(t.""SumTxns"", 0) AS ""SumTxns"", COALESCE(b.""Balance"", 0) AS ""Balance"",
                                       COALESCE(t.""SumFrozen"", 0) AS ""SumFrozen"", COALESCE(b.""Frozen"", 0) AS ""Frozen"",
                                       (t.""UserId"" IS NULL) AS ""OnlyOnBalanceSide""
                                FROM (SELECT ""UserId"", ""Currency"",
                                              SUM(CASE WHEN ""Action"" = 'WithdrawSettle' THEN 0 ELSE ""Amount"" END) AS ""SumTxns"",
                                              SUM(CASE WHEN ""Action"" = 'WithdrawSettle' THEN ""Amount""
                                                       WHEN ""Action"" IN ('WithdrawFreeze', 'WithdrawUnfreeze') THEN -""Amount""
                                                       ELSE 0 END) AS ""SumFrozen""
                                      FROM ""ColBalanceTxns"" GROUP BY ""UserId"", ""Currency"") t
                                FULL JOIN ""ColBalances"" b
                                  ON b.""UserId"" = t.""UserId"" AND b.""Currency"" = t.""Currency""
                                WHERE COALESCE(t.""SumTxns"", 0) <> COALESCE(b.""Balance"", 0)
                                   OR COALESCE(t.""SumFrozen"", 0) <> COALESCE(b.""Frozen"", 0)")
            .ToListAsync();
    }
}

/// <summary>账本核对结果行（流水合计 vs 余额行，可用/冻结两桶；OnlyOnBalanceSide = 只有余额侧有记录）。</summary>
public record AuditRow(
    Guid UserId, int Currency, decimal SumTxns, decimal Balance,
    decimal SumFrozen, decimal Frozen, bool OnlyOnBalanceSide);
