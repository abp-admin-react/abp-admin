using System;
using Volo.Abp;
using Volo.Abp.Domain.Entities.Auditing;

namespace AbpAdmin.Editions;

public class Edition : AuditedAggregateRoot<Guid>
{
    public string DisplayName { get; private set; } = default!;

    protected Edition()
    {
    }

    public Edition(Guid id, string displayName)
        : base(id)
    {
        SetDisplayName(displayName);
    }

    public void SetDisplayName(string displayName)
    {
        DisplayName = Check.NotNullOrWhiteSpace(
            displayName,
            nameof(displayName),
            maxLength: EditionConsts.MaxDisplayNameLength);
    }
}
