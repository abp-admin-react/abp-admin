using System;
using System.IO;
using System.Linq;
using AbpAdmin.ClientErrors;
using Shouldly;
using Volo.Abp;
using Xunit;

namespace AbpAdmin.Biz.Template.ClientErrors;

/// <summary>
/// A18：复制样板必须改名；未改公开码前缀会与样板冲突并启动失败。
/// </summary>
public class BizTemplateRenameAndCopyTests
{
    private static readonly string[] RenameTokens =
    [
        "BizTemplateClient",
        "EnableClientErrorSample",
        "ClientErrors",
        "BizTemplate:Public:",
    ];

    [Fact]
    public void Copying_Template_Public_Definitions_Without_Rename_Must_Fail_Catalog_Validation()
    {
        var original = new BizTemplatePublicErrorDefinitions().GetDefinitions().ToList();
        original.ShouldNotBeEmpty();
        PublicErrorCatalogStartupValidator.Validate(original);

        var copiedWithoutRename = original.Concat(original).ToList();
        var duplicateInternal = Should.Throw<AbpInitializationException>(
            () => PublicErrorCatalogStartupValidator.Validate(copiedWithoutRename));
        duplicateInternal.Message.ShouldContain(BizTemplateClientErrorCodes.ProjectUnavailable);

        var renamedInternalSamePublic = original.Select(definition => definition with
        {
            InternalCode = "CopiedModule:" + definition.InternalCode,
            ResourceType = typeof(ClientErrorResource),
        }).ToList();
        var inconsistentPublic = Should.Throw<AbpInitializationException>(
            () => PublicErrorCatalogStartupValidator.Validate(original.Concat(renamedInternalSamePublic)));
        inconsistentPublic.Message.ShouldContain(BizTemplateClientErrorCodes.ProjectUnavailablePublic);
    }

    [Fact]
    public void Biz_Module_Rename_List_Must_Cover_Client_Error_Tokens()
    {
        var repoRoot = FindGitRepoRoot();
        var rulePath = Path.Combine(
            repoRoot, ".cursor", "rules", "framework", "common", "biz-module.mdc");
        File.Exists(rulePath).ShouldBeTrue(rulePath);

        var rule = File.ReadAllText(rulePath);
        foreach (var token in RenameTokens)
        {
            rule.ShouldContain(token);
        }

        var moduleDir = Path.Combine(
            repoRoot, "backend", "modules", "biz", "AbpAdmin.Biz.Template");
        Directory.Exists(moduleDir).ShouldBeTrue(moduleDir);

        var sources = Directory.GetFiles(moduleDir, "*.cs", SearchOption.AllDirectories)
            .Concat(Directory.GetFiles(moduleDir, "*.json", SearchOption.AllDirectories))
            .Select(File.ReadAllText)
            .ToArray();
        var joined = string.Join('\n', sources);
        foreach (var token in RenameTokens)
        {
            joined.ShouldContain(token);
        }
    }

    private static string FindGitRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null)
        {
            var rule = Path.Combine(directory.FullName, ".cursor", "rules", "framework", "common", "biz-module.mdc");
            var sln = Path.Combine(directory.FullName, "backend", "AbpAdmin.slnx");
            if (File.Exists(rule) && File.Exists(sln))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException(
            "未能从测试程序集位置定位仓库根（需同时存在 backend/AbpAdmin.slnx 与 biz-module.mdc）。");
    }
}
