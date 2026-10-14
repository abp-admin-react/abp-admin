using System;
using System.Collections.Generic;

namespace AbpAdmin.Ledger.Configuration;

public class LedgerOptions
{
    public const string SectionName = "Ledger";

    /// <summary>总开关（与设置 Finance.Ledger.Enabled 同时为 true 才放行业务 Post；manual-adjust 不受限）。</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>按 BizType 灰度（定稿 §2.3 Finance:Ledger:BizTypes:{type}）；未列出的 BizType 视为开。</summary>
    public Dictionary<string, bool> BizTypes { get; set; } = new(StringComparer.Ordinal);
}
