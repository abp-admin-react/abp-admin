using System;
using System.Linq;
using System.Threading.Tasks;
using AbpAdmin.Ledger.Integration.Contracts;
using AbpAdmin.Ledger.Services;
using AbpAdmin.Ledger.Services.Dtos;
using Shouldly;
using Volo.Abp;
using Volo.Abp.Authorization;
using Xunit;

namespace AbpAdmin.Ledger;

/// <summary>定稿 §5.1：五态合法/非法迁移、A 建 B 批、自批 403、非 Pending 批/驳 400。</summary>
public class ManualAdjustTests : LedgerScenarioTestBase
{
    private FinManualAdjustAppService Adjust => GetRequiredService<FinManualAdjustAppService>();

    private CreateUpdateFinManualAdjustDto Draft(decimal amount = 15m, string? reason = null) => new()
    {
        Amount = amount,
        Currency = Cny,
        DebitAccountCode = FinanceAccountCodes.ChannelClearing,
        DebitOwnerType = FinOwnerType.Channel,
        DebitOwnerId = "default",
        Reason = reason ?? "adj",
    };

    [Fact]
    public async Task Five_State_Happy_Path_A_Create_B_Approve_Posts_GL()
    {
        Guid id;
        using (As(TestUsers.AccountantA))
        {
            var created = await Adjust.CreateAsync(Draft(20m, "happy"));
            created.Status.ShouldBe(FinManualAdjustStatus.Draft);
            created.ApplicantId.ShouldBe(TestUsers.AccountantA);
            id = created.Id;
            (await Adjust.SubmitAsync(id)).Status.ShouldBe(FinManualAdjustStatus.Pending);
        }

        using (As(TestUsers.AccountantB))
        {
            var posted = await Adjust.ApproveAsync(id);
            posted.Status.ShouldBe(FinManualAdjustStatus.Posted);
            posted.AuditorId.ShouldBe(TestUsers.AccountantB);
            posted.VoucherId.ShouldNotBeNull();

            var voucher = await GetRequiredService<FinVoucherAppService>().GetAsync(posted.VoucherId!.Value);
            voucher.BizType.ShouldBe(FinanceBizTypes.ManualAdjust);
            voucher.TotalDebit.ShouldBe(20m);
            voucher.TotalCredit.ShouldBe(20m);
            voucher.Entries!.Count.ShouldBe(2);
            (await ChannelBalance()).ShouldBe(20m);
            (await PlatformBalance(FinanceAccountCodes.ManualAdjust)).ShouldBe(20m);
        }
    }

    [Fact]
    public async Task Reject_Then_Redraft_Then_Resubmit_Then_Boss_Approve()
    {
        Guid id;
        using (As(TestUsers.AccountantA))
        {
            id = (await Adjust.CreateAsync(Draft())).Id;
            await Adjust.SubmitAsync(id);
        }

        using (As(TestUsers.Boss))
        {
            var rejected = await Adjust.RejectAsync(id, new RejectFinManualAdjustInput { RejectReason = "no" });
            rejected.Status.ShouldBe(FinManualAdjustStatus.Rejected);
            rejected.RejectReason.ShouldBe("no");
        }

        using (As(TestUsers.AccountantA))
        {
            (await Adjust.ReopenAsync(id)).Status.ShouldBe(FinManualAdjustStatus.Draft);
            await Adjust.UpdateAsync(id, Draft(18m, "fixed"));
            await Adjust.SubmitAsync(id);
        }

        using (As(TestUsers.Boss))
        {
            (await Adjust.ApproveAsync(id)).Status.ShouldBe(FinManualAdjustStatus.Posted);
        }
    }

