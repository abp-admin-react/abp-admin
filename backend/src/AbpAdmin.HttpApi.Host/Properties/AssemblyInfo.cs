using System.Runtime.CompilerServices;

// 供 AbpAdmin.HttpApi.Host.Tests 钉住 internal 契约（如 BuildDataProtectionKeyName 的
// 键名格式——env 后缀一旦丢失就是跨环境令牌互验漏洞），与 Application/Domain 等
// 项目的 AssemblyInfo 同一惯例。
[assembly: InternalsVisibleToAttribute("AbpAdmin.HttpApi.Host.Tests")]
