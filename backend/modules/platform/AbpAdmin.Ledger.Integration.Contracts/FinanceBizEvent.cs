using System;
using System.Collections.Generic;

namespace AbpAdmin.Ledger.Integration.Contracts;

/// <summary>
/// 业务 → Ledger 集成事件（定稿 §2.1）。
/// BatchKey：多笔同 Phase 批次键；null/空 → 落库 ""。
/// </summary>
public sealed record FinanceBizEvent(
    string BizType,
    string BizRefId,
    string Phase,
    string Currency,
    decimal Amount,
    Guid? UserId = null,
    Guid? AgentId = null,
    AccountRef? From = null,
    AccountRef? To = null,
    IDictionary<string, string>? Dimensions = null,
    string? Summary = null,
    Guid? OperatorId = null,
    string? BatchKey = null)
{
    /// <summary>
    /// Amount 上界：FinVoucher.Amount 列是 numeric(28,18)，整数部分只有 10 位——超过此值
    /// 的金额能通过全部校验、直到 SaveChanges 才被 PG 以 numeric overflow 打成 500。
    /// 校验（引擎 Validate / 调账 BuildLines）用本常量把拒绝提前到 400。
    /// </summary>
    public const decimal MaxAmount = 9_999_999_999m;
}
