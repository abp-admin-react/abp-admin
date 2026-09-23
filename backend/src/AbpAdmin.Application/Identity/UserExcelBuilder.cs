using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AbpAdmin.DataDictionaries;
using AbpAdmin.Desensitization;
using EasyAbp.Abp.DataDictionary;
using MiniExcelLibs;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Identity;

namespace AbpAdmin.Identity;

/// <summary>
/// 用户导出 Excel 构建器（同步导出与后台作业共用）。
/// 导出列：用户名、名、姓、邮箱、邮箱已确认、手机号、手机号已确认、是否启用、
/// 锁定截止时间、角色（分号分隔）、组织单元（分号分隔）、创建时间、最后密码修改时间。
/// 列名集中在 <see cref="UserExportColumnNames"/>。
/// </summary>
public class UserExcelBuilder : IUserExcelBuilder, ITransientDependency
{
    // 与 Host 脱敏注册表（IdentityUserDto.Email/PhoneNumber）等价的脱敏规格
    private static readonly MaskedAttribute EmailMaskSpec = new(MaskKindEnum.Email)
    {
        PlaintextPermission = IdentityPermissions.Users.Update,
    };

    private static readonly MaskedAttribute MobileMaskSpec = new(MaskKindEnum.Mobile)
    {
        PlaintextPermission = IdentityPermissions.Users.Update,
    };

    /// <summary>OU 归属连接表查询的 IN 列表分批大小（与用户分页同尺寸，防超长 IN 列表）。</summary>
    private const int OuMembershipBatchSize = 1000;

    private readonly IIdentityUserRepository _userRepository;
    private readonly IRepository<IdentityUserOrganizationUnit> _userOuRepository;
    private readonly IOrganizationUnitRepository _organizationUnitRepository;
    private readonly IDataDictionaryRenderer _dataDictionaryRenderer;

    public UserExcelBuilder(
        IIdentityUserRepository userRepository,
        IRepository<IdentityUserOrganizationUnit> userOuRepository,
        IOrganizationUnitRepository organizationUnitRepository,
        IDataDictionaryRenderer dataDictionaryRenderer)
    {
        _userRepository = userRepository;
        _userOuRepository = userOuRepository;
        _organizationUnitRepository = organizationUnitRepository;
        _dataDictionaryRenderer = dataDictionaryRenderer;
    }

