using System;
using System.Linq;
using System.Threading.Tasks;
using AbpAdmin.Permissions;
using Microsoft.AspNetCore.Authorization;
using Volo.Abp;
using Volo.Abp.Application.Dtos;
using Volo.Abp.BackgroundJobs;
using Volo.Abp.Domain.Repositories;

namespace AbpAdmin.BackgroundJobs;

[Authorize(AbpAdminPermissions.BackgroundJobs.Default)]
public class BackgroundJobAppService : AbpAdminAppService, IBackgroundJobAppService
{
    private readonly IRepository<BackgroundJobRecord, Guid> _jobRepository;
    private readonly IBackgroundJobManager _backgroundJobManager;

    public BackgroundJobAppService(
        IRepository<BackgroundJobRecord, Guid> jobRepository,
        IBackgroundJobManager backgroundJobManager)
    {
        _jobRepository = jobRepository;
        _backgroundJobManager = backgroundJobManager;
    }

    public virtual async Task<PagedResultDto<BackgroundJobDto>> GetListAsync(GetBackgroundJobListInput input)
    {
        var queryable = await _jobRepository.GetQueryableAsync();
        var query = queryable
            .WhereIf(!input.JobName.IsNullOrWhiteSpace(), x => x.JobName.Contains(input.JobName!))
            .WhereIf(input.IsAbandoned.HasValue, x => x.IsAbandoned == input.IsAbandoned)
            .WhereIf(input.HasCompleted == true, x => x.CompletionTime != null)
            .WhereIf(input.HasCompleted == false, x => x.CompletionTime == null);

        var count = await AsyncExecuter.CountAsync(query);
        var items = await AsyncExecuter.ToListAsync(
            query.OrderByDescending(x => x.CreationTime)
                .Skip(input.SkipCount)
                .Take(input.MaxResultCount));

        return new PagedResultDto<BackgroundJobDto>(count, items.Select(Map).ToList());
    }

    public virtual async Task<BackgroundJobDto> GetAsync(Guid id)
    {
        var job = await _jobRepository.GetAsync(id);
        return Map(job);
    }

    [Authorize(AbpAdminPermissions.BackgroundJobs.Delete)]
    public virtual async Task DeleteAsync(Guid id)
    {
        await _jobRepository.DeleteAsync(id);
    }

    [Authorize(AbpAdminPermissions.BackgroundJobs.Abandon)]
    public virtual async Task AbandonAsync(Guid id)
    {
        var job = await _jobRepository.GetAsync(id);
        job.IsAbandoned = true;
        await _jobRepository.UpdateAsync(job);
    }

    [Authorize(AbpAdminPermissions.BackgroundJobs.Retry)]
    public virtual async Task RetryAsync(Guid id)
    {
        var job = await _jobRepository.GetAsync(id);
        job.IsAbandoned = false;
        job.TryCount = 0;
        job.CompletionTime = null;
        job.NextTryTime = Clock.Now;
        await _jobRepository.UpdateAsync(job);
    }

    [Authorize(AbpAdminPermissions.BackgroundJobs.Enqueue)]
    public virtual async Task EnqueueTestAsync(EnqueueTestBackgroundJobDto input)
    {
        await _backgroundJobManager.EnqueueAsync(new EmailSendingArgs
        {
            EmailAddress = input.EmailAddress,
            Subject = input.Subject,
            Body = input.Body
        });
    }

    private static BackgroundJobDto Map(BackgroundJobRecord job)
    {
        return new BackgroundJobDto
        {
            Id = job.Id,
            ApplicationName = job.ApplicationName,
            JobName = job.JobName,
            JobArgs = job.JobArgs,
            TryCount = job.TryCount,
            CreationTime = job.CreationTime,
            NextTryTime = job.NextTryTime,
            LastTryTime = job.LastTryTime,
            IsAbandoned = job.IsAbandoned,
            CompletionTime = job.CompletionTime,
            Priority = (byte)job.Priority
        };
    }
}
