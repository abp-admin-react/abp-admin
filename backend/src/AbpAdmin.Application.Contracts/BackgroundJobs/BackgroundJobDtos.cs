using System;
using System.ComponentModel.DataAnnotations;
using System.Threading.Tasks;
using Volo.Abp.Application.Dtos;
using Volo.Abp.Application.Services;

namespace AbpAdmin.BackgroundJobs;

public interface IBackgroundJobAppService : IApplicationService
{
    Task<PagedResultDto<BackgroundJobDto>> GetListAsync(GetBackgroundJobListInput input);

    Task<BackgroundJobDto> GetAsync(Guid id);

    Task DeleteAsync(Guid id);

    Task AbandonAsync(Guid id);

    Task RetryAsync(Guid id);

    Task EnqueueTestAsync(EnqueueTestBackgroundJobDto input);
}

public class GetBackgroundJobListInput : PagedAndSortedResultRequestDto
{
    public string? JobName { get; set; }

    public bool? IsAbandoned { get; set; }

    public bool? HasCompleted { get; set; }
}

public class BackgroundJobDto : EntityDto<Guid>
{
    public string? ApplicationName { get; set; }

    public string JobName { get; set; } = default!;

    public string JobArgs { get; set; } = default!;

    public short TryCount { get; set; }

    public DateTime CreationTime { get; set; }

    public DateTime NextTryTime { get; set; }

    public DateTime? LastTryTime { get; set; }

    public bool IsAbandoned { get; set; }

    public DateTime? CompletionTime { get; set; }

    public byte Priority { get; set; }
}

public class EnqueueTestBackgroundJobDto
{
    [Required]
    [EmailAddress]
    public string EmailAddress { get; set; } = default!;

    [Required]
    [StringLength(256)]
    public string Subject { get; set; } = default!;

    public string? Body { get; set; }
}
