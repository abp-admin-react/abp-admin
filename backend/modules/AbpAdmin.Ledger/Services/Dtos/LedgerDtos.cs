using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using AbpAdmin.Ledger.Integration.Contracts;
using Volo.Abp.Application.Dtos;

namespace AbpAdmin.Ledger.Services.Dtos;

/// <summary>A1–A7 共用列表参数（定稿 §5.3）。maxResultCount 默认 20、上限 100。</summary>
public class FinListInput : IPagedAndSortedResultRequest
{
    public int SkipCount { get; set; }

    public int MaxResultCount { get; set; } = 20;

    public string? Sorting { get; set; }

    public string? Filter { get; set; }

    /// <summary>按 CreationTime 过滤（含）。</summary>
    public DateTime? StartTime { get; set; }

    /// <summary>按 CreationTime 过滤（不含）。</summary>
    public DateTime? EndTime { get; set; }

    /// <summary>逗号分隔多值，按枚举名（大小写不敏感）。</summary>
    public string? Status { get; set; }
}

public class FinVoucherListInput : FinListInput
{
    public Guid? AgentId { get; set; }

    public bool? IsDemo { get; set; }

    public string? BizType { get; set; }

    public string? BizRefId { get; set; }

    public string? Phase { get; set; }
}

public class FinVoucherDto : EntityDto<Guid>
{
    public string BizType { get; set; } = default!;
    public string BizRefId { get; set; } = default!;
    public string Phase { get; set; } = default!;
    public string BatchKey { get; set; } = default!;
    public int RuleVersion { get; set; }
    public Guid? PeriodId { get; set; }
    public FinVoucherStatus Status { get; set; }
    public Guid? ReversalOf { get; set; }
    public Guid? ReversedBy { get; set; }
    public Guid? AgentId { get; set; }
    public Guid? UserId { get; set; }
    public bool IsDemo { get; set; }
    public string? Summary { get; set; }
    public string Currency { get; set; } = default!;
    public decimal Amount { get; set; }
    public decimal TotalDebit { get; set; }
    public decimal TotalCredit { get; set; }
    public Guid? CreatorId { get; set; }
    public DateTime CreationTime { get; set; }
    public List<FinEntryDto>? Entries { get; set; }
}

public class FinEntryDto
{
    public int Seq { get; set; }
    public string AccountCode { get; set; } = default!;
    public string AccountName { get; set; } = default!;
    public FinOwnerType OwnerType { get; set; }
    public string OwnerId { get; set; } = default!;
    public string Bucket { get; set; } = default!;
    public decimal Debit { get; set; }
    public decimal Credit { get; set; }
    public decimal? BalanceAfter { get; set; }
    public string? Dimensions { get; set; }
}

public class FinManualAdjustListInput : FinListInput
{
    public Guid? ApplicantId { get; set; }
}

/// <summary>必填项（amount/currency/debitAccountCode）在服务里校验，缺失统一返回 Finance:Adjust:InvalidInput（400）。</summary>
public class CreateUpdateFinManualAdjustDto
{
    public decimal? Amount { get; set; }

    [StringLength(16)]
    public string? Currency { get; set; }

    [StringLength(32)]
    public string? DebitAccountCode { get; set; }

    public FinOwnerType DebitOwnerType { get; set; } = FinOwnerType.Platform;

    [StringLength(64)]
    public string? DebitOwnerId { get; set; }

    /// <summary>缺省 3101 人工调整（定稿 §3 manual-adjust/post）。</summary>
    [StringLength(32)]
    public string? CreditAccountCode { get; set; }

    public FinOwnerType CreditOwnerType { get; set; } = FinOwnerType.Platform;

    [StringLength(64)]
    public string? CreditOwnerId { get; set; }

    [StringLength(512)]
    public string? Reason { get; set; }
}

public class RejectFinManualAdjustInput
{
    /// <summary>必填；JSON 字段名 rejectReason。</summary>
    [StringLength(512)]
    public string? RejectReason { get; set; }
}

public class FinManualAdjustDto : EntityDto<Guid>
{
    public Guid ApplicantId { get; set; }
    public Guid? AuditorId { get; set; }
    public decimal Amount { get; set; }
    public string Currency { get; set; } = default!;
    public string DebitAccountCode { get; set; } = default!;
    public FinOwnerType DebitOwnerType { get; set; }
    public string DebitOwnerId { get; set; } = default!;
    public string CreditAccountCode { get; set; } = default!;
    public FinOwnerType CreditOwnerType { get; set; }
    public string CreditOwnerId { get; set; } = default!;
    public string? Reason { get; set; }
    public string? RejectReason { get; set; }
    public FinManualAdjustStatus Status { get; set; }
    public DateTime? SubmittedAt { get; set; }
    public DateTime? AuditedAt { get; set; }
    public Guid? VoucherId { get; set; }
    public Guid? ReversalVoucherId { get; set; }
    public DateTime CreationTime { get; set; }
}

/// <summary>演示事件（仅 BizType 以 "demo." 开头），供 A2b 培训、e2e 与对账演示。</summary>
public class PostDemoEventInput
{
    [Required, StringLength(64)]
    public string BizType { get; set; } = default!;

