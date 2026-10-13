using System;
using System.ComponentModel.DataAnnotations;
using System.Threading.Tasks;
using Volo.Abp.Application.Dtos;
using Volo.Abp.Application.Services;

namespace AbpAdmin.ScheduledJobs;

public interface IScheduledJobAppService : IApplicationService
{
    Task<PagedResultDto<ScheduledJobDto>> GetListAsync(GetScheduledJobsInput input);

    Task<ScheduledJobDto> GetAsync(Guid id);

    Task<ScheduledJobDto> CreateAsync(CreateScheduledJobDto input);

    Task<ScheduledJobDto> UpdateAsync(Guid id, UpdateScheduledJobDto input);

    Task DeleteAsync(Guid id);

    Task<ScheduledJobDto> SetEnabledAsync(Guid id, bool isEnabled);

    Task TriggerAsync(Guid id);

    Task<PagedResultDto<ScheduledJobExecutionDto>> GetExecutionsAsync(Guid id, PagedAndSortedResultRequestDto input);

    /// <summary>返回所有已注册的 handler，供前端下拉选择 JobType。</summary>
    Task<ListResultDto<ScheduledJobTypeDto>> GetJobTypesAsync();

    /// <summary>校验 cron 表达式并返回接下来 5 次的执行时间，供前端预览。</summary>
    Task<CronPreviewDto> PreviewCronAsync(string cronExpression);
}

public class GetScheduledJobsInput : PagedAndSortedResultRequestDto
{
    public string? Name { get; set; }

    public string? JobType { get; set; }

    public bool? IsEnabled { get; set; }
}

public class ScheduledJobDto : EntityDto<Guid>
{
    public string Name { get; set; } = default!;

    public string JobType { get; set; } = default!;

    public string CronExpression { get; set; } = default!;

    public bool IsEnabled { get; set; }

    public string? Description { get; set; }

    public string? Payload { get; set; }

    public DateTime? LastRunTime { get; set; }

    public bool? LastRunSuccess { get; set; }

    public string? LastRunMessage { get; set; }

    public DateTime? NextRunTime { get; set; }
}

public class CreateScheduledJobDto
{
    [Required]
    [StringLength(ScheduledJobConsts.MaxNameLength)]
    public string Name { get; set; } = default!;

    [Required]
    [StringLength(ScheduledJobConsts.MaxJobTypeLength)]
    public string JobType { get; set; } = default!;

    [Required]
    [StringLength(ScheduledJobConsts.MaxCronExpressionLength)]
    public string CronExpression { get; set; } = default!;

    public bool IsEnabled { get; set; }

    [StringLength(ScheduledJobConsts.MaxDescriptionLength)]
    public string? Description { get; set; }

    public string? Payload { get; set; }
}

public class UpdateScheduledJobDto
{
    [Required]
    [StringLength(ScheduledJobConsts.MaxNameLength)]
    public string Name { get; set; } = default!;

    [Required]
    [StringLength(ScheduledJobConsts.MaxCronExpressionLength)]
    public string CronExpression { get; set; } = default!;

    [StringLength(ScheduledJobConsts.MaxDescriptionLength)]
    public string? Description { get; set; }

    public string? Payload { get; set; }
}

public class ScheduledJobExecutionDto : EntityDto<Guid>
{
    public Guid ScheduledJobId { get; set; }

    public DateTime StartTime { get; set; }

    public DateTime? EndTime { get; set; }

    public bool Success { get; set; }

    public string? Message { get; set; }

    public long DurationMs { get; set; }

    public DateTime CreationTime { get; set; }
}

public class ScheduledJobTypeDto
{
    public string JobType { get; set; } = default!;

    /// <summary>本地化后的展示名（服务端已按当前 UI 文化解析）。</summary>
    public string DisplayName { get; set; } = default!;
}

public class CronPreviewDto
{
    public bool IsValid { get; set; }

    public string? ErrorMessage { get; set; }

    public DateTime[] NextFireTimes { get; set; } = [];
}
