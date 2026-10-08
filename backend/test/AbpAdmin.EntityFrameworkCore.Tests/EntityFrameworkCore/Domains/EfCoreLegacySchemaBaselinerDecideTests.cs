using System;
using AbpAdmin.EntityFrameworkCore;
using Shouldly;
using Xunit;

namespace AbpAdmin.EntityFrameworkCore.Domains;

/* 存量库打戳三态决策表（纯函数，无需数据库）。
 * 这是打戳器的心脏：决策错了要么存量库起不来（该戳不戳），要么全新库被误戳
 * 导致迁移永远不建表（不该戳乱戳——BizTemplate 若以框架的 AbpUsers 为哨兵，
 * 全新库上框架先建出 AbpUsers 就会踩中）。此处钉死四个象限。
 */
public class EfCoreLegacySchemaBaselinerDecideTests
{
    [Fact]
    public void History_Recorded_Always_Skips_Even_If_Sentinel_Missing()
    {
        EfCoreLegacySchemaBaseliner.Decide(initialRecorded: true, sentinelExists: true)
            .ShouldBe(BaselineDecision.SkipAlreadyRecorded);
        // 已记账但哨兵不在（理论不可能的组合）也必须跳过——记账优先
        EfCoreLegacySchemaBaseliner.Decide(initialRecorded: true, sentinelExists: false)
            .ShouldBe(BaselineDecision.SkipAlreadyRecorded);
    }

    [Fact]
    public void Fresh_Database_No_Sentinel_No_Stamp()
    {
        // 全新空库：未记账 + 哨兵不存在 → 正常建全部，绝不打戳
        EfCoreLegacySchemaBaseliner.Decide(initialRecorded: false, sentinelExists: false)
            .ShouldBe(BaselineDecision.FreshDatabase);
    }

    [Fact]
    public void Legacy_Database_Sentinel_Without_History_Stamps()
    {
        // 脚本时代存量库：表在（AbpUsers/BizProjects）而 History 未记账 → 打戳
        EfCoreLegacySchemaBaseliner.Decide(initialRecorded: false, sentinelExists: true)
            .ShouldBe(BaselineDecision.StampBaseline);
    }

    [Fact]
    public void Decision_Covers_All_Four_Combinations()
    {
        // 决策表完备性：2×2 输入各自有确定归宿，且三种决策都可达
        //（Skip 由前两例、Stamp 第三例、Fresh 第四例），防未来改判定漏态
        EfCoreLegacySchemaBaseliner.Decide(true, true)
            .ShouldBe(BaselineDecision.SkipAlreadyRecorded);
        EfCoreLegacySchemaBaseliner.Decide(false, true)
            .ShouldBe(BaselineDecision.StampBaseline);
        EfCoreLegacySchemaBaseliner.Decide(false, false)
            .ShouldBe(BaselineDecision.FreshDatabase);
        Enum.GetValues<BaselineDecision>().Length.ShouldBe(3);
    }
}
