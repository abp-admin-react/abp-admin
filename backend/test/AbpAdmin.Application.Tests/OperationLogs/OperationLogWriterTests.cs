using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using AbpAdmin.OperationLogs;
using Shouldly;
using Volo.Abp;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Modularity;
using Volo.Abp.Uow;
using Xunit;

namespace AbpAdmin.OperationLogs;

/* 写入器契约测试：
 * 1) 超长自由文本（模板渲染值/异常消息）落库前截断——不截断会让 Check.Length 抛异常
 *    吞掉整条审计记录（审查 H2：攻击者可用超长输入蓄意让审计消失）；
 * 2) 控制字符清洗——防前端 pre-wrap 渲染与未来文本导出里伪造日志行；
 * 3) requiresNew UoW 独立落库（业务回滚后仍可查询）。
 */
public abstract class OperationLogWriterTests<TStartupModule> : AbpAdminApplicationTestBase<TStartupModule>
    where TStartupModule : IAbpModule
{
    private readonly IOperationLogWriter _writer;
    private readonly IRepository<OperationLog, Guid> _repository;

    protected OperationLogWriterTests()
    {
        _writer = GetRequiredService<IOperationLogWriter>();
        _repository = GetRequiredService<IRepository<OperationLog, Guid>>();
    }

    [Fact]
    public async Task Overlong_Fields_Are_Truncated_Not_Dropped()
    {
        var entry = new OperationLogEntry
        {
            Type = "测试模块",
            SubType = "超长输入",
            Action = new string('长', OperationLogConsts.MaxActionLength + 500),
            ErrorMessage = new string('误', OperationLogConsts.MaxErrorMessageLength + 500),
            UserAgent = new string('U', OperationLogConsts.MaxUserAgentLength + 100),
            RequestUrl = "/" + new string('u', OperationLogConsts.MaxRequestUrlLength + 100),
            Success = false,
        };

        await _writer.WriteAsync(entry);

        var log = await FindSingleAsync(SubType: "超长输入");
        log.ShouldNotBeNull();
        log.Action!.Length.ShouldBe(OperationLogConsts.MaxActionLength);
        log.Action!.EndsWith('…').ShouldBeTrue();
        log.ErrorMessage!.Length.ShouldBe(OperationLogConsts.MaxErrorMessageLength);
        log.UserAgent!.Length.ShouldBe(OperationLogConsts.MaxUserAgentLength);
        log.RequestUrl!.Length.ShouldBe(OperationLogConsts.MaxRequestUrlLength);
    }

    [Fact]
    public async Task Overlong_Type_SubType_Are_Truncated_Not_Dropped()
    {
        // SubType 是模板渲染产物（如 "删除 {{file.fileName}}"），长度不可控：
        // 不截断会让实体构造器 Check.Length 抛异常，整条日志在 catch 里静默消失
        var entry = new OperationLogEntry
        {
            Type = new string('类', OperationLogConsts.MaxTypeLength + 10),
            SubType = new string('操', OperationLogConsts.MaxSubTypeLength + 10),
            Action = "验证 Type/SubType 超长不再丢日志",
            Success = true,
        };

        await _writer.WriteAsync(entry);

        // 截断语义：保留前 N-1 个字符并以「…」收尾（与 Action 等自由文本字段一致）
        var expectedSubType = entry.SubType[..(OperationLogConsts.MaxSubTypeLength - 1)] + '…';
        var log = await FindSingleAsync(SubType: expectedSubType);
        log.ShouldNotBeNull();
        log.Type!.Length.ShouldBe(OperationLogConsts.MaxTypeLength);
        log.SubType!.Length.ShouldBe(OperationLogConsts.MaxSubTypeLength);
    }

    [Fact]
    public async Task Control_Characters_Are_Sanitized()
    {
        var entry = new OperationLogEntry
        {
            Type = "测试模块",
            SubType = "注入尝试",
            Action = "第一行\r\n伪造的第二行\t制表",
            Success = true,
        };

        await _writer.WriteAsync(entry);

        var log = await FindSingleAsync(SubType: "注入尝试");
        log.ShouldNotBeNull();
        log.Action!.ShouldBe("第一行 伪造的第二行 制表");
    }

    [Fact]
    public async Task CorrelationId_Is_Persisted_And_Truncated()
    {
        // 与 ABP AuditLog/SecurityLog 同源的关联 ID 必须完整落库；
        // 透传外部 X-Correlation-Id 头可能超长，落库前统一截断（防 Check.Length 吞日志）
        await _writer.WriteAsync(new OperationLogEntry
        {
            Type = "测试模块",
            SubType = "关联ID-正常",
            Success = true,
            CorrelationId = "32bit-n-guid",
        });
        await _writer.WriteAsync(new OperationLogEntry
        {
            Type = "测试模块",
            SubType = "关联ID-超长",
            Success = true,
            CorrelationId = new string('c', OperationLogConsts.MaxCorrelationIdLength + 100),
        });

        var normal = await FindSingleAsync(SubType: "关联ID-正常");
        normal.ShouldNotBeNull();
        normal.CorrelationId.ShouldBe("32bit-n-guid");

        var overlong = await FindSingleAsync(SubType: "关联ID-超长");
        overlong.ShouldNotBeNull();
        overlong.CorrelationId!.Length.ShouldBe(OperationLogConsts.MaxCorrelationIdLength);
        overlong.CorrelationId!.EndsWith('…').ShouldBeTrue();
    }

    [Fact]
    public async Task Write_Survives_Ambient_UoW_Rollback()
    {
        // 失败日志的持久性契约：业务事务回滚后日志仍在（requiresNew 语义）
        try
        {
            await WithUnitOfWorkAsync(async () =>
            {
                await _writer.WriteAsync(new OperationLogEntry
                {
                    Type = "测试模块",
                    SubType = "回滚后保留",
                    Success = false,
                    ErrorMessage = "业务失败",
                });

                throw new BusinessException("TEST:ROLLBACK"); // 模拟业务回滚
            });
        }
        catch (BusinessException)
        {
            // 预期
        }

        var log = await FindSingleAsync(SubType: "回滚后保留");
        log.ShouldNotBeNull();
        log.Success.ShouldBeFalse();
    }

    private async Task<OperationLog?> FindSingleAsync(string? SubType = null)
    {
        return await WithUnitOfWorkAsync(async () =>
        {
            var logs = await _repository.GetListAsync(x => x.SubType == SubType);
            return logs.Count == 0 ? null : logs[0];
        });
    }
}
