namespace AbpAdmin.Blobs;

/// <summary>
/// BLOB 存储强类型配置，绑定 appsettings.json 的 "Blob" 节。
/// 字段名与 ABP 各 provider 的配置参数一一对应（以 ABP 10.6 源码为准）。
/// </summary>
public class AbpAdminBlobStorageOptions
{
    public const string SectionName = "Blob";

    /// <summary>
    /// Database | FileSystem | Aliyun | Aws | Minio
    /// </summary>
    public string Provider { get; set; } = "Database";

    public FileSystemBlobOptions FileSystem { get; set; } = new();

    public AliyunBlobOptions Aliyun { get; set; } = new();

    public AwsBlobOptions Aws { get; set; } = new();

    public MinioBlobOptions Minio { get; set; } = new();
}

public class FileSystemBlobOptions
{
    public string BasePath { get; set; } = "App_Data/blobs";

    public bool AppendContainerNameToBasePath { get; set; } = true;
}

public class AliyunBlobOptions
{
    public string AccessKeyId { get; set; } = string.Empty;

    public string AccessKeySecret { get; set; } = string.Empty;

    public string Endpoint { get; set; } = string.Empty;

    public string ContainerName { get; set; } = string.Empty;

    public bool CreateContainerIfNotExists { get; set; }
}

public class AwsBlobOptions
{
    public string AccessKeyId { get; set; } = string.Empty;

    public string SecretAccessKey { get; set; } = string.Empty;

    public string Region { get; set; } = string.Empty;

    /// <summary>
    /// 注意是 ServiceURL 不是 ServiceUrl。填了之后 ABP 自动开 path style（用于 S3 兼容存储，如腾讯云 COS、MinIO）。
    /// </summary>
    public string ServiceURL { get; set; } = string.Empty;

    /// <summary>
    /// 注意 Aws provider 用 ContainerName 不是 BucketName。
    /// </summary>
    public string ContainerName { get; set; } = string.Empty;

    public bool CreateContainerIfNotExists { get; set; }

    public bool UseCredentials { get; set; } = true;
}

public class MinioBlobOptions
{
    public string EndPoint { get; set; } = string.Empty;

    public string AccessKey { get; set; } = string.Empty;

    public string SecretKey { get; set; } = string.Empty;

    /// <summary>
    /// Minio provider 自己的参数名是 BucketName。
    /// </summary>
    public string BucketName { get; set; } = string.Empty;

    public bool WithSSL { get; set; }

    public bool CreateBucketIfNotExists { get; set; }
}
