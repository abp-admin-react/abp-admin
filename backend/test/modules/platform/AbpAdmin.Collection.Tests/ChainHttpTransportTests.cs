using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using AbpAdmin.Collection.Chain;
using Shouldly;
using Xunit;

namespace AbpAdmin.Collection;

public class ChainHttpTransportTests
{
    [Theory]
    [InlineData("http://rpc.example.test")]
    [InlineData("https://user:password@rpc.example.test")]
    [InlineData("https://rpc.example.test/#fragment")]
    [InlineData("https://node.localhost./rpc")]
    [InlineData("https://127.0.0.1/rpc")]
    [InlineData("https://2130706433/rpc")]
    [InlineData("https://[::ffff:127.0.0.1]/rpc")]
    [InlineData("https://169.254.169.254/rpc")]
    [InlineData("https://[2002:7f00:1::1]/rpc")]
    [InlineData("https://[::127.0.0.1]/rpc")]
    public void Endpoint_Is_Rejected_Before_Saving(string endpoint) =>
        ChainHttpTransport.IsAllowedEndpoint(endpoint).ShouldBeFalse();

    [Theory]
    [InlineData("https://rpc.example.test:8443/example?key=opaque")]
    [InlineData("https://8.8.8.8/rpc")]
    [InlineData("https://[2606:4700:4700::1111]/rpc")]
    public void Public_Endpoint_Values_Are_Accepted_Without_Dns(string endpoint) =>
        ChainHttpTransport.IsAllowedEndpoint(endpoint).ShouldBeTrue();

    [Fact]
    public async Task Checked_Resolution_Is_Used_Directly_For_Socket_Connections()
    {
        var first = IPAddress.Parse("2606:4700:4700::1111");
        var second = IPAddress.Parse("1.1.1.1");
        var probes = new List<IPEndPoint>();
        var resolutions = 0;
        using var stream = new MemoryStream();
        using var cancellation = new CancellationTokenSource();

        var result = await ChainHttpTransport.ConnectToPublicAddressAsync(new DnsEndPoint("rpc.example.test", 8443),
            (host, token) =>
            {
                resolutions++;
                host.ShouldBe("rpc.example.test");
                token.ShouldBe(cancellation.Token);
                return Task.FromResult(new[] { first, second });
            },
            (endpoint, token) =>
            {
                probes.Add(endpoint);
                token.ShouldBe(cancellation.Token);
                return endpoint.Address.Equals(first)
                    ? ValueTask.FromException<Stream>(new SocketException((int)SocketError.NetworkUnreachable))
                    : ValueTask.FromResult<Stream>(stream);
            }, cancellation.Token);

        resolutions.ShouldBe(1);
        probes.ShouldBe([new IPEndPoint(first, 8443), new IPEndPoint(second, 8443)]);
        result.ShouldBeSameAs(stream);
    }

    [Theory]
    [InlineData("10.2.3.4")]
    [InlineData("::ffff:172.17.0.1")]
    [InlineData("2002:a00:1::")]
    [InlineData(null)]
    public async Task Mixed_Or_Empty_Dns_Answers_Do_Not_Dial_Any_Address(string? invalidAddress)
    {
        IPAddress[] addresses = invalidAddress == null ? [] : [IPAddress.Parse("1.1.1.1"), IPAddress.Parse(invalidAddress)];
        await Should.ThrowAsync<HttpRequestException>(() => ChainHttpTransport.ConnectToPublicAddressAsync(
            new DnsEndPoint("rpc.example.test", 443), (_, _) => Task.FromResult(addresses),
            (_, _) => throw new InvalidOperationException("An invalid answer must not dial a socket."),
            CancellationToken.None).AsTask());
    }

    [Fact]
    public async Task Production_Handler_Does_Not_Proxy_Redirect_Or_Dial_Plain_Http()
    {
        using var handler = ChainHttpTransport.CreateHandler();
        handler.AllowAutoRedirect.ShouldBeFalse();
        handler.UseProxy.ShouldBeFalse();
        handler.ConnectCallback.ShouldNotBeNull();
        using var client = new HttpClient(handler, disposeHandler: false);
        var exception = await Should.ThrowAsync<HttpRequestException>(() => client.GetAsync("http://127.0.0.1/"));
        exception.Message.ShouldContain("must use HTTPS and a public address");
    }
}
