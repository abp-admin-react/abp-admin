using System;
using System.ComponentModel.DataAnnotations;
using Volo.Abp.Application.Dtos;

namespace AbpAdmin.Collection.Contracts;

public enum CollectionBalanceAdjustmentStatus
{
    Pending = 0,
    Approved = 1,
    Rejected = 2,
}

public class CreateCollectionBalanceAdjustmentInput
{
    public Guid RequestId { get; set; }
    public Guid UserId { get; set; }
    public CollectionCurrency Currency { get; set; }

    [Required]
    [StringLength(64)]
    public string Amount { get; set; } = default!;

    [Required]
    [StringLength(512)]
    public string Reason { get; set; } = default!;
}

public class RejectCollectionBalanceAdjustmentInput
{
    [Required]
    [StringLength(512)]
    public string Reason { get; set; } = default!;
}

public class CollectionBalanceAdjustmentListInput : PagedResultRequestDto
{
    public Guid? UserId { get; set; }

    [EnumDataType(typeof(CollectionBalanceAdjustmentStatus))]
    public CollectionBalanceAdjustmentStatus? Status { get; set; }

    [StringLength(128)]
    public string? Filter { get; set; }
}

public class CollectionBalanceAdjustmentDto : EntityDto<Guid>
{
    public Guid RequestId { get; set; }
    public Guid UserId { get; set; }
    public CollectionCurrency Currency { get; set; }
    public string Amount { get; set; } = default!;
    public string Reason { get; set; } = default!;
    public CollectionBalanceAdjustmentStatus Status { get; set; }
    public Guid ApplicantId { get; set; }
    public Guid? ReviewerId { get; set; }
    public DateTime? ReviewedAt { get; set; }
    public string? ReviewNote { get; set; }
    public Guid? BalanceTxnId { get; set; }
    public Guid? LedgerVoucherId { get; set; }
    public string? BalanceAfter { get; set; }
    public DateTime CreationTime { get; set; }
}

public static class CollectionBalanceAdjustmentErrorCodes
{
    public const string InvalidInput = CollectionErrorCodes.Namespace + ":BalanceAdjustmentInvalidInput";
    public const string RequestConflict = CollectionErrorCodes.Namespace + ":BalanceAdjustmentRequestConflict";
    public const string InvalidStatus = CollectionErrorCodes.Namespace + ":BalanceAdjustmentInvalidStatus";
    public const string UserNotFound = CollectionErrorCodes.Namespace + ":BalanceAdjustmentUserNotFound";
    public const string SelfApprovalDenied = CollectionErrorCodes.Namespace + ":BalanceAdjustmentSelfApprovalDenied";
}