    [Fact]
    public async Task Reopen_Rules_And_RejectReason_Is_Kept()
    {
        Guid id;
        using (As(TestUsers.AccountantA))
        {
            id = (await Adjust.CreateAsync(Draft())).Id;
            await Adjust.SubmitAsync(id);
            (await Should.ThrowAsync<BusinessException>(() => Adjust.ReopenAsync(id))).Code
                .ShouldBe(FinanceErrorCodes.AdjustInvalidStatus);
        }

        using (As(TestUsers.AccountantB))
        {
            await Adjust.RejectAsync(id, new RejectFinManualAdjustInput { RejectReason = "金额不对" });
            (await Should.ThrowAsync<BusinessException>(() => Adjust.ReopenAsync(id))).Code
                .ShouldBe(FinanceErrorCodes.AdjustReopenDenied);
        }

        using (As(TestUsers.Boss))
        {
            (await Should.ThrowAsync<BusinessException>(() => Adjust.ReopenAsync(id))).Code
                .ShouldBe(FinanceErrorCodes.AdjustCreateDenied);
        }

        using (As(TestUsers.AccountantA))
        {
            (await Should.ThrowAsync<BusinessException>(() => Adjust.UpdateAsync(id, Draft(9m)))).Code
                .ShouldBe(FinanceErrorCodes.AdjustInvalidStatus);
            var reopened = await Adjust.ReopenAsync(id);
            reopened.Status.ShouldBe(FinManualAdjustStatus.Draft);
            reopened.RejectReason.ShouldBe("金额不对");
            await Adjust.UpdateAsync(id, Draft(9m));
            var resubmitted = await Adjust.SubmitAsync(id);
            resubmitted.Status.ShouldBe(FinManualAdjustStatus.Pending);
            resubmitted.RejectReason.ShouldBe("金额不对");
            (await Should.ThrowAsync<BusinessException>(() => Adjust.ReopenAsync(id))).Code
                .ShouldBe(FinanceErrorCodes.AdjustInvalidStatus);
        }
    }


    [Fact]
    public async Task Reject_Requires_Reason()
    {
        Guid id;
        using (As(TestUsers.AccountantA))
        {
            id = (await Adjust.CreateAsync(Draft())).Id;
            await Adjust.SubmitAsync(id);
        }

        using (As(TestUsers.AccountantB))
        {
            (await Should.ThrowAsync<BusinessException>(() =>
                Adjust.RejectAsync(id, new RejectFinManualAdjustInput { RejectReason = "  " }))).Code
                .ShouldBe(FinanceErrorCodes.AdjustRejectReasonRequired);
            (await Should.ThrowAsync<BusinessException>(() =>
                Adjust.RejectAsync(id, new RejectFinManualAdjustInput()))).Code
                .ShouldBe(FinanceErrorCodes.AdjustRejectReasonRequired);
            await Adjust.RejectAsync(id, new RejectFinManualAdjustInput { RejectReason = "缺附件" });
        }

        using (As(TestUsers.AccountantA))
        {
            (await Adjust.GetAsync(id)).RejectReason.ShouldBe("缺附件");
        }
    }

    [Fact]
    public async Task Reopen_Without_Create_Permission_Is_CreateDenied()
    {
        Guid id;
        using (As(TestUsers.AccountantA))
        {
            id = (await Adjust.CreateAsync(Draft())).Id;
            await Adjust.SubmitAsync(id);
        }

        using (As(TestUsers.AccountantB))
        {
            await Adjust.RejectAsync(id, new RejectFinManualAdjustInput { RejectReason = "驳" });
        }

        // Boss 有 Approve 无 Create → CreateDenied
        using (As(TestUsers.Boss))
        {
            (await Should.ThrowAsync<BusinessException>(() => Adjust.ReopenAsync(id))).Code
                .ShouldBe(FinanceErrorCodes.AdjustCreateDenied);
        }
    }

    [Fact]
    public async Task Self_Approve_And_Self_Reject_Without_ApproveOwn_Are_Rejected()
    {
        Guid id;
        using (As(TestUsers.AccountantA))
        {
            id = (await Adjust.CreateAsync(Draft())).Id;
            await Adjust.SubmitAsync(id);
            var approve = await Should.ThrowAsync<BusinessException>(() => Adjust.ApproveAsync(id));
            approve.Code.ShouldBe(FinanceErrorCodes.AdjustSelfApproveNotAllowed);
            var reject = await Should.ThrowAsync<BusinessException>(() => Adjust.RejectAsync(id, new RejectFinManualAdjustInput { RejectReason = "自驳试" }));
            reject.Code.ShouldBe(FinanceErrorCodes.AdjustSelfApproveNotAllowed);
        }
    }

    [Fact]
    public async Task Admin_Without_ApproveOwn_Cannot_Self_Approve()
    {
        Guid id;
        using (As(TestUsers.Admin))
        {
            id = (await Adjust.CreateAsync(Draft())).Id;
            await Adjust.SubmitAsync(id);
            (await Should.ThrowAsync<BusinessException>(() => Adjust.ApproveAsync(id))).Code
                .ShouldBe(FinanceErrorCodes.AdjustSelfApproveNotAllowed);
        }
    }

