using System;
using System.Collections.Concurrent;
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
    public ConcurrentDictionary<string, ChannelCreateResult> CreateResults { get; } = new();

    /// <summary>按收款号决定关单结果；未配置时默认 Closed。</summary>
    public ConcurrentDictionary<string, ChannelCloseResult> CloseResults { get; } = new();

    public ConcurrentDictionary<string, ChannelQueryResult> QueryResults { get; } = new();

    /// <summary>一次性创建结果（下一次 CreateAsync 消费后清空）。</summary>
    public ChannelCreateResult? NextCreateResult { get; set; }

    public Func<ChannelCreateRequest, Task<ChannelCreateResult>>? OnCreate { get; set; }

    public Func<ChannelCloseRequest, Task<ChannelCloseResult>>? OnClose { get; set; }

    private int _createCallCount;
    private int _closeCallCount;
    public int CreateCallCount => _createCallCount;
    public int CloseCallCount => _closeCallCount;

    private readonly CollectionChannel _channel;

    private FakeChannelGateway(CollectionChannel channel)
    {
        _channel = channel;
    }

    public CollectionChannel Channel => _channel;

    public string MerchantAccountId => "test-" + _channel;

    public Task<ChannelCreateResult> CreateAsync(ChannelCreateRequest request, CancellationToken cancellationToken = default)
    {
        var count = Interlocked.Increment(ref _createCallCount);
        if (OnCreate is { } onCreate)
        {
            return onCreate(request);
        }

        if (NextCreateResult != null)
        {
            var oneShot = NextCreateResult;
            NextCreateResult = null;
            return Task.FromResult(oneShot);
        }

        return Task.FromResult(CreateResults.TryGetValue(request.CollectionNo, out var result)
            ? result
            : new ChannelCreateResult(Succeeded: true, TradeNo: "fake-trade-" + _channel + count, Payload: "fake://" + _channel));
    }

    public Task<ChannelQueryResult> QueryAsync(ChannelQueryRequest request, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(QueryResults.TryGetValue(request.CollectionNo, out var result)
            ? result
            : new ChannelQueryResult(ChannelQueryOutcome.Pending));
    }

    public Task<ChannelCloseResult> CloseAsync(ChannelCloseRequest request, CancellationToken cancellationToken = default)
    {
        Interlocked.Increment(ref _closeCallCount);
        if (OnClose is { } onClose)
        {
            return onClose(request);
        }

        return Task.FromResult(CloseResults.TryGetValue(request.CollectionNo, out var result)
            ? result
            : new ChannelCloseResult(ChannelCloseOutcome.Closed));
    }

    /// <summary>用例间复位：静态实例跨 AbpIntegratedTest 重建存活，计数与预配必须清零。</summary>
    public void Reset()
    {
        CreateResults.Clear();
        CloseResults.Clear();
        QueryResults.Clear();
        NextCreateResult = null;
        OnCreate = null;
        OnClose = null;
        _createCallCount = 0;
        _closeCallCount = 0;
    }
}
