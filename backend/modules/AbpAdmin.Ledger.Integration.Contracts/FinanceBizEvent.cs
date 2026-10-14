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
    string? BatchKey = null);
