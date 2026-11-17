using System;
using System.Threading;

namespace AbpAdmin.Collection.Contracts;

/// <summary>
/// A server-side scope for one payment admitted by its caller before the payment
/// can change the order or the channel. Background work must admit new payments separately.
/// </summary>
public sealed class CollectionPaymentAdmission : IDisposable
{
    private static readonly AsyncLocal<CollectionPaymentAdmission?> Current = new();
    private const int Available = 0;
    private const int Consumed = 1;
    private const int Disposed = 2;
    private readonly CollectionPaymentAdmission? _previous;
    private readonly string _businessType;
    private readonly Guid _businessId;
    private readonly CollectionChannel? _channel;
    private readonly CollectionCurrency? _balanceCurrency;
    private int _state;

    private CollectionPaymentAdmission(string businessType, Guid businessId,
        CollectionChannel? channel, CollectionCurrency? balanceCurrency)
    {
        _businessType = businessType;
        _businessId = businessId;
        _channel = channel;
        _balanceCurrency = balanceCurrency;
        _previous = Current.Value;
        Current.Value = this;
    }

    public static CollectionPaymentAdmission Begin(string businessType, Guid businessId,
        CollectionChannel? channel, CollectionCurrency? balanceCurrency)
    {
        if (channel.HasValue == balanceCurrency.HasValue)
        {
            throw new ArgumentException("Specify either a payment channel or a balance currency.");
        }

        return new CollectionPaymentAdmission(businessType, businessId, channel, balanceCurrency);
    }

    public static bool Covers(string businessType, Guid businessId,
        CollectionChannel? channel, CollectionCurrency? balanceCurrency)
    {
        return Current.Value is { } current
            && current.Matches(businessType, businessId, channel, balanceCurrency)
            && Volatile.Read(ref current._state) == Available;
    }

    public static bool TryConsume(string businessType, Guid businessId,
        CollectionChannel? channel, CollectionCurrency? balanceCurrency)
    {
        return Current.Value is { } current
            && current.Matches(businessType, businessId, channel, balanceCurrency)
            && Interlocked.CompareExchange(ref current._state, Consumed, Available) == Available;
    }

    public void Dispose()
    {
        // A copied execution context can outlive the request that created the scope.
        Interlocked.Exchange(ref _state, Disposed);
        if (Current.Value == this)
        {
            Current.Value = _previous;
        }
    }

    private bool Matches(string businessType, Guid businessId,
        CollectionChannel? channel, CollectionCurrency? balanceCurrency) =>
        _businessType == businessType
        && _businessId == businessId
        && _channel == channel
        && _balanceCurrency == balanceCurrency;
}
