using AbpAdmin.Localization;
using Volo.Abp.Features;
using Volo.Abp.Localization;
using Volo.Abp.Validation.StringValues;

namespace AbpAdmin.Features;

public class AbpAdminFeatureDefinitionProvider : FeatureDefinitionProvider
{
    public override void Define(IFeatureDefinitionContext context)
    {
        var group = context.AddGroup(AbpAdminFeatures.GroupName, L("Feature:AbpAdmin"));

        group.AddFeature(
            AbpAdminFeatures.FileManagementStorageQuotaBytes,
            defaultValue: "0",
            displayName: L("Feature:FileManagementStorageQuotaBytes"),
            description: L("Feature:FileManagementStorageQuotaBytesDescription"),
            valueType: new FreeTextStringValueType(new NumericValueValidator(0)));
    }

    private static LocalizableString L(string name)
    {
        return LocalizableString.Create<AbpAdminResource>(name);
    }
}

public static class AbpAdminFeatures
{
    public const string GroupName = "AbpAdmin";

    /// <summary>
    /// 每租户文件存储配额（字节）。0 表示不限制。
    /// </summary>
    public const string FileManagementStorageQuotaBytes = GroupName + ".FileManagement.StorageQuotaBytes";
}