    [Required, StringLength(64)]
    public string BizRefId { get; set; } = default!;

    [Required, StringLength(32)]
    public string Phase { get; set; } = default!;

    [Required, StringLength(16)]
    public string Currency { get; set; } = default!;

    public decimal Amount { get; set; }

    public Guid? UserId { get; set; }

    public Guid? AgentId { get; set; }

    [StringLength(64)]
    public string? BatchKey { get; set; }

    [StringLength(512)]
    public string? Summary { get; set; }

    public Dictionary<string, string>? Dimensions { get; set; }
}

public class PostResultDto
{
    public Guid VoucherId { get; set; }
}

public class GetBalanceInput
{
    [Required]
    public string AccountCode { get; set; } = default!;
    public FinOwnerType OwnerType { get; set; } = FinOwnerType.Platform;
    public string? OwnerId { get; set; }
    public string? Bucket { get; set; }
    [Required]
    public string Currency { get; set; } = default!;
}

public class FinBalanceDto
{
    public string AccountCode { get; set; } = default!;
    public FinOwnerType OwnerType { get; set; }
    public string OwnerId { get; set; } = default!;
    public string Currency { get; set; } = default!;
    public decimal Balance { get; set; }
}

public class FinSubAccountBalanceListInput : FinListInput
{
    public string? AccountCode { get; set; }
    public string? Currency { get; set; }
    public Guid? AgentId { get; set; }
    public string? OwnerId { get; set; }
}

public class FinTrialBalanceInput
{
    public string? Currency { get; set; }
    public bool IncludeDemo { get; set; } = true;
}

public class FinTrialBalanceLineDto
{
    public string AccountCode { get; set; } = default!;
    public string AccountName { get; set; } = default!;
    public string Currency { get; set; } = default!;
    public decimal Debit { get; set; }
    public decimal Credit { get; set; }
}

public class FinTrialBalanceDto
{
    public decimal TotalDebit { get; set; }
    public decimal TotalCredit { get; set; }
    public bool IsBalanced { get; set; }
    public List<FinTrialBalanceLineDto> Lines { get; set; } = new();
}

/// <summary>
/// A2b 过账规则列表入参。
/// </summary>
public class FinPostingRuleListInput
{
    /// <summary>
    /// 默认 false：排除 Status=Deprecated（2），避免 A2b 列表出现已废弃模板（如 demo.commission）。
    /// 显式 true 时含全部状态。
    /// </summary>
    public bool IncludeDeprecated { get; set; }
}

public class FinPostingRuleDto : EntityDto<Guid>
{
    public string BizType { get; set; } = default!;
    public string Phase { get; set; } = default!;
    public int Version { get; set; }
    public FinPostingRuleStatus Status { get; set; }
    public string TemplateJson { get; set; } = default!;
    public string? Description { get; set; }
    public bool IsHost { get; set; }
}


public class FinReconcileListInput : FinListInput
{
    /// <summary>默认 false：排除 IsDemo 凭证。true 时包含演示。</summary>
    public bool IncludeDemo { get; set; }
}

public class FinReconcileRunInput
{
    public string RunType { get; set; } = "ledger-internal";
    public bool IncludeDemo { get; set; }

    /// <summary>按凭证过账时间筛，含。UTC，须带 Z（或显式偏移）。</summary>
    public DateTime? RangeStart { get; set; }

    /// <summary>按凭证过账时间筛，不含。UTC，须带 Z（或显式偏移）。</summary>
    public DateTime? RangeEnd { get; set; }
}

public class FinReconcileRunDto : EntityDto<Guid>
{
    public string RunType { get; set; } = default!;
    public bool IncludeDemo { get; set; }
    public bool HasDiff { get; set; }
    public string? DiffJson { get; set; }
    public string? ScopeJson { get; set; }
    public DateTime CreationTime { get; set; }
}

/// <summary>A6 业务单据只读（演示凭证按 BizType/BizRefId 联查）。</summary>
public class FinBizDocListInput : FinListInput
{
    /// <summary>Default false: hide vouchers whose BizType only has Deprecated posting rules (e.g. demo.commission).</summary>
    public bool IncludeDeprecated { get; set; }

    public Guid? AgentId { get; set; }
    public bool? IsDemo { get; set; }
    public string? BizType { get; set; }
}


public class FinOverviewDto
{
    public decimal TodayIncome { get; set; }
    public decimal TodayExpense { get; set; }
    public bool IsBalanced { get; set; }
    public decimal TotalDebit { get; set; }
    public decimal TotalCredit { get; set; }
    public int UnreconciledCount { get; set; }
    public decimal AvailableTotal { get; set; }
    public decimal FrozenTotal { get; set; }
    public decimal UnearnedTotal { get; set; }
    public decimal RecognizedIncomeTotal { get; set; }
}

public class FinPeriodDto : EntityDto<Guid>
{
    public int Year { get; set; }
    public int Month { get; set; }
    public FinPeriodStatus Status { get; set; }
    public bool IsCurrent { get; set; }
}
