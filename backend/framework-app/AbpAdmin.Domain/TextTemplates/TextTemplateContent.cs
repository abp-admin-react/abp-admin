using System;
using Volo.Abp;
using Volo.Abp.Domain.Entities.Auditing;
using Volo.Abp.MultiTenancy;

namespace AbpAdmin.TextTemplates;

public class TextTemplateContent : AuditedAggregateRoot<Guid>, IMultiTenant
{
    public virtual Guid? TenantId { get; protected set; }

    public string Name { get; private set; } = default!;

    public string? CultureName { get; private set; }

    public string Content { get; private set; } = default!;

    protected TextTemplateContent()
    {
    }

    public TextTemplateContent(Guid id, Guid? tenantId, string name, string content, string? cultureName = null)
        : base(id)
    {
        TenantId = tenantId;
        Name = Check.NotNullOrWhiteSpace(name, nameof(name), maxLength: 128);
        SetContent(content);
        CultureName = cultureName;
    }

    public void SetContent(string content)
    {
        Content = Check.NotNull(content, nameof(content));
    }
}
