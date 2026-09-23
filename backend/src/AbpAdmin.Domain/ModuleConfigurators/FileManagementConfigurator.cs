using AbpAdmin.Files;
using EasyAbp.FileManagement;
using EasyAbp.FileManagement.Containers;
using EasyAbp.FileManagement.Files;
using EasyAbp.FileManagement.Options;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Volo.Abp.BlobStoring;

namespace AbpAdmin.ModuleConfigurators;

/// <summary>
/// 文件管理（EasyAbp.FileManagement）容器与上传限额配置（自 AbpAdminDomainModule 拆出，注册等价搬移）。
/// </summary>
internal static class FileManagementConfigurator
{
    public static void ConfigureFileManagement(this IServiceCollection services, IConfiguration configuration)
    {
        var fileManagementSection = configuration.GetSection(AbpAdminFileManagementOptions.SectionName);
        var fileManagementOptions = fileManagementSection.Get<AbpAdminFileManagementOptions>() ?? new AbpAdminFileManagementOptions();
        services.Configure<AbpAdminFileManagementOptions>(fileManagementSection);

        services.Configure<FileManagementOptions>(options =>
        {
            options.DefaultFileDownloadProviderType = typeof(LocalFileDownloadProvider);
            options.Containers.Configure<AdminFileManagementContainer>(container =>
            {
                // Private 意味着非所有者不可访问，这是管理后台想要的默认。
                container.FileContainerType = FileContainerType.Private;
                container.AbpBlobContainerName = BlobContainerNameAttribute.GetContainerName<AdminFileBlobContainer>();
                container.AbpBlobDirectorySeparator = "/";
                container.RetainUnusedBlobs = false;
                container.EnableAutoRename = true;
                container.MaxByteSizeForEachFile = fileManagementOptions.MaxByteSizeForEachFile;
                container.MaxByteSizeForEachUpload = fileManagementOptions.MaxByteSizeForEachUpload;
                container.MaxFileQuantityForEachUpload = fileManagementOptions.MaxFileQuantityForEachUpload;
                container.AllowOnlyConfiguredFileExtensions = fileManagementOptions.AllowOnlyConfiguredFileExtensions;
                container.GetDownloadInfoTimesLimitEachUserPerMinute = fileManagementOptions.GetDownloadInfoTimesLimitEachUserPerMinute;
                foreach (var ext in fileManagementOptions.AllowedFileExtensions)
                {
                    container.FileExtensionsConfiguration[ext] = true;
                }
            });
        });
    }
}
