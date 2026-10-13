using System;
using System.Threading;
using System.Threading.Tasks;
using AbpAdmin.Collection.Channels;
using AbpAdmin.Collection.Contracts;

namespace AbpAdmin.Collection.Fakes;

/// <summary>
/// 渠道网关假实现（M1：真实网关 M3/M4 落地）。微信/支付宝两个静态实例分别注册，
/// 测试按序配置行为（NextCreateResult 一次性开关——收款号在服务内生成，无法按键预配）。
/// </summary>
public class FakeChannelGateway : ICollectionChannelGateway
{
    public static FakeChannelGateway Wechat { get; } = new(CollectionChannel.WechatNative);

    public static FakeChannelGateway Alipay { get; } = new(CollectionChannel.AlipayPrecreate);

    /// <summary>按收款号决定创建结果；未配置时默认成功（带回二维码载荷）。</summary>
    public System.Collections.Generic.Dictionary<string, ChannelCreateResult> CreateResults { get; } = new();

    /// <summary>按收款号决定关单结果；未配置时默认 Closed。</summary>
    public System.Collections.Generic.Dictionary<string, ChannelCloseResult> CloseResults { get; } = new();

    /// <summary>一次性创建结果（下一次 CreateAsync 消费后清空）。</summary>
    public ChannelCreateResult? NextCreateResult { get; set; }

    public int CreateCallCount { get; private set; }

    public int CloseCallCount { get; private set; }

    private readonly CollectionChannel _channel;

    private FakeChannelGateway(CollectionChannel channel)
    {
        _channel = channel;
    }

    public CollectionChannel Channel => _channel;

    public Task<ChannelCreateResult> CreateAsync(ChannelCreateRequest request, CancellationToken cancellationToken = default)
    {
        CreateCallCount++;
        if (NextCreateResult != null)
        {
            var oneShot = NextCreateResult;
            NextCreateResult = null;
            return Task.FromResult(oneShot);
        }

        return Task.FromResult(CreateResults.TryGetValue(request.CollectionNo, out var result)
            ? result
            : new ChannelCreateResult(Succeeded: true, TradeNo: "fake-trade-" + _channel + CreateCallCount, Payload: "fake://" + _channel));
    }

    public Task<ChannelCloseResult> CloseAsync(ChannelCloseRequest request, CancellationToken cancellationToken = default)
    {
        CloseCallCount++;
        return Task.FromResult(CloseResults.TryGetValue(request.CollectionNo, out var result)
            ? result
            : new ChannelCloseResult(ChannelCloseOutcome.Closed));
    }

    /// <summary>用例间复位：静态实例跨 AbpIntegratedTest 重建存活，计数与预配必须清零。</summary>
    public void Reset()
    {
        CreateResults.Clear();
        CloseResults.Clear();
        NextCreateResult = null;
        CreateCallCount = 0;
        CloseCallCount = 0;
    }
}
