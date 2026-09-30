using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using AbpAdmin.Monitoring;
using Microsoft.Extensions.Configuration;
using Shouldly;
using Xunit;

namespace AbpAdmin.Monitoring;

/// <summary>
/// CacheMonitorRedisConnection 拨号姿态的钉住：abortConnect=false 是连接串常见默认
/// （Azure 风格），不强制覆盖时 ConnectAsync 在 Redis 不可达时会静默返回未连接的
/// 多路复用器——监控页随后每条命令都付一次超时、被误报成 INFO/连接异常，正是该类
/// 注释宣称要避免的误导诊断。删掉强制覆盖（options.AbortOnConnectFail = true），
/// 本测试变红：连接"成功"、GetMultiplexerAsync 不再抛。
/// 与宿主侧 RedisStartupPostureTests.Shared_Connection_Fails_Fast_... 互补——
/// 两条自建连接各自钉住同一纪律。
/// </summary>
public class CacheMonitorRedisConnectionTests
{
    private static CacheMonitorRedisConnection ConnectionWith(string configuration) =>
        new(new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Redis:IsEnabled"] = "true",
                ["Redis:Configuration"] = configuration,
            }).Build());

    [Fact]
    public async Task Fails_Fast_Even_When_String_Opts_Out_Via_AbortConnectFalse()
    {
        // 127.0.0.1:1 特权端口无监听：连接被立即拒绝而非挂起（与宿主侧测试同一手法，
        // 有意在本用例里真的拨号）
        var connection = ConnectionWith("127.0.0.1:1,abortConnect=false");

        await Should.ThrowAsync<Exception>(() => connection.GetMultiplexerAsync());
    }
}
