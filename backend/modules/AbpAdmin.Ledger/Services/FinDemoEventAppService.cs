using System;
using System.Threading.Tasks;
using AbpAdmin.Ledger.Integration.Contracts;
using AbpAdmin.Ledger.Permissions;
using AbpAdmin.Ledger.Posting;
using AbpAdmin.Ledger.Services.Dtos;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Hosting;
using Volo.Abp;

namespace AbpAdmin.Ledger.Services;

/// <summary>
/// 演示过账入口（培训/e2e）。POST /api/app/fin-demo-event
/// 仅接受 BizType 以 demo. 开头；真实业务走进程内 ILedgerIntegrationService。
/// 引擎契约明确「业务闸门由调用方补」（LedgerPostingEngine 类注释）：人工调账的
/// 双签闸门在 FinManualAdjustAppService，本入口若仅凭 Adjust.Create 即可单笔过账，
/// 等于给所有 Create 持有者开了一条绕开双签的侧门（sectest b06：非 Development
/// 环境必须 fail closed——与 TokenGateway simulate-paid 同款环境门）。
/// </summary>
public class FinDemoEventAppService : FinanceAppServiceBase
{
    private readonly LedgerPostingEngine _engine;
    private readonly IWebHostEnvironment _env;

    public FinDemoEventAppService(LedgerPostingEngine engine, IWebHostEnvironment env)
    {
        _engine = engine;
        _env = env;
    }

    public virtual async Task<PostResultDto> CreateAsync(PostDemoEventInput input)
    {
        // 演示面只在 Development 打开：生产/预发凭 Adjust.Create 单权限铸造任意金额凭证、
        // 污染 FinBalanceProjection（overview 的 Unearned/Recognized 不滤 IsDemo）不可接受。
        if (!_env.IsDevelopment())
        {
            throw new BusinessException(FinanceErrorCodes.InvalidEvent)
                .WithData("Field", "demo event posting is only available in Development");
        }

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
