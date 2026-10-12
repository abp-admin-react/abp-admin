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
        // 直接引的 EasyAbp 包——评审实测抓到过这种漏网）
        var projectRefs = document.Descendants("ProjectReference")
            .Select(e => Path.GetFileNameWithoutExtension(e.Attribute("Include")!.Value))
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
            .Select(e => Path.GetFileNameWithoutExtension(e.Attribute("Include")!.Value))
            .ToList();
    }

    private static string FindCsproj(string projectName)
    {
        var repoRoot = FindRepoRoot(Path.GetDirectoryName(typeof(ModuleBoundaryContractTests).Assembly.Location)!);
        var hits = Directory.GetFiles(Path.Combine(repoRoot, "src", projectName), "*.csproj");
        hits.Length.ShouldBe(1);
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
