using System;
using System.Collections.Generic;

namespace AbpAdmin.Ledger.Configuration;

/// <summary>
/// Ledger 模块配置节（Configuration/Ledger.json + Host 同名覆盖）。
/// 环境变量：Ledger__Enabled、Ledger__BizTypes__{bizType}=false。
/// </summary>
public class LedgerOptions
{
    public const string SectionName = "Ledger";

    /// <summary>
    /// 总开关。与设置 <c>Finance.Ledger.Enabled</c> 同时为 true 才放行业务 Post；
    /// <c>manual-adjust</c> 不受本开关与设置限制（A3 调账页仍可用）。
    /// false 或设置关闭 → <c>Finance:LedgerDisabled</c>（HTTP 403）。默认 true。
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// 按 BizType 灰度。键=BizType（如 demo.credit），值=false 则该类型 Post 拒绝。
    /// 未列出的 BizType 视为开。对应定稿 §2.3 / 配置路径 Ledger:BizTypes:{type}。
    /// </summary>
    public Dictionary<string, bool> BizTypes { get; set; } = new(StringComparer.Ordinal);
}
