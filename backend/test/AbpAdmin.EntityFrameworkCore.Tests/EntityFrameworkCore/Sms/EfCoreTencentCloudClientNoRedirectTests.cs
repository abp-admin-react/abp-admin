using System;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;

namespace AbpAdmin.EntityFrameworkCore.Sms;

/// <summary>
/// 腾讯云 SMS 命名客户端禁重定向的真实接线测试（安全审计 run-2 C-5 回归锁）：
/// EasyAbp requester 走 CreateClient("DefaultTencentCloud")（包模块自注册、无处理器配置），
/// 空名默认客户端上的禁重定向覆盖不到它——EndPoint 是管理员可改设置，SafeHttpUrl 只校验
/// 入口 URL，跟随 3xx 会把签名请求重放到内网目标。本测试从真实启动的 Domain 模块
/// （AbpAdminEntityFrameworkCoreTestModule → … → AbpAdminDomainModule 的注册链）解析
/// IHttpClientFactory，断言该命名客户端对入口 302 不跟随（302 原样上抛、重定向目标零命中）。
/// 入口/目标都是测试进程内的环回监听器（无外网依赖；Domain.Tests 无 EF 仓储起不了完整
/// 模块链，故落在 EFCore 测试宿主）。
/// </summary>
[Collection(AbpAdminTestConsts.CollectionDefinitionName)]
public class EfCoreTencentCloudClientNoRedirectTests : AbpAdminEntityFrameworkCoreTestBase
{
    [Fact]
    public async Task Named_Client_Does_Not_Follow_Redirect_To_Internal_Target()
    {
        var factory = GetRequiredService<IHttpClientFactory>();
        await using var target = new LoopbackServer(); // 重定向目标（内网形态）
        await using var entry = new LoopbackServer();  // 入口（SafeHttpUrl 语义下的公网形态替身）
        entry.RespondWith = $"HTTP/1.1 302 Found\r\nLocation: {target.Url}/internal\r\nConnection: close\r\n\r\n";

        using var client = factory.CreateClient("DefaultTencentCloud");
        using var response = await client.GetAsync(entry.Url);

        // 不跟随：302 原样上抛（若跟随，状态会是目标返回的 200 且 target 被命中）
        ((int)response.StatusCode).ShouldBe(302);
        entry.HitCount.ShouldBeGreaterThanOrEqualTo(1);
        target.HitCount.ShouldBe(0);
    }

    /// <summary>
    /// 最小环回 HTTP 监听器：接受连接、读完请求头、回固定响应（默认 200）。
    /// TcpListener 直连（不依赖 HttpListener 的平台 URL ACL），Dispose 即释放。
    /// </summary>
    private sealed class LoopbackServer : IAsyncDisposable
    {
        private readonly TcpListener _listener;
        private readonly Task _serveLoop;

        public int HitCount { get; private set; }
        public string RespondWith { get; set; } = "HTTP/1.1 200 OK\r\nContent-Length: 2\r\nConnection: close\r\n\r\nok";
        public string Url => $"http://127.0.0.1:{((IPEndPoint)_listener.LocalEndpoint).Port}";

        public LoopbackServer()
        {
            _listener = new TcpListener(IPAddress.Loopback, 0);
            _listener.Start();
            _serveLoop = Task.Run(ServeAsync);
        }

        private async Task ServeAsync()
        {
            while (true)
            {
                TcpClient client;
                try
                {
                    client = await _listener.AcceptTcpClientAsync();
                }
                catch (ObjectDisposedException)
                {
                    return; // Dispose 后退出循环
                }

                HitCount++;
                _ = Task.Run(async () =>
                {
                    try
                    {
                        using var _ = client;
                        await HandleAsync(client, RespondWith);
                    }
                    catch
                    {
                        // 单连接处理失败不影响断言（命中计数已在 accept 时记录）
                    }
                });
            }
        }

        private static async Task HandleAsync(TcpClient client, string respondWith)
        {
            using var ns = client.GetStream();
            var buffer = new byte[4096];
            var received = 0;
            // 读完请求头即可回（本测试只有 GET，无请求体；超长头直接按读完处理）
            while (received < buffer.Length)
            {
                var read = await ns.ReadAsync(buffer.AsMemory(received, buffer.Length - received));
                if (read == 0)
                {
                    break;
                }

                received += read;
                if (Encoding.ASCII.GetString(buffer, 0, received).Contains("\r\n\r\n"))
                {
                    break;
                }
            }

            var bytes = Encoding.ASCII.GetBytes(respondWith);
            await ns.WriteAsync(bytes);
            await ns.FlushAsync();
        }

        public async ValueTask DisposeAsync()
        {
            _listener.Stop();
            await _serveLoop.ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
        }
    }
}