    /// <summary>
    /// 按筛选条件分页拉取用户并生成 Excel 字节数组。
    /// maxResultCount 是硬上限：同步分支传实际条数（≤ 1000），后台作业分支传允许的最大行数。
    /// 角色名与 OU 归属均经批量接口常数次查询（修复逐用户 2 次查询的 N+1）；
    /// 用户行 includeDetails:false——ABP 对 IdentityUser 的 IncludeDetails 会连带
    /// Roles/Logins/Claims/Tokens/PasswordHistories/Passkeys 七张集合表，
    /// 导出只读标量列，全部用不到，历史库下纯属过度加载（见代码审查 round1）。
    /// </summary>
    public virtual async Task<byte[]> BuildAsync(string? filter, int maxResultCount, bool maskSensitive, CancellationToken cancellationToken = default)
    {
        var users = new List<IdentityUser>();
        var skipCount = 0;
        const int batchSize = 1000;

        while (users.Count < maxResultCount)
        {
            var remaining = maxResultCount - users.Count;
            var currentBatchSize = Math.Min(batchSize, remaining);

            // includeDetails:false：OU 列不走导航加载，改由下方连接表批量查询提供
            var batch = await _userRepository.GetListAsync(
                sorting: "UserName",
                maxResultCount: currentBatchSize,
                skipCount: skipCount,
                filter: filter,
                includeDetails: false,
                cancellationToken: cancellationToken);

            if (batch.Count == 0)
            {
                break;
            }

            users.AddRange(batch);
            skipCount += batch.Count;

            if (batch.Count < currentBatchSize)
            {
                break;
            }
        }

        // 角色名：仓储批量接口一次查询（与 UserManager.GetRolesAsync 同源：含组织单元派生角色）
        var userIds = users.Select(u => u.Id).ToList();
        var roleNamesById = (await _userRepository.GetRoleNamesAsync(userIds, cancellationToken))
            .ToDictionary(x => x.Id, x => (IList<string>)x.RoleNames.ToList());

        // OU 归属：IIdentityUserRepository 没有「按用户列表批量取 OU」的接口
        // （GetOrganizationUnitsAsync 是单用户签名），直接查身份模块连接表
        // IdentityUserOrganizationUnit，按 1000 一批避免超长 IN 列表（SQL Server 参数上限）
        var ouIdsByUserId = new Dictionary<Guid, List<Guid>>();
        foreach (var userIdChunk in userIds.Chunk(OuMembershipBatchSize))
        {
            var chunkIds = userIdChunk.ToList();
            var memberships = await _userOuRepository.GetListAsync(
                x => chunkIds.Contains(x.UserId),
                includeDetails: false,
                cancellationToken);
            foreach (var membership in memberships)
            {
                if (!ouIdsByUserId.TryGetValue(membership.UserId, out var ouIds))
                {
                    ouIdsByUserId[membership.UserId] = ouIds = new List<Guid>();
                }

                ouIds.Add(membership.OrganizationUnitId);
            }
        }

        // OU 显示名：一次查询后内存合并
        var ouIdSet = ouIdsByUserId.Values
            .SelectMany(ids => ids)
            .Distinct()
            .ToList();
        var ouDisplayNameById = ouIdSet.Count == 0
            ? new Dictionary<Guid, string>()
            : (await _organizationUnitRepository.GetListAsync(ouIdSet, cancellationToken: cancellationToken))
                .ToDictionary(ou => ou.Id, ou => ou.DisplayName);
        var ouDisplayNamesByUserId = ouIdsByUserId.ToDictionary(
            kv => kv.Key,
            kv => (IList<string>)kv.Value
                .Select(id => ouDisplayNameById.GetValueOrDefault(id))
                .Where(n => n != null)
                .Select(n => n!)
                .ToList());

        // T3.4：「是否启用」列走数据字典渲染（EnabledStatus：Enabled/Disabled → 启用/停用）。
        // 字典未种子或项缺失时渲染结果为 null，回落原硬编码文案，导出不因此失败。
        var renderDtos = users.Select(x => new UserExportRenderDto
        {
            IsActiveCode = x.IsActive
                ? AbpAdminDictionaryCodes.EnabledStatusItems.Enabled
                : AbpAdminDictionaryCodes.EnabledStatusItems.Disabled
        }).ToList();
        renderDtos = await _dataDictionaryRenderer.RenderListAsync(renderDtos);

        var rows = users.Zip(renderDtos, (x, r) => new Dictionary<string, object?>
        {
            [UserExportColumnNames.UserName] = x.UserName,
            [UserExportColumnNames.Name] = x.Name,
            [UserExportColumnNames.Surname] = x.Surname,
            // maskSensitive=true（无明文权限）时与响应序列化同一算法脱敏，
            // 防「列表看掩码、Excel 拿明文」的旁路（安全审查 H1）
            [UserExportColumnNames.Email] = maskSensitive
                ? StringMasker.Mask(x.Email, EmailMaskSpec)
                : x.Email,
            [UserExportColumnNames.EmailConfirmed] = x.EmailConfirmed ? UserImportBooleanTexts.Yes : UserImportBooleanTexts.No,
            [UserExportColumnNames.PhoneNumber] = maskSensitive
                ? StringMasker.Mask(x.PhoneNumber, MobileMaskSpec)
                : x.PhoneNumber,
            [UserExportColumnNames.PhoneNumberConfirmed] = x.PhoneNumberConfirmed ? UserImportBooleanTexts.Yes : UserImportBooleanTexts.No,
            // 字典渲染失败时回退到导入端可解析的「是/否」（词汇单一出处，见 UserImportBooleanTexts）
            [UserExportColumnNames.IsActive] = r.IsActiveText ?? (x.IsActive ? UserImportBooleanTexts.Yes : UserImportBooleanTexts.No),
            [UserExportColumnNames.LockoutEnd] = x.LockoutEnd?.ToString("yyyy-MM-dd HH:mm:ss"),
            [UserExportColumnNames.RoleNames] = string.Join(";", roleNamesById.GetValueOrDefault(x.Id, new List<string>())),
            [UserExportColumnNames.OrganizationUnits] = string.Join(";", ouDisplayNamesByUserId.GetValueOrDefault(x.Id, new List<string>())),
            [UserExportColumnNames.CreationTime] = x.CreationTime.ToString("yyyy-MM-dd HH:mm:ss"),
            [UserExportColumnNames.LastPasswordChangeTime] = x.LastPasswordChangeTime?.ToString("yyyy-MM-dd HH:mm:ss")
        });

        using var stream = new MemoryStream();
        MiniExcel.SaveAs(stream, rows);
        return stream.ToArray();
    }
}
