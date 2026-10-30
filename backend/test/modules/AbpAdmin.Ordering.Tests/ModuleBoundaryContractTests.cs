using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Xml.Linq;
using Shouldly;
using Volo.Abp.Data;
using Xunit;

namespace AbpAdmin.Ordering;

/// <summary>
/// 架构边界测试（计划第 2、10 节）：
/// ① Collection.Contracts 的传递闭包里不能出现 EasyAbp、Nethereum、收款实现工程、AbpAdmin.EntityFrameworkCore；
/// ② AbpAdmin.Ordering 只检查直接引用：不能直接引用收款实现、EasyAbp 或 Nethereum
///   （经 AbpAdmin.EntityFrameworkCore 传递进来的 EasyAbp 包是框架工程既有的，登记进 framework-contracts.md，
///   不作为退回条件）；
/// ③ 两个 DbContext 的 ConnectionStringName 必须都是 Default（与共享事务测试互为表里）。
/// </summary>
public class ModuleBoundaryContractTests
{
    [Fact]
    public void Collection_Contracts_Transitive_Closure_Is_Clean()
    {
        var contractsAssembly = typeof(AbpAdmin.Collection.Contracts.ICollectionIntegrationService).Assembly;
        var closure = TransitiveReferences(contractsAssembly).ToHashSet();

        // 闭包里不能出现 EasyAbp、Nethereum、收款实现工程、AbpAdmin.EntityFrameworkCore、订单工程。
        // 实现工程的判断用前缀（未来 AbpAdmin.Collection.Channels 一类兄弟工程也挡住），
        // 契约程序集自身名 "AbpAdmin.Collection.Contracts" 不在其自身引用闭包里。
        closure.ShouldNotContain(name => name.StartsWith("EasyAbp"));
        closure.ShouldNotContain(name => name.StartsWith("Nethereum"));
        closure.ShouldNotContain(name => name == "AbpAdmin.Collection" || name.StartsWith("AbpAdmin.Collection."));
        closure.ShouldNotContain(name => name.StartsWith("AbpAdmin.EntityFrameworkCore"));
        closure.ShouldNotContain(name => name.StartsWith("AbpAdmin.Ordering"));
    }

    [Fact]
    public void Ordering_Direct_References_Contain_No_Collection_Implementation()
    {
        var csproj = FindCsproj("AbpAdmin.Ordering");
        var document = XDocument.Load(csproj);

        // 直接引用 = ProjectReference + PackageReference 两类都要查（只扫工程引用会漏掉
        // 直接引的 EasyAbp 包——防这种漏网）
        var projectRefs = document.Descendants("ProjectReference")
            // csproj 的 Include 属性用反斜杠相对路径，Linux 上 Path.GetFileName 不切 \ ——
            // 先归一为 / 再取文件名（Windows/Linux 路径分隔符平台坑）
            .Select(e => Path.GetFileNameWithoutExtension(e.Attribute("Include")!.Value.Replace('\\', '/')))
            .ToList();
        var packageRefs = document.Descendants("PackageReference")
            .Select(e => e.Attribute("Include")!.Value)
            .ToList();

        // 收款实现工程（及其未来兄弟工程）即退回；Collection.Contracts 是计划要求的唯一例外
        projectRefs.ShouldNotContain("AbpAdmin.Collection");
        projectRefs.ShouldNotContain(name => name.StartsWith("AbpAdmin.Collection.") && name != "AbpAdmin.Collection.Contracts");
        packageRefs.ShouldNotContain(name => name.StartsWith("EasyAbp"), $"Ordering 的 PackageReference 里出现了 EasyAbp 包：{string.Join(",", packageRefs.Where(n => n.StartsWith("EasyAbp")))}");
        packageRefs.ShouldNotContain(name => name.StartsWith("Nethereum"));
        projectRefs.ShouldContain("AbpAdmin.EntityFrameworkCore");
        projectRefs.ShouldContain("AbpAdmin.Collection.Contracts");
    }

