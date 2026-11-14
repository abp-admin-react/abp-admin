using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AbpAdmin.Collection.Contracts;
using AbpAdmin.Collection.Permissions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Volo.Abp;
using Volo.Abp.Application.Services;
using Volo.Abp.Uow;

namespace AbpAdmin.Collection.Services;

[Authorize(CollectionPermissions.Payments.View)]
public class CollectionPaymentOperationsAppService(
    CollectionIntegrationService collections,
    CollectionPaymentAdminAppService paymentRecords,
    ICollectionRefundService refunds) : ApplicationService
{
    [HttpPost("api/app/collection-payments/{id}/query")]
    [Authorize(CollectionPermissions.Payments.Reconcile)]
    [UnitOfWork(IsDisabled = true)]
    public virtual async Task<CollectionPaymentAdminDto> QueryPaymentAsync(
        Guid id, CancellationToken cancellationToken)
    {
        AssertHost();
        await collections.QueryChannelAsync(id, cancellationToken);
        return await paymentRecords.GetAsync(id);
    }

    [HttpGet("api/app/collection-payments/{id}/refunds")]
    public virtual Task<IReadOnlyList<CollectionRefundDto>> GetRefundsAsync(
        Guid id, CancellationToken cancellationToken)
    {
        AssertHost();
        return refunds.ListAsync(id, cancellationToken);
    }

    [HttpPost("api/app/collection-refunds")]
    [Authorize(CollectionPermissions.Payments.Refund)]
    [UnitOfWork(IsDisabled = true)]
    public virtual Task<CollectionRefundDto> CreateRefundAsync(
        RequestCollectionRefundInput input, CancellationToken cancellationToken)
    {
        AssertHost();
        return refunds.RequestAsync(input, cancellationToken);
    }

    [HttpPost("api/app/collection-refunds/{id}/query")]
    [Authorize(CollectionPermissions.Payments.Refund)]
    [UnitOfWork(IsDisabled = true)]
    public virtual Task<CollectionRefundDto> QueryRefundAsync(Guid id, CancellationToken cancellationToken)
    {
        AssertHost();
        return refunds.QueryAsync(id, cancellationToken);
    }

    private void AssertHost()
    {
        if (CurrentTenant.Id != null)
        {
            throw new BusinessException(CollectionErrorCodes.HostOnly);
        }
    }
}
