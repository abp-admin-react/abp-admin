using System;
using AbpAdmin.OperationLogs;
using AbpAdmin.Posts;
using Shouldly;
using Xunit;

namespace AbpAdmin.OperationLogs;

/* 复现浏览器实测 bug：匿名快照 old 有值（{{old.code}} 能渲染），但 _diff 输出旧值 (空)。 */
public class OperationLogDifferDiagnosisTests
{
    private class Snapshot
    {
        public string Name { get; set; } = "旧名";
        public string Code { get; set; } = "BROWSER1";
        public int SortOrder { get; set; } = 1;
    }

    [Fact]
    public void Diff_Anonymous_Old_With_Dto_New_Should_Show_Old_Values()
    {
        var old = new { Name = "旧名", Code = "BROWSER1", SortOrder = 1, Status = 0, Remark = (string?)null };
        var @new = new UpdatePostDto { Name = "新名", Code = "BROWSER1", SortOrder = 2, Status = 0 };

        var result = OperationLogDiffer.Diff(old, @new);

        result!.ShouldContain("【岗位名称】旧名 → 新名");
        result!.ShouldContain("【显示顺序】1 → 2");
        result!.ShouldNotContain("(空)");
    }

    [Fact]
    public void Diff_Named_Snapshot_Should_Also_Work()
    {
        var old = new Snapshot();
        var @new = new UpdatePostDto { Name = "新名", Code = "BROWSER1", SortOrder = 1, Status = 0 };

        OperationLogDiffer.Diff(old, @new)!.ShouldContain("旧名 → 新名");
    }
}
