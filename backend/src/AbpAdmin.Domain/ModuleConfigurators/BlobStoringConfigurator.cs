using AbpAdmin.Blobs;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Volo.Abp.BlobStoring;
using Volo.Abp.BlobStoring.Aliyun;
using Volo.Abp.BlobStoring.Aws;
using Volo.Abp.BlobStoring.Database;
using Volo.Abp.BlobStoring.FileSystem;
using Volo.Abp.BlobStoring.Minio;

namespace AbpAdmin.ModuleConfigurators;

/// <summary>
/// Blob 存储四提供商（Database/FileSystem/Aliyun/Aws/Minio）配置（自 AbpAdminDomainModule 拆出，注册等价搬移）。
/// </summary>
internal static class BlobStoringConfigurator
{
    public static void ConfigureBlobStoring(this IServiceCollection services, IConfiguration configuration)
    {
        // 同一配置节只 GetSection 一次，本地快照与 IOptions 绑定共用同一 section，避免两处读取漂移
        var blobStorageSection = configuration.GetSection(AbpAdminBlobStorageOptions.SectionName);
        var blobOptions = blobStorageSection.Get<AbpAdminBlobStorageOptions>() ?? new AbpAdminBlobStorageOptions();
        services.Configure<AbpAdminBlobStorageOptions>(blobStorageSection);

        services.Configure<AbpBlobStoringOptions>(options =>
        {
            options.Containers.ConfigureDefault(container =>
            {
                // 显式写出默认值。ABP 的 BlobContainerConfiguration.IsMultiTenant 默认已是 true，
                // 本行只是把默认值写明，防止后人误改成 false。
                container.IsMultiTenant = true;
                switch (blobOptions.Provider)
                {
                    case "FileSystem":
                        container.UseFileSystem(fs =>
                        {
                            fs.BasePath = blobOptions.FileSystem.BasePath;
                            fs.AppendContainerNameToBasePath = blobOptions.FileSystem.AppendContainerNameToBasePath;
                        });
                        break;
                    case "Aliyun":
                        container.UseAliyun(a =>
                        {
                            a.AccessKeyId = blobOptions.Aliyun.AccessKeyId;
                            a.AccessKeySecret = blobOptions.Aliyun.AccessKeySecret;
                            a.Endpoint = blobOptions.Aliyun.Endpoint;
                            a.ContainerName = blobOptions.Aliyun.ContainerName;
                            a.CreateContainerIfNotExists = blobOptions.Aliyun.CreateContainerIfNotExists;
                        });
                        break;
                    case "Aws":
                        container.UseAws(a =>
                        {
                            a.AccessKeyId = blobOptions.Aws.AccessKeyId;
                            a.SecretAccessKey = blobOptions.Aws.SecretAccessKey;
                            a.Region = blobOptions.Aws.Region;
                            a.ServiceURL = blobOptions.Aws.ServiceURL;
                            a.ContainerName = blobOptions.Aws.ContainerName;
                            a.CreateContainerIfNotExists = blobOptions.Aws.CreateContainerIfNotExists;
                            a.UseCredentials = blobOptions.Aws.UseCredentials;
                        });
                        break;
                    case "Minio":
                        container.UseMinio(m =>
                        {
                            m.EndPoint = blobOptions.Minio.EndPoint;
                            m.AccessKey = blobOptions.Minio.AccessKey;
                            m.SecretKey = blobOptions.Minio.SecretKey;
                            m.BucketName = blobOptions.Minio.BucketName;
                            m.WithSSL = blobOptions.Minio.WithSSL;
                            m.CreateBucketIfNotExists = blobOptions.Minio.CreateBucketIfNotExists;
                        });
                        break;
                    default:
                        container.UseDatabase();
                        break;
                }
            });
        });
    }
}
