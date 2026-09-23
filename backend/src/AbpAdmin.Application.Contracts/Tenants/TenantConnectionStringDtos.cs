using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace AbpAdmin.Tenants;

/// <summary>
/// 租户连接字符串管理视图（T2.8 SaaS Pro 缺口第 4 项）。
/// 管理 API 永不返回明文：已设值的项 Value 为固定掩码 "********"。
/// </summary>
public class TenantConnectionStringManagementDto
{
    /// <summary>
    /// 设置项 AbpAdmin.Saas.EnableTenantBasedConnectionStringManagement 的当前值，
    /// 便于前端在无权限读取设置接口时也能判断。
    /// </summary>
    public bool IsManagementEnabled { get; set; }

    /// <summary>
    /// 「使用共享数据库」：当前无任何连接串记录时为 true，租户回退到 host 的连接串。
    /// </summary>
    public bool UseSharedDatabase { get; set; }

    /// <summary>
    /// 已有的连接串记录（Default 与模块特定）。
    /// </summary>
    public List<TenantConnectionStringItemDto> Items { get; set; } = new();
}

public class TenantConnectionStringItemDto
{
    /// <summary>
    /// "Default" 或 IsUsedByTenants = true 的数据库名（如 EasyAbpFileManagement）。
    /// </summary>
    public string Name { get; set; } = default!;

    /// <summary>
    /// 固定掩码 "********"（已设值时）；永不返回明文。
    /// </summary>
    public string? Value { get; set; }
}

/// <summary>
/// 全量同步租户的连接串记录。
/// </summary>
public class UpdateTenantConnectionStringsInput
{
    /// <summary>
    /// 勾选「使用共享数据库」：移除该租户的所有连接串记录，回退到 host 的连接串。
    /// 为 true 时忽略 Items。
    /// </summary>
    public bool UseSharedDatabase { get; set; }

    /// <summary>
    /// 期望的最终连接串集合。Value 留空或回传掩码表示保持原值；
    /// 已存在但未出现在本列表中的记录会被删除。
    /// </summary>
    public List<TenantConnectionStringItemInput> Items { get; set; } = new();
}

public class TenantConnectionStringItemInput
{
    [Required]
    [StringLength(64)]
    public string Name { get; set; } = default!;

    /// <summary>
    /// 新的连接串明文；留空或回传掩码表示保持原值。存储前会在服务端加密。
    /// </summary>
    [StringLength(1024)]
    public string? Value { get; set; }
}

/// <summary>
/// POST /api/app/tenant/{id}/check-connection-string 的入参。纯校验，不写库。
/// </summary>
public class CheckTenantConnectionStringInput
{
    [Required]
    [StringLength(1024)]
    public string ConnectionString { get; set; } = default!;
}

public class CheckTenantConnectionStringResultDto
{
    public bool IsValid { get; set; }

    /// <summary>
    /// 失败原因（本地化后的通用文案；底层驱动异常原文只进服务端日志，不回传客户端）。
    /// </summary>
    public string? ErrorMessage { get; set; }
}