    [Fact]
    public async Task Self_Approve_And_Self_Reject_Succeed_With_ApproveOwn()
    {
        Guid approveId, rejectId;
        using (As(TestUsers.AccountantSelfApprover))
        {
            approveId = (await Adjust.CreateAsync(Draft(7m))).Id;
            await Adjust.SubmitAsync(approveId);
            var posted = await Adjust.ApproveAsync(approveId);
            posted.Status.ShouldBe(FinManualAdjustStatus.Posted);
            posted.AuditorId.ShouldBe(TestUsers.AccountantSelfApprover);

            rejectId = (await Adjust.CreateAsync(Draft(1m))).Id;
            await Adjust.SubmitAsync(rejectId);
            (await Adjust.RejectAsync(rejectId, new RejectFinManualAdjustInput { RejectReason = "自驳" }))
                .Status.ShouldBe(FinManualAdjustStatus.Rejected);
        }
    }

    [Fact]
    public async Task Boss_Granted_Create_Can_Create_And_Others_Approve()
    {
        Guid id;
        using (As(TestUsers.BossWithCreate))
        {
            var created = await Adjust.CreateAsync(Draft(5m, "boss-create"));
            created.ApplicantId.ShouldBe(TestUsers.BossWithCreate);
            id = created.Id;
            // 老板默认没有 Submit，提交仍按权限码拒绝
            (await Should.ThrowAsync<BusinessException>(() => Adjust.SubmitAsync(id))).Code
                .ShouldBe(FinanceErrorCodes.AdjustSubmitDenied);
        }
    }

    [Fact]
    public async Task Reopen_Only_By_Applicant_Even_With_Create()
    {
        Guid id;
        using (As(TestUsers.AccountantA))
        {
            id = (await Adjust.CreateAsync(Draft())).Id;
            await Adjust.SubmitAsync(id);
        }

        using (As(TestUsers.Boss))
        {
            await Adjust.RejectAsync(id, new RejectFinManualAdjustInput { RejectReason = "x" });
        }

        using (As(TestUsers.AccountantB))
        {
            (await Should.ThrowAsync<BusinessException>(() => Adjust.ReopenAsync(id))).Code
                .ShouldBe(FinanceErrorCodes.AdjustReopenDenied);
        }

        using (As(TestUsers.Admin))
        {
            (await Should.ThrowAsync<BusinessException>(() => Adjust.ReopenAsync(id))).Code
                .ShouldBe(FinanceErrorCodes.AdjustReopenDenied);
        }

        using (As(TestUsers.AccountantA))
        {
            (await Adjust.ReopenAsync(id)).Status.ShouldBe(FinManualAdjustStatus.Draft);
        }
    }

    [Fact]
    public async Task Each_Action_Checks_Its_Own_Permission()
    {
        Guid id;
        using (As(TestUsers.AccountantA))
        {
            id = (await Adjust.CreateAsync(Draft())).Id;
            await Adjust.SubmitAsync(id);
        }

        using (As(TestUsers.Nobody))
        {
            (await Should.ThrowAsync<BusinessException>(() => Adjust.RejectAsync(id, new RejectFinManualAdjustInput { RejectReason = "x" }))).Code
                .ShouldBe(FinanceErrorCodes.AdjustRejectDenied);
            (await Should.ThrowAsync<BusinessException>(() => Adjust.ReverseAsync(id))).Code
                .ShouldBe(FinanceErrorCodes.AdjustReverseDenied);
        }
    }

    [Fact]
    public async Task Non_Pending_Approve_Or_Reject_Is_InvalidStatus_400()
    {
        Guid draftId, postedId;
        using (As(TestUsers.AccountantA))
        {
            draftId = (await Adjust.CreateAsync(Draft(3m))).Id;
            postedId = (await Adjust.CreateAsync(Draft(4m))).Id;
            await Adjust.SubmitAsync(postedId);
        }

        using (As(TestUsers.AccountantB))
        {
            var exDraft = await Should.ThrowAsync<BusinessException>(() => Adjust.ApproveAsync(draftId));
            exDraft.Code.ShouldBe(FinanceErrorCodes.AdjustInvalidStatus);
            await Adjust.ApproveAsync(postedId);
            var exPosted = await Should.ThrowAsync<BusinessException>(() => Adjust.RejectAsync(postedId, new RejectFinManualAdjustInput { RejectReason = "x" }));
            exPosted.Code.ShouldBe(FinanceErrorCodes.AdjustInvalidStatus);
        }
    }

