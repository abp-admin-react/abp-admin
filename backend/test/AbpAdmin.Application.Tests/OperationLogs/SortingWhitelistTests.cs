using System.Collections.Generic;
using AbpAdmin;
using Shouldly;
using Xunit;

namespace AbpAdmin.OperationLogs;

/* SortingWhitelist 单测：合法形态放行、注入载荷与畸形串全部拒绝。
 * 覆盖浏览器实测的注入样本（分号拼接 DROP、Dynamic LINQ 构造表达式）。 */
public class SortingWhitelistTests
{
    private static readonly string[] Fields = ["ExecutionTime", "Duration", "Type"];

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("ExecutionTime")]
    [InlineData("executionTime DESC")]
    [InlineData("ExecutionTime desc, Type")]
    [InlineData("Duration asc , Type")]
    public void Should_Accept_Valid_Sorting(string? sorting)
    {
        SortingWhitelist.IsValid(sorting, Fields).ShouldBeTrue();
    }

    [Theory]
    [InlineData("ExecutionTime desc; DROP TABLE AbpUsers")]   // SQL 注入拼接
    [InlineData("new Func(() => 1)")]                          // Dynamic LINQ 表达式注入
    [InlineData("ExecutionTime;Duration")]                     // 分号分隔（非本接口语法）
    [InlineData("Password")]                                   // 白名单外字段
    [InlineData("ExecutionTime descending")]                   // 非法方向词
    [InlineData("ExecutionTime desc,,Type")]                  // 空段
    [InlineData("ExecutionTime desc,")]                        // 尾随逗号
    [InlineData(" ExecutionTime desc;--")]                     // 注释尾注
    public void Should_Reject_Invalid_Sorting(string sorting)
    {
        SortingWhitelist.IsValid(sorting, Fields).ShouldBeFalse();
    }
}
