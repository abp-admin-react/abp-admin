using Volo.Abp.DependencyInjection;
using Volo.Abp.Security.Encryption;

namespace AbpAdmin.Tenants;

/// <summary>
/// 租户连接字符串的加密/解密与掩码（T2.8 SaaS Pro 缺口第 6 项）。
///
/// 这是一处有意偏离 ABP Pro 行为的地方：Pro 的文档明确写着租户连接字符串按原样持久化、
/// 按原样返回，不加密。连接字符串包含数据库密码，明文存储与明文返回是不可接受的风险，
/// 且本项目无历史数据、没有兼容 Pro 数据格式的需求，因此改为：
/// - 存储前用 <see cref="IStringEncryptionService.Encrypt"/> 加密；
/// - 读取给「连接使用」（EditionAwareTenantStore 构建 TenantConfiguration 时）解密；
/// - 管理 API 永不返回明文：返回固定掩码 <see cref="Mask"/>，前端编辑时留空或原样回传掩码表示保持原值。
///
/// 已知限制：StringEncryption:DefaultPassPhrase 本身在 appsettings.json 里是明文，
/// 这只是提升了攻击门槛（防止拖库直接拿到数据库密码），不是完整的密钥管理方案。
/// 生产环境应把 pass phrase 放进环境变量或密钥管理服务。
/// </summary>
public class TenantConnectionStringProtector : ITransientDependency
{
    /// <summary>
    /// 管理 API 返回的固定掩码。前端把该值原样回传（或留空）表示「保持原值」。
    /// </summary>
    public const string Mask = "********";

    private readonly IStringEncryptionService _encryptionService;

    public TenantConnectionStringProtector(IStringEncryptionService encryptionService)
    {
        _encryptionService = encryptionService;
    }

    /// <summary>加密待存储的连接字符串。</summary>
    public virtual string Encrypt(string connectionString)
    {
        return _encryptionService.Encrypt(connectionString)!;
    }

    /// <summary>
    /// 解密数据库中存储的连接字符串。解密失败（例如启用加密前写入的明文旧数据）按明文回退，
    /// 保证旧数据在轮换前仍然可用；下一次保存会改写为密文。
    /// </summary>
    public virtual string DecryptOrPlain(string storedValue)
    {
        try
        {
            return _encryptionService.Decrypt(storedValue) ?? storedValue;
        }
        catch
        {
            return storedValue;
        }
    }

    /// <summary>判断入参是否为「保持原值」占位（空或掩码）。</summary>
    public static bool IsMaskOrEmpty(string? value)
    {
        return string.IsNullOrWhiteSpace(value) || value == Mask;
    }
}
