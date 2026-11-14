using System;

namespace AbpAdmin.Collection.Contracts;

public class ChainRuntimeSettingsDto
{
    public int ChainRoundBudgetSeconds { get; set; }
    public int WithdrawRoundBudgetSeconds { get; set; }
    public Guid[] AlertRecipientUserIds { get; set; } = [];
    public bool UseLegacyExplorerFallback { get; set; } = true;
    public bool HasEncryptionKey { get; set; }
}
