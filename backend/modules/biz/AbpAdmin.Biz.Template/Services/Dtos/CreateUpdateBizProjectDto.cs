using System.ComponentModel.DataAnnotations;

namespace AbpAdmin.Biz.Template.Services.Dtos;

public class CreateUpdateBizProjectDto
{
    [Required]
    [StringLength(BizTemplateConsts.MaxNameLength)]
    public string Name { get; set; } = default!;

    [StringLength(BizTemplateConsts.MaxDescriptionLength)]
    public string? Description { get; set; }

    public bool IsActive { get; set; }
}
