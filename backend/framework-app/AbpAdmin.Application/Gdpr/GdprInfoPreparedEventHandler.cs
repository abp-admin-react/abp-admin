using System;
using System.Threading.Tasks;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.EventBus.Distributed;
using Volo.Abp.Guids;
using Volo.Abp.Uow;

namespace AbpAdmin.Gdpr;

/// <summary>
/// GDPR 侧订阅 <see cref="GdprUserDataPreparedEto"/>，
/// 为每个模块贡献的 payload 插入一条 <see cref="GdprInfo"/>。
/// </summary>
public class GdprInfoPreparedEventHandler :
    IDistributedEventHandler<GdprUserDataPreparedEto>,
    ITransientDependency
{
    private readonly IRepository<GdprInfo, Guid> _gdprInfoRepository;
    private readonly IGuidGenerator _guidGenerator;

    public GdprInfoPreparedEventHandler(
        IRepository<GdprInfo, Guid> gdprInfoRepository,
        IGuidGenerator guidGenerator)
    {
        _gdprInfoRepository = gdprInfoRepository;
        _guidGenerator = guidGenerator;
    }

    [UnitOfWork]
    public virtual async Task HandleEventAsync(GdprUserDataPreparedEto eventData)
    {
        var info = new GdprInfo(
            _guidGenerator.Create(),
            eventData.TenantId,
            eventData.RequestId,
            eventData.Provider,
            eventData.Data);

        await _gdprInfoRepository.InsertAsync(info);
    }
}
