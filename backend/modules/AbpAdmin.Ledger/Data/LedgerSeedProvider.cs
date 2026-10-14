using System.Collections.Generic;
using AbpAdmin.Ledger.Integration.Contracts;

namespace AbpAdmin.Ledger.Data;

/// <summary>科目种子：按 Code 判重，已存在不覆盖（名称/类型改动只影响新库）。</summary>
public record LedgerAccountSeed(string Code, string Name, FinAccountType Type, FinNormalSide Side);

/// <summary>过账模板种子：按 BizType+Phase+Version 判重；新版本落库后同 BizType+Phase 的旧版本置 Deprecated。</summary>
public record LedgerRuleSeed(string BizType, string Phase, string Description, string TemplateJson, int Version = 1);

/// <summary>
/// Ledger 种子扩展点。框架只注册 <see cref="FrameworkLedgerSeedProvider"/>（通用科目 + 通用模板）；
/// 业务模块接入 Ledger 时各自实现本接口并以 ITransientDependency 注册，
/// 模块未被宿主 DependsOn 就不会注册，新库也就不会出现该模块的科目和模板。
/// </summary>
public interface ILedgerSeedProvider
{
    IEnumerable<LedgerAccountSeed> Accounts { get; }

    IEnumerable<LedgerRuleSeed> Rules { get; }
}
