using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AbpAdmin.OperationLogs;

namespace AbpAdmin.Settings;

/// <summary>
/// 录制型 IOperationLogWriter 替身：SettingUi 写入口的操作日志断言用。
/// 刻意【不带】ISingletonDependency/ITransientDependency 标记——常规注册器会把它
/// 扫进所有用 Application 测试模块的测试链，把真实 writer 全局顶掉
/// （EfCoreOperationLogWriterTests 的落库断言直接假红）。只在
/// AbpAdminSettingUiMaskingTestModule 里显式注册，解析经接口再 cast 回本类。
/// </summary>
public class RecordingOperationLogWriter : IOperationLogWriter
{
    public List<OperationLogEntry> Entries { get; } = [];

    public Task WriteAsync(OperationLogEntry entry, CancellationToken cancellationToken = default)
    {
        Entries.Add(entry);
        return Task.CompletedTask;
    }
}
