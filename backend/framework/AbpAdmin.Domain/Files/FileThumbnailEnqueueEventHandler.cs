using System;
using System.Threading.Tasks;
using Volo.Abp.BackgroundJobs;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Domain.Entities.Events;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.EventBus;
using Volo.Abp.Guids;
using File = EasyAbp.FileManagement.Files.File;
using FileType = EasyAbp.FileManagement.Files.FileType;

namespace AbpAdmin.Files;

/// <summary>
/// 文件上传完成 → 创建缩略图映射行并入队生成作业（T3.1）。
///
/// 接法说明（规格第 7 步的二选一）：T1.1 没有产出我们自己的上传 AppService 包装
/// （前端直接调模块的 multipart 端点），所以走 EntityCreatedEventData&lt;File&gt; 本地事件。
/// 这是 ABP 框架对聚合根插入的标准事件（EasyAbp 模块自己的 FileCreatedEventHandler
/// 也依赖同一个事件），不是对模块实现细节的隐式依赖；且任何上传路径（含将来其他入口）
/// 都能覆盖到。
///
/// 事件在上传请求的 UoW 完成阶段同步触发，插入映射行与入队（写 AbpBackgroundJobs）
/// 都挂同一个 UoW，不会出现「文件提交了但作业丢了」的中间态。
/// </summary>
public class FileThumbnailEnqueueEventHandler : ILocalEventHandler<EntityCreatedEventData<File>>, ITransientDependency
{
    private readonly IRepository<FileThumbnail, Guid> _thumbnailRepository;
    private readonly IBackgroundJobManager _backgroundJobManager;
    private readonly IGuidGenerator _guidGenerator;

    public FileThumbnailEnqueueEventHandler(
        IRepository<FileThumbnail, Guid> thumbnailRepository,
        IBackgroundJobManager backgroundJobManager,
        IGuidGenerator guidGenerator)
    {
        _thumbnailRepository = thumbnailRepository;
        _backgroundJobManager = backgroundJobManager;
        _guidGenerator = guidGenerator;
    }

    public virtual async Task HandleEventAsync(EntityCreatedEventData<File> eventData)
    {
        var file = eventData.Entity;
        if (file.FileType != FileType.RegularFile)
        {
            return;
        }

        // 幂等：同一 FileId 只建一行（作业重试/事件重复触发时不多建）
        if (await _thumbnailRepository.AnyAsync(x => x.FileId == file.Id))
        {
            return;
        }

        await _thumbnailRepository.InsertAsync(new FileThumbnail(_guidGenerator.Create(), file.TenantId, file.Id));

        // 所有常规文件都入队：扩展名是否支持由作业判定并落 Unsupported（验收标准要求的形状），
        // 这里不预筛，避免两处白名单判断漂移。
        await _backgroundJobManager.EnqueueAsync(new ThumbnailGenerationJobArgs
        {
            TenantId = file.TenantId,
            FileId = file.Id,
            FileName = file.FileName
        });
    }
}
