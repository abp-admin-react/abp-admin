using System;
using System.ComponentModel.DataAnnotations;
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
         * Schema changes: append a numbered .sql under Sql/postgresql and Sql/sqlite (no EF migrations in this repo).
         */
    }

    private static void ConfigureExtraProperties()
    {
        // T2.8 SaaS Pro 缺口：租户激活状态三态与版本过期
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
                            // 不应要求输入，故移除。实测结论：10.6 的 Required 由属性信息上的
                            // Attributes 列表驱动，可在此删除。
                            property.Attributes.RemoveAll(a => a is RequiredAttribute);
                        });

                    tenant.AddOrUpdateProperty<DateTime?>(
                        AbpAdminTenantConsts.ActivationEndDatePropertyName);

                    tenant.AddOrUpdateProperty<DateTime?>(
                        AbpAdminTenantConsts.EditionEndDateUtcPropertyName);
                });
            });
    }
}
