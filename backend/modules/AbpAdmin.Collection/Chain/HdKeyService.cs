using System;
using Nethereum.HdWallet;
using Volo.Abp.Security.Encryption;

namespace AbpAdmin.Collection.Chain;

/// <summary>
/// HD 密钥派生（计划第 5.6 节）：路径 m/44'/60'/0'/0/i，索引 0 不用、1 = Gas 地址、用户地址从 2 起。
/// 助记词只在调用现场解密为明文（不落日志、不入库），派生完立即离开作用域。
/// 私钥不入库——归集时按 MnemonicCipher + AddressIndex 现推。
/// </summary>
public static class HdKeyService
{
    /// <summary>Gas 地址（index 1）。</summary>
    public const int GasAddressIndex = 1;

    /// <summary>用户地址起始索引。</summary>
    public const int FirstUserAddressIndex = 2;

    /// <summary>解密后的助记词句柄：Dispose 抹除明文引用。</summary>
    public sealed class DecryptedMnemonic : IDisposable
    {
        internal string Words { get; private set; }

        public DecryptedMnemonic(string words)
        {
            Words = words;
        }

        public void Dispose()
        {
            Words = string.Empty;
        }
    }

    /// <summary>解密助记词（cipher = 版本前缀 + 密文；masterKey = Collection:MasterKey）。</summary>
    public static DecryptedMnemonic Unprotect(string cipher, string masterKey, IStringEncryptionService encryptor)
    {
        if (string.IsNullOrEmpty(cipher) || cipher.Length < 2)
        {
            throw new InvalidOperationException("Mnemonic cipher is missing or malformed.");
        }

        if (cipher[0] != '\u0001')
        {
            throw new InvalidOperationException($"Unknown mnemonic cipher version: 0x{(byte)cipher[0]:x2}.");
        }

        var words = encryptor.Decrypt(cipher[1..], masterKey);
        // 解密结果 null/空 = masterKey 不对或密文损坏：fail-closed（派生地址绝不能基于空助记词）
        if (string.IsNullOrEmpty(words))
        {
            throw new InvalidOperationException("Mnemonic decryption produced no plaintext; master key mismatch?");
        }

        return new DecryptedMnemonic(words);
    }

    /// <summary>加密助记词（版本前缀 0x01：未来换算法/轮换时按前缀分流，本期不做轮换工具）。</summary>
    public static string Protect(string words, string masterKey, IStringEncryptionService encryptor)
    {
        return '\u0001' + encryptor.Encrypt(words, masterKey);
    }

    /// <summary>
    /// 解密任意敏感明文（提现热钱包私钥等，同一版本前缀协议与口令）。
    /// 返回 null = masterKey 不对或密文损坏（调用方 fail-closed）。
    /// </summary>
    public static string? UnprotectPlain(string cipher, string masterKey, IStringEncryptionService encryptor)
    {
        if (string.IsNullOrEmpty(cipher) || cipher.Length < 2 || cipher[0] != '\u0001')
        {
            return null;
        }

        return encryptor.Decrypt(cipher[1..], masterKey);
    }

    /// <summary>加密任意敏感明文（与 Protect 同一协议；提现热钱包私钥用）。</summary>
    public static string ProtectPlain(string plaintext, string masterKey, IStringEncryptionService encryptor)
    {
        return '\u0001' + encryptor.Encrypt(plaintext, masterKey);
    }

    public static string DeriveAddress(string words, int index)
    {
        return MakeWallet(words).GetAccount(index).Address;
    }

    /// <summary>现推私钥（归集签名用）；明文不落日志，调用方用完即弃。</summary>
    public static string DerivePrivateKey(string words, int index)
    {
        return MakeWallet(words).GetAccount(index).PrivateKey;
    }

    /// <summary>已定助记词的钱包（4.29 ctor：mnemonic/password/seedPassword/IRandom）。</summary>
    private static Wallet MakeWallet(string words)
    {
        return new Wallet(words, string.Empty, string.Empty, null);
    }

    /// <summary>生成 24 词助记词（管理端一次性展示；明文不落日志）。</summary>
    public static string GenerateMnemonic()
    {
        var wallet = new Wallet(NBitcoin.Wordlist.English, NBitcoin.WordCount.TwentyFour,
            string.Empty, string.Empty, null);
        return string.Join(' ', wallet.Words);
    }
}
