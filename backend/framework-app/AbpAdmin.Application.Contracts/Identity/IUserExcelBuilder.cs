using System;
using System.Threading;
using System.Threading.Tasks;
using AbpAdmin.Grids;

namespace AbpAdmin.Identity;

/// <summary>同步下载和后台作业共用用户 Excel 生成器，列表导出使用与网格相同的查询条件。</summary>
public interface IUserExcelBuilder
{
    Task<byte[]> BuildAsync(string? filter, int maxResultCount, bool maskSensitive, CancellationToken cancellationToken = default);

    Task<long> CountGridAsync(GridQueryInput query, bool maskSensitive, DateTimeOffset asOf, CancellationToken cancellationToken = default);

    Task<byte[]> BuildGridAsync(GridQueryInput query, int maxResultCount, bool maskSensitive, DateTimeOffset asOf, CancellationToken cancellationToken = default);
}
