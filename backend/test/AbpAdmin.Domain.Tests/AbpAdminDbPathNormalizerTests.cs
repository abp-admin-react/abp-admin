using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.Extensions.Configuration;
using Shouldly;
using Xunit;

namespace AbpAdmin;

/* AbpAdminDbPathNormalizer（SQLite 相对路径连接串的 cwd 归一）纯函数单测。
 *
 * 为什么不用临时目录放假 AbpAdmin.slnx：FindRepoRoot 的起点是 AppContext.BaseDirectory
 * （测试进程的程序集目录，即仓库 bin 目录），与进程 cwd / 临时目录无关——测试 bin 目录
 * 向上必然先碰到仓库根的真 slnx，临时目录里的假锚永远影响不到查找结果。
 * 因此「找不到仓库锚 → false」的分支在本仓库的测试环境里不可测（发布布局才会走到），
 * 这里锚定的是「命中相对形态时改写正确」与「不命中时绝不动配置」两件事。
 */
public class AbpAdminDbPathNormalizerTests
{
    private static IConfiguration BuildConfig(string? connectionString)
    {
        return new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Default"] = connectionString
            })
            .Build();
    }

    [Fact]
    public void Should_Rewrite_Relative_Form_To_Anchored_Absolute_Path()
    {
        var config = BuildConfig("Data Source=../../AbpAdmin.db;Cache=Shared;Default Timeout=30");

        var result = AbpAdminDbPathNormalizer.TryNormalize(config, out var appliedPath);

        result.ShouldBeTrue();
        appliedPath.ShouldNotBeNull();

        // 锚定结果必须是从程序集位置向上找到的仓库根下的库文件：
        // 不硬编码盘符（不同机器仓库位置不同），改为断言锚目录里真的有 AbpAdmin.slnx
        Path.IsPathRooted(appliedPath!).ShouldBeTrue("改写结果必须是绝对路径，行为与启动 cwd 无关");
        appliedPath!.Contains('\\').ShouldBeFalse(); // 库路径统一用 / 分隔（SQLite 连接串跨平台形态）
        appliedPath!.Contains("../..").ShouldBeFalse();
        appliedPath.ShouldEndWith("AbpAdmin.db");
        File.Exists(Path.Combine(
            Path.GetDirectoryName(appliedPath)!, "AbpAdmin.slnx")).ShouldBeTrue(
            "锚定目录必须是真正持有 AbpAdmin.slnx 的仓库根");

        var rewritten = config["ConnectionStrings:Default"];
        rewritten.ShouldNotBeNull();
        rewritten!.Contains("../..").ShouldBeFalse(); // 相对形态必须被整体改写掉
        rewritten.ShouldStartWith($"Data Source={appliedPath}");
        // 整段替换只动 Data Source 一段：连接串的其余参数（尾参）原样保留
        rewritten!.ShouldEndWith(";Cache=Shared;Default Timeout=30");
    }

    [Fact]
    public void Should_Not_Touch_Non_Relative_Connection_Strings()
    {
        // 发布布局的常见形态：已经是绝对路径（或不带该相对前缀）→ 不命中，配置原样
        var absolute = BuildConfig("Data Source=C:/deploy/AbpAdmin.db;Cache=Shared");
        AbpAdminDbPathNormalizer.TryNormalize(absolute, out var appliedAbsolute).ShouldBeFalse();
        appliedAbsolute.ShouldBeNull();
        absolute["ConnectionStrings:Default"].ShouldBe("Data Source=C:/deploy/AbpAdmin.db;Cache=Shared");

        // 形态必须整体匹配：只改了文件名 / 层级数不同都不算命中
        var nearMiss = BuildConfig("Data Source=../AbpAdmin.db");
        AbpAdminDbPathNormalizer.TryNormalize(nearMiss, out var appliedNearMiss).ShouldBeFalse();
        appliedNearMiss.ShouldBeNull();
        nearMiss["ConnectionStrings:Default"].ShouldBe("Data Source=../AbpAdmin.db");
    }

    [Fact]
    public void Should_Return_False_When_Connection_String_Missing()
    {
        var config = BuildConfig(null);

        AbpAdminDbPathNormalizer.TryNormalize(config, out var appliedPath).ShouldBeFalse();
        appliedPath.ShouldBeNull();
        config["ConnectionStrings:Default"].ShouldBeNull();
    }
}
