using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Uow;

namespace AbpAdmin.OperationLogs;

/// <summary>
/// 手写操作日志的统一提交点：<see cref="IOperationLogWriter.WriteAsync"/> 在当前工作单元
/// 提交后执行（日志与业务变更同生共死）；无 UoW（后台作业/启动期）时直接落。
/// <para>存在理由：上游非自动 API 控制器（Identity 的 /api/identity/*、EasyAbp SettingUi）
/// 的 [OperationLog] 特性挂不到 MVC Action 的 MethodInfo，这些入口只能服务内手写——
/// 此前 AbpAdminSettingUiAppService 与 AbpAdminRoleAppService 各持一份相同形状的
/// 私有 wrapper，收敛到本类。</para>
/// <para>fail-open：操作日志故障绝不影响已提交的业务变更。<see cref="IOperationLogWriter"/>
/// 的实现契约是自身吞异常记警告（见 OperationLogWriter.WriteAsync），此处的兜底 catch
/// 防将来实现违约破坏该语义。</para>
/// </summary>
public class OperationLogCommitter : ITransientDependency
{
    private readonly IOperationLogWriter _writer;
    private readonly IUnitOfWorkManager _unitOfWorkManager;
    private readonly ILogger<OperationLogCommitter> _logger;

    public OperationLogCommitter(
        IOperationLogWriter writer,
        IUnitOfWorkManager unitOfWorkManager,
        ILogger<OperationLogCommitter> logger)
    {
        _writer = writer;
        _unitOfWorkManager = unitOfWorkManager;
        _logger = logger;
    }

    /// <summary>登记提交后写日志。<paramref name="entry"/> 须在调用前构造完毕（闭包只捕获不可变值）。</summary>
    public void WriteOnCommit(OperationLogEntry entry)
    {
        var uow = _unitOfWorkManager.Current;
        if (uow == null)
        {
            _ = _writer.WriteAsync(entry);
            return;
        }

        uow.OnCompleted(async () =>
        {
            try
            {
                await _writer.WriteAsync(entry);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "操作日志写入失败（Type={Type}, SubType={SubType}）",
                    entry.Type, entry.SubType);
            }
        });
    }
}
