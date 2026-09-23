using System;
using Microsoft.EntityFrameworkCore;
using AbpAdmin.Tenants;
using Volo.Abp.AuditLogging;
using Volo.Abp.Identity;
using Volo.Abp.ObjectExtending;
using Volo.Abp.TenantManagement;
using Volo.Abp.Threading;

namespace AbpAdmin.EntityFrameworkCore;

public static class AbpAdminEfCoreEntityExtensionMappings
{
    private static readonly OneTimeRunner OneTimeRunner = new OneTimeRunner();

    public static void Configure()
    {
        AbpAdminGlobalFeatureConfigurator.Configure();
        AbpAdminModuleExtensionConfigurator.Configure();

        OneTimeRunner.Run(() =>
        {
            // T2.8 SaaS Pro 缺口：将租户扩展属性映射为 AbpTenants 表的真实列
            // 已验证：MapEfCoreProperty 扩展方法在 Volo.Abp.ObjectExtending 10.6 存在
            ObjectExtensionManager.Instance
                .MapEfCoreProperty<Tenant, byte>(
                    AbpAdminTenantConsts.ActivationStatePropertyName,
                    (entityBuilder, propertyBuilder) =>
                    {
                        propertyBuilder.HasDefaultValue((byte)TenantActivationStateEnum.Active);
                    })
                .MapEfCoreProperty<Tenant, DateTime?>(
                    AbpAdminTenantConsts.ActivationEndDatePropertyName)
                .MapEfCoreProperty<Tenant, DateTime?>(
                    AbpAdminTenantConsts.EditionEndDateUtcPropertyName);

            // 审计日志「已处理」标记列（替代 AuditLogHandleRecord 侧表）：
            // 官方机制见 docs Extending Entities「Entity Extensions (EF Core)」——与 Tenant 同一套
            // MapEfCoreProperty，写经 SetProperty（SaveChanges 时字典→列自动同步），
            // 读经 GetProperty（实体被 track 时列→字典回填），查经 EF.Property 单表条件。
            // 未处理锚点 = HandledAt IS NULL；值双写（ExtraProperties JSON + 列）与 Tenant 先例一致。
            ObjectExtensionManager.Instance
                .MapEfCoreProperty<AuditLog, DateTime?>(
                    AuditLogs.AuditLogHandleConsts.HandledAtPropertyName)
                .MapEfCoreProperty<AuditLog, Guid?>(
                    AuditLogs.AuditLogHandleConsts.HandledByUserIdPropertyName)
                .MapEfCoreProperty<AuditLog, string>(
                    AuditLogs.AuditLogHandleConsts.HandledByNamePropertyName,
                    (entityBuilder, propertyBuilder) =>
                    {
                        propertyBuilder.HasMaxLength(AuditLogs.AuditLogHandleConsts.MaxHandledByNameLength);
                    })
                .MapEfCoreProperty<AuditLog, string>(
                    AuditLogs.AuditLogHandleConsts.HandledNotePropertyName,
                    (entityBuilder, propertyBuilder) =>
                    {
                        propertyBuilder.HasMaxLength(AuditLogs.AuditLogHandleConsts.MaxNoteLength);
                    });
        });
    }
}