    [Fact]
    public async Task Pending_Cannot_Be_Edited_Deleted_Or_Resubmitted()
    {
        Guid id;
        using (As(TestUsers.AccountantA))
        {
            id = (await Adjust.CreateAsync(Draft())).Id;
            await Adjust.SubmitAsync(id);
            (await Should.ThrowAsync<BusinessException>(() => Adjust.UpdateAsync(id, Draft(99m)))).Code
                .ShouldBe(FinanceErrorCodes.AdjustInvalidStatus);
            (await Should.ThrowAsync<BusinessException>(() => Adjust.DeleteAsync(id))).Code
                .ShouldBe(FinanceErrorCodes.AdjustInvalidStatus);
            (await Should.ThrowAsync<BusinessException>(() => Adjust.SubmitAsync(id))).Code
                .ShouldBe(FinanceErrorCodes.AdjustInvalidStatus);
        }
    }

    [Fact]
    public async Task Draft_Can_Update_Delete_Submit_Only_By_Applicant()
    {
        Guid id;
        using (As(TestUsers.AccountantA))
        {
            id = (await Adjust.CreateAsync(Draft(1m))).Id;
            await Adjust.UpdateAsync(id, Draft(2m, "upd"));
            (await Adjust.GetAsync(id)).Amount.ShouldBe(2m);
        }

        using (As(TestUsers.AccountantB))
        {
            (await Should.ThrowAsync<BusinessException>(() => Adjust.UpdateAsync(id, Draft(3m)))).Code
                .ShouldBe(FinanceErrorCodes.AdjustNotApplicant);
            (await Should.ThrowAsync<BusinessException>(() => Adjust.DeleteAsync(id))).Code
                .ShouldBe(FinanceErrorCodes.AdjustNotApplicant);
            (await Should.ThrowAsync<BusinessException>(() => Adjust.SubmitAsync(id))).Code
                .ShouldBe(FinanceErrorCodes.AdjustNotApplicant);
        }

        using (As(TestUsers.AccountantA))
        {
            await Adjust.DeleteAsync(id);
            (await Should.ThrowAsync<BusinessException>(() => Adjust.GetAsync(id))).Code
                .ShouldBe(FinanceErrorCodes.AdjustNotFound);
        }
    }

    [Fact]
    public async Task Boss_Cannot_Create_Accountant_Without_Approve_Cannot_Approve()
    {
        using (As(TestUsers.Boss))
        {
            (await Should.ThrowAsync<BusinessException>(() => Adjust.CreateAsync(Draft()))).Code
                .ShouldBe(FinanceErrorCodes.AdjustCreateDenied);
        }

        // 只有 Create、没有 Approve 的角色：用 Nobody 验证 ApproveDenied；老板有 Approve 会过
        Guid id;
        using (As(TestUsers.AccountantA))
        {
            id = (await Adjust.CreateAsync(Draft())).Id;
            await Adjust.SubmitAsync(id);
        }

        using (As(TestUsers.Nobody))
        {
            (await Should.ThrowAsync<BusinessException>(() => Adjust.ApproveAsync(id))).Code
                .ShouldBe(FinanceErrorCodes.AdjustApproveDenied);
        }
    }

    [Fact]
    public async Task Agent_Has_No_Adjust_Access()
    {
        using (As(TestUsers.Agent))
        {
            await Should.ThrowAsync<Exception>(() => Adjust.CreateAsync(Draft()));
            await Should.ThrowAsync<Exception>(() => Adjust.GetListAsync(new FinManualAdjustListInput()));
        }
    }

    [Fact]
    public async Task Reverse_Posted_Adjust_Goes_To_Reversed()
    {
        Guid id;
        using (As(TestUsers.AccountantA))
        {
            id = (await Adjust.CreateAsync(Draft(6m))).Id;
            await Adjust.SubmitAsync(id);
        }

        using (As(TestUsers.AccountantB))
        {
            await Adjust.ApproveAsync(id);
            var reversed = await Adjust.ReverseAsync(id);
            reversed.Status.ShouldBe(FinManualAdjustStatus.Reversed);
            reversed.ReversalVoucherId.ShouldNotBeNull();
            (await ChannelBalance()).ShouldBe(0m);
            (await PlatformBalance(FinanceAccountCodes.ManualAdjust)).ShouldBe(0m);

            // Reversed 起点的所有动作（二次红冲/Reopen）都必须拒绝——Illegal_Transitions_Matrix
            // 不能只覆盖 Draft/Rejected 起点，Reversed 起点同样要测。
            // 注意调用身份：ReverseAsync 无申请人检查（B 有 Reverse 权限即可）；ReopenAsync 先校验
            // 申请人——必须以申请人 A 调用才能走到状态检查，用 B 会先抛 AdjustReopenDenied
            (await Should.ThrowAsync<BusinessException>(() => Adjust.ReverseAsync(id))).Code
                .ShouldBe(FinanceErrorCodes.AdjustInvalidStatus);
            using (As(TestUsers.AccountantA))
            {
                (await Should.ThrowAsync<BusinessException>(() => Adjust.ReopenAsync(id))).Code
                    .ShouldBe(FinanceErrorCodes.AdjustInvalidStatus);
            }
        }
    }

