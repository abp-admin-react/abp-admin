using System;
using System.ComponentModel.DataAnnotations;
using AbpAdmin.Editions;
using AbpAdmin.Identity;
using AbpAdmin.Tenants;
using Volo.Abp.Identity;
using Volo.Abp.ObjectExtending;
using Volo.Abp.Threading;

namespace AbpAdmin;

public static class AbpAdminModuleExtensionConfigurator
{
    private static readonly OneTimeRunner OneTimeRunner = new OneTimeRunner();

    public static void Configure()
    {
        OneTimeRunner.Run(() =>
        {
            ConfigureExistingProperties();
            ConfigureExtraProperties();
        });
    }

    private static void ConfigureExistingProperties()
    {
        /* You can change max lengths for properties of the
         * entities defined in the modules used by your application.
         *
         * Example: Change user and role name max lengths

           AbpUserConsts.MaxNameLength = 99;
           IdentityRoleConsts.MaxNameLength = 99;

         * Notice: It is not suggested to change property lengths
         * unless you really need it. Go with the standard values wherever possible.
         *
         * Schema changes: EF migrations only — run `dotnet ef migrations add` (see docs/pg-migration-runbook.md).
         */
    }

    private static void ConfigureExtraProperties()
    {
        ObjectExtensionManager.Instance.Modules()
            .ConfigureIdentity(identity => identity.ConfigureRole(role =>
                role.AddOrUpdateProperty<string>(RoleRemarkConsts.PropertyName, property =>
                {
                    property.Attributes.Add(new StringLengthAttribute(RoleRemarkConsts.MaxLength));
                })));

        // 租户激活状态三态与版本过期
        // 已验证：ConfigureTenantManagement 扩展方法在 Volo.Abp.TenantManagement.Domain.Shared 10.6 存在
        // 已验证：AddOrUpdateProperty 支持枚举类型与 DefaultValue
        ObjectExtensionManager.Instance.Modules()
            .ConfigureTenantManagement(tenantManagement =>
            {
                tenantManagement.ConfigureTenant(tenant =>
                {
                    tenant.AddOrUpdateProperty<TenantActivationStateEnum>(
                        AbpAdminTenantConsts.ActivationStatePropertyName,
                        property =>
                        {
                            property.DefaultValue = TenantActivationStateEnum.Active;

                            // ABP 对枚举扩展属性自动加 [Required]（ExtensionPropertyHelper.GetDefaultAttributes），
                            // 会导致创建租户时不传 ActivationState 就校验失败。该字段有实体默认值兜底，
                            // 不应要求输入，故移除。10.6 的 Required 由属性信息上的
                            // Attributes 列表驱动，可在此删除。
                            property.Attributes.RemoveAll(a => a is RequiredAttribute);
                        });

                    tenant.AddOrUpdateProperty<DateTime?>(
                        AbpAdminTenantConsts.ActivationEndDatePropertyName);

                    tenant.AddOrUpdateProperty<DateTime?>(
                        AbpAdminTenantConsts.EditionEndDateUtcPropertyName);

                    // 版本分配（EditionId）同为租户扩展属性：不注册则 DTO 的 extraProperties
                    // 序列化会剥掉它——set-tenant-edition 写进实体后，租户列表"版本"列与
                    // TenantEditionForm 回显永远为空（实测缺陷，SAAS 测试计划 A-TN-04）。
                    // 常量即 EditionConsts.TenantEditionPropertyName（值 "EditionId"）。
                    tenant.AddOrUpdateProperty<Guid?>(
                        EditionConsts.TenantEditionPropertyName);
                });
            });
    }
}