    [Fact]
    public void Collection_References_Only_Ledger_Contracts_Not_Implementation()
    {
        // Collection → Ledger 接线边界（Cursor 计划补锁）：只允许引用纯契约工程
        // AbpAdmin.Ledger.Integration.Contracts（ILedgerIntegrationService/ILedgerSeedProvider），
        // 不得引用 Ledger 实现（AbpAdmin.Ledger）——否则模块反向耦合总账实现。
        var repoRoot = FindRepoRoot(Path.GetDirectoryName(typeof(ModuleBoundaryContractTests).Assembly.Location)!);
        var collectionProject = Directory.GetFiles(
            Path.Combine(repoRoot, "modules", "AbpAdmin.Collection"), "AbpAdmin.Collection.csproj").Single();
        var collectionRefs = System.Xml.Linq.XDocument.Load(collectionProject)
            .Descendants("ProjectReference")
            .Select(e => (string)e.Attribute("Include")!)
            .Select(x => x.Replace('\\', '/'))
            .ToList();
        collectionRefs.ShouldContain(x => x.EndsWith("AbpAdmin.Ledger.Integration.Contracts.csproj"));
        collectionRefs.ShouldNotContain(x => x.EndsWith("AbpAdmin.Ledger/AbpAdmin.Ledger.csproj"));
    }

    [Fact]
    public void Both_Module_DbContexts_Declare_Default_Connection_String()
    {
        AssertDefaultConnectionString(typeof(Data.OrderingDbContext));
        AssertDefaultConnectionString(typeof(Collection.Data.CollectionDbContext));
    }

    // ── 内部 ─────────────────────────────────────────────────────────────────────────────

    private static void AssertDefaultConnectionString(Type dbContextType)
    {
        var attr = dbContextType.GetCustomAttribute<ConnectionStringNameAttribute>();
        attr.ShouldNotBeNull($"{dbContextType.Name} 必须显式声明 [ConnectionStringName]");
        attr.Name.ShouldBe("Default");
    }

    private static IEnumerable<string> DirectProjectReferences(string projectName)
    {
        var csproj = FindCsproj(projectName);
        var document = XDocument.Load(csproj);
        return document.Descendants("ProjectReference")
            .Select(e => Path.GetFileNameWithoutExtension(e.Attribute("Include")!.Value.Replace('\\', '/')))
            .ToList();
    }

    private static string FindCsproj(string projectName)
    {
        var repoRoot = FindRepoRoot(Path.GetDirectoryName(typeof(ModuleBoundaryContractTests).Assembly.Location)!);
        // 业务模块在 modules/、框架级在 framework/——两区都找（目录不存在则跳过），命中恰好一个才算数
        var hits = new[] { "modules", "framework" }
            .Where(seg => Directory.Exists(Path.Combine(repoRoot, seg, projectName)))
            .SelectMany(seg => Directory.GetFiles(Path.Combine(repoRoot, seg, projectName), "*.csproj"))
            .ToList();
        hits.Count.ShouldBe(1);
        return hits[0];
    }

    private static string FindRepoRoot(string startDirectory)
    {
        var dir = new DirectoryInfo(startDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "AbpAdmin.slnx")))
        {
            dir = dir.Parent!;
        }

        dir.ShouldNotBeNull("未能从测试程序集位置定位仓库根（AbpAdmin.slnx）");
        return dir.FullName;
    }

    /// <summary>
    /// 程序集传递引用闭包（按引用程序集名，BCL 除外）。测试输出目录会带上全部依赖，
    /// 用已加载程序集的 GetReferencedAssemblies 递归走一遍即可覆盖直接+传递。
    /// </summary>
    private static IEnumerable<string> TransitiveReferences(Assembly assembly)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var queue = new Queue<Assembly>();
        queue.Enqueue(assembly);

        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            foreach (var referenced in current.GetReferencedAssemblies())
            {
                if (referenced.Name == null) continue;
                if (referenced.Name.StartsWith("System", StringComparison.OrdinalIgnoreCase)
                    || referenced.Name.StartsWith("Microsoft", StringComparison.OrdinalIgnoreCase)
                    || referenced.Name == "netstandard"
                    || referenced.Name == "mscorlib")
                {
                    continue; // 只关心业务/框架程序集
                }

                if (seen.Add(referenced.Name))
                {
                    yield return referenced.Name;

                    var loaded = Array.Find(AppDomain.CurrentDomain.GetAssemblies(), a => a.GetName().Name == referenced.Name);
                    if (loaded != null)
                    {
                        queue.Enqueue(loaded);
                    }
                }
            }
        }
    }
}