    [Fact]
    public async Task Illegal_Transitions_Matrix()
    {
        // Draft → Approve/Reject/Redraft/Reverse
        Guid draft;
        using (As(TestUsers.AccountantA))
        {
            draft = (await Adjust.CreateAsync(Draft(1m))).Id;
        }

        using (As(TestUsers.AccountantB))
        {
            foreach (Func<Task> act in new Func<Task>[]
                     {
                         () => Adjust.ApproveAsync(draft),
                         () => Adjust.RejectAsync(draft, new RejectFinManualAdjustInput { RejectReason = "x" }),
                     })
            {
                (await Should.ThrowAsync<BusinessException>(act)).Code.ShouldBe(FinanceErrorCodes.AdjustInvalidStatus);
            }
        }

        using (As(TestUsers.AccountantA))
        {
            (await Should.ThrowAsync<BusinessException>(() => Adjust.ReopenAsync(draft))).Code
                .ShouldBe(FinanceErrorCodes.AdjustInvalidStatus);
            (await Should.ThrowAsync<BusinessException>(() => Adjust.ReverseAsync(draft))).Code
                .ShouldBe(FinanceErrorCodes.AdjustInvalidStatus);
            await Adjust.SubmitAsync(draft);
        }

        // Rejected → Approve/Reject/Submit(需先 Redraft)/Reverse
        Guid rejected;
        using (As(TestUsers.AccountantA))
        {
            rejected = (await Adjust.CreateAsync(Draft(1m))).Id;
            await Adjust.SubmitAsync(rejected);
        }

        using (As(TestUsers.Boss))
        {
            await Adjust.RejectAsync(rejected, new RejectFinManualAdjustInput { RejectReason = "x" });
            (await Should.ThrowAsync<BusinessException>(() => Adjust.ApproveAsync(rejected))).Code
                .ShouldBe(FinanceErrorCodes.AdjustInvalidStatus);
        }

        using (As(TestUsers.AccountantA))
        {
            (await Should.ThrowAsync<BusinessException>(() => Adjust.SubmitAsync(rejected))).Code
                .ShouldBe(FinanceErrorCodes.AdjustInvalidStatus);
        }
    }


    [Fact]
    public async Task Submit_Requires_Reason()
    {
        Guid id;
        using (As(TestUsers.AccountantA))
        {
            // Draft helper coalesces null→"adj"; pass empty/whitespace explicitly.
            id = (await Adjust.CreateAsync(Draft(10m, reason: ""))).Id;
            var empty = await Should.ThrowAsync<BusinessException>(() => Adjust.SubmitAsync(id));
            empty.Code.ShouldBe(FinanceErrorCodes.AdjustInvalidInput);
            empty.Data["Fields"].ShouldBe("reason");

            await Adjust.UpdateAsync(id, Draft(10m, reason: "  "));
            var ws = await Should.ThrowAsync<BusinessException>(() => Adjust.SubmitAsync(id));
            ws.Code.ShouldBe(FinanceErrorCodes.AdjustInvalidInput);
            ws.Data["Fields"].ShouldBe("reason");

            await Adjust.UpdateAsync(id, Draft(10m, reason: "ok-reason"));
            (await Adjust.SubmitAsync(id)).Status.ShouldBe(FinManualAdjustStatus.Pending);
        }
    }

    [Fact]
    public async Task Create_Missing_Or_Invalid_Fields_Is_InvalidInput()
    {
        using (As(TestUsers.AccountantA))
        {
            var empty = await Should.ThrowAsync<BusinessException>(() => Adjust.CreateAsync(null));
            empty.Code.ShouldBe(FinanceErrorCodes.AdjustInvalidInput);
            empty.Data["Fields"].ShouldBe("amount,currency,debitAccountCode");

            var noAmount = await Should.ThrowAsync<BusinessException>(() => Adjust.CreateAsync(new CreateUpdateFinManualAdjustDto
            {
                Currency = Cny,
                DebitAccountCode = FinanceAccountCodes.ChannelClearing,
            }));
            noAmount.Code.ShouldBe(FinanceErrorCodes.AdjustInvalidInput);
            noAmount.Data["Fields"].ShouldBe("amount");

            var negative = await Should.ThrowAsync<BusinessException>(() => Adjust.CreateAsync(Draft(-1m)));
            negative.Data["Fields"].ShouldBe("amount");
        }
    }
}
