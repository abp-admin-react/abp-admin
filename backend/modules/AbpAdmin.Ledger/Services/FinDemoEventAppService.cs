using System;
using System.Threading.Tasks;
using AbpAdmin.Ledger.Integration.Contracts;
using AbpAdmin.Ledger.Permissions;
using AbpAdmin.Ledger.Posting;
using AbpAdmin.Ledger.Services.Dtos;
using Microsoft.AspNetCore.Mvc;
using Volo.Abp;

namespace AbpAdmin.Ledger.Services;

/// <summary>
/// 演示过账入口（培训/e2e）。POST /api/app/fin-demo-event
/// 仅接受 BizType 以 demo. 开头；真实业务走进程内 ILedgerIntegrationService。
/// </summary>
public class FinDemoEventAppService : FinanceAppServiceBase
{
    private readonly LedgerPostingEngine _engine;

    public FinDemoEventAppService(LedgerPostingEngine engine) => _engine = engine;

    public virtual async Task<PostResultDto> CreateAsync(PostDemoEventInput input)
    {
        await RequireAnyAsync(null, FinancePermissions.Adjust.Create);
        if (string.IsNullOrWhiteSpace(input.BizType) || !input.BizType.StartsWith("demo.", StringComparison.Ordinal))
        {
            throw new BusinessException(FinanceErrorCodes.InvalidEvent)
                .WithData("Field", "BizType must start with demo.");
        }

        var id = await _engine.PostAsync(new FinanceBizEvent(
            input.BizType, input.BizRefId, input.Phase, input.Currency, input.Amount,
            input.UserId, input.AgentId, Dimensions: input.Dimensions, Summary: input.Summary,
            OperatorId: CurrentUser.Id, BatchKey: input.BatchKey));
        return new PostResultDto { VoucherId = id };
    }
}
