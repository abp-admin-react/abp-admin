using System;
using System.Collections.Generic;
using Volo.Abp.Domain.Entities;

namespace AbpAdmin.Collection.Entities;

/// <summary>One credential in the ordered deposit scanner list.</summary>
public class ColScanProvider : BasicAggregateRoot<Guid>
{
    public ColScanProvider(Guid id) : base(id)
    {
    }

    protected ColScanProvider()
    {
    }

    public ChainApiKeyProvider Provider { get; set; }
    public string? Label { get; set; }
    public string KeyHash { get; set; } = null!;
    public string KeyCipher { get; set; } = null!;
    public string? PlatformAuthKeyCipher { get; set; }
    public string? Group { get; set; }
    public bool IsEnabled { get; set; }
    public int SortOrder { get; set; }
    public ICollection<ColScanProviderNetwork> Networks { get; } = new List<ColScanProviderNetwork>();
}

public class ColScanProviderNetwork : Entity
{
    public Guid ProviderId { get; set; }
    public Guid NetworkId { get; set; }
    public int? LogResultCap { get; set; }

    public override object[] GetKeys() => [ProviderId, NetworkId];
}
