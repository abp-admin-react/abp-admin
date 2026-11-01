using System;
using System.ComponentModel.DataAnnotations;
using System.Threading.Tasks;
using Volo.Abp.Application.Dtos;
using Volo.Abp.Application.Services;

using AbpAdmin.OperationLogs;

namespace AbpAdmin.Posts;

public interface IPostAppService : IApplicationService
{
    Task<PagedResultDto<PostDto>> GetListAsync(GetPostListInput input);

    /// <summary>启用状态的岗位下拉（用户表单用）。</summary>
    Task<ListResultDto<PostDto>> GetLookupAsync();

    Task<PostDto> CreateAsync(CreatePostDto input);

    Task<PostDto> UpdateAsync(Guid id, UpdatePostDto input);

    Task DeleteAsync(Guid id);

    Task<PagedResultDto<PostUserDto>> GetMembersAsync(Guid id, GetPostMembersInput input);

    Task AddMembersAsync(Guid id, Guid[] userIds);

    Task RemoveMemberAsync(Guid id, Guid userId);

    /// <summary>用户已关联的岗位（用户详情侧展示用）。</summary>
    Task<ListResultDto<PostDto>> GetPostsByUserAsync(Guid userId);
}

public class GetPostListInput : PagedAndSortedResultRequestDto
{
    /// <summary>按名称/编码模糊过滤。</summary>
    public string? Filter { get; set; }

    /// <summary>按状态过滤；null 为全部。</summary>
    public PostStatusEnum? Status { get; set; }
}

public class PostDto : ExtensibleFullAuditedEntityDto<Guid>
{
    public string Name { get; set; } = default!;

    public string Code { get; set; } = default!;

    public int SortOrder { get; set; }

    public PostStatusEnum Status { get; set; }

    public string? Remark { get; set; }

    public int MemberCount { get; set; }
}

public class CreatePostDto
{
    [Required]
    [StringLength(PostConsts.MaxNameLength)]
    public string Name { get; set; } = default!;

    [Required]
    [StringLength(PostConsts.MaxCodeLength)]
    public string Code { get; set; } = default!;

    public int SortOrder { get; set; }

    public PostStatusEnum Status { get; set; } = PostStatusEnum.Enabled;

    [StringLength(PostConsts.MaxRemarkLength)]
    public string? Remark { get; set; }
}

public class UpdatePostDto
{
    /// <summary>显示名同时服务操作日志 _diff 输出（【岗位名称】xx → yy）。</summary>
    [Required]
    [StringLength(PostConsts.MaxNameLength)]
    [OperationLogField("岗位名称")]
    public string Name { get; set; } = default!;

    [Required]
    [StringLength(PostConsts.MaxCodeLength)]
    [OperationLogField("岗位编码")]
    public string Code { get; set; } = default!;

    [OperationLogField("显示顺序")]
    public int SortOrder { get; set; }

    [OperationLogField("状态")]
    public PostStatusEnum Status { get; set; }

    [StringLength(PostConsts.MaxRemarkLength)]
    [OperationLogField("备注")]
    public string? Remark { get; set; }
}

public class GetPostMembersInput : PagedResultRequestDto
{
    public string? Filter { get; set; }
}

public class PostUserDto : EntityDto<Guid>
{
    public string UserName { get; set; } = default!;

    public string? Name { get; set; }

    public string? Email { get; set; }

    public bool IsActive { get; set; }
}
