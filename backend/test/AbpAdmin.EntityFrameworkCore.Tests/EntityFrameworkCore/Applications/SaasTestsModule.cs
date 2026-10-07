using System.Collections.Concurrent;
using System.Threading.Tasks;
using AbpAdmin.Data;
using AbpAdmin.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Volo.Abp.Modularity;

namespace AbpAdmin.EntityFrameworkCore.Applications;

/// <summary>
/// 建库器调用记录契约的实现：记录 CreateIfNotExistsAsync 收到的连接串（明文）。
/// </summary>
public class RecordingFakeTenantDatabaseCreator : ITenantDatabaseCreator, AbpAdmin.Saas.ITenantDatabaseCreationRecorder
{
    public ConcurrentQueue<string> ReceivedConnectionStrings { get; } = new();

    public Task CreateIfNotExistsAsync(string connectionString)
    {
        ReceivedConnectionStrings.Enqueue(connectionString);
        return Task.CompletedTask;
    }
}

/// <summary>
/// Saas 域测试的聚合模块：EF 测试基线 + 记录式建库器 Fake。
///
/// 背景：A-block 重构后 TenantDatabaseCreator 是 PostgreSQL-only（经维护库 CREATE DATABASE），
/// 测试基建没有可建库的 PG 服务器（pg_hba 只放行既有库）。Saas 域用例守护的是本仓库自己的接线
/// （保存独立连接串 → 提交后处理器调用建库器；共享库模式不得触发）， Npgsql 语义用 Fake 摘出被测面。
/// PostConfigureServices：所有模块 ConfigureServices（含约定注册的真实 creator）之后执行，替换不被覆盖。
/// </summary>
[DependsOn(typeof(AbpAdminEntityFrameworkCoreTestModule))]
public class SaasTestsModule : AbpModule
{
    public override void PostConfigureServices(ServiceConfigurationContext context)
    {
        var fake = new RecordingFakeTenantDatabaseCreator();
        context.Services.RemoveAll<ITenantDatabaseCreator>();
        context.Services.AddSingleton<AbpAdmin.Saas.ITenantDatabaseCreationRecorder>(fake);
        context.Services.AddSingleton<ITenantDatabaseCreator>(fake);
    }
}
