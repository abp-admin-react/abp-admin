using System;
using System.Threading;
using System.Threading.Tasks;
using AbpAdmin.Collection.Channels;
using AbpAdmin.Collection.Contracts;

namespace AbpAdmin.Collection.Fakes;

public class FakeRefundChannelGateway : ICollectionRefundChannelGateway
{
    public static FakeRefundChannelGateway Instance { get; } = new();
    public const string AccountId = "refund-tests-merchant";

    public CollectionChannel Channel => CollectionChannel.WechatNative;
    public string MerchantAccountId => AccountId;
    public Func<CollectionRefundChannelRequest, Task<CollectionRefundChannelResult>>? OnSubmit { get; set; }
    public Func<CollectionRefundChannelRequest, Task<CollectionRefundChannelResult>>? OnQuery { get; set; }

    public Task<CollectionRefundChannelResult> RefundAsync(
        CollectionRefundChannelRequest request, CancellationToken cancellationToken = default) =>
        OnSubmit?.Invoke(request) ?? Task.FromResult(new CollectionRefundChannelResult(CollectionRefundOutcome.Unknown));

    public Task<CollectionRefundChannelResult> QueryRefundAsync(
        CollectionRefundChannelRequest request, CancellationToken cancellationToken = default) =>
        OnQuery?.Invoke(request) ?? Task.FromResult(new CollectionRefundChannelResult(CollectionRefundOutcome.Unknown));
}
