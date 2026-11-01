using AbpAdmin.Ledger.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Volo.Abp.EntityFrameworkCore.Modeling;

namespace AbpAdmin.Ledger.Data;

public class FinReconcileRunConfig : IEntityTypeConfiguration<FinReconcileRun>
{
    public void Configure(EntityTypeBuilder<FinReconcileRun> b)
    {
        b.ToTable(LedgerConsts.DbTablePrefix + "ReconcileRuns", LedgerConsts.DbSchema);
        b.ConfigureByConvention();
        b.Property(x => x.RunType).IsRequired().HasMaxLength(64);
        b.Property(x => x.ScopeJson).HasColumnType("jsonb");
        b.Property(x => x.DiffJson).HasColumnType("jsonb");
        b.HasIndex(x => new { x.TenantId, x.CreationTime });
    }
}
