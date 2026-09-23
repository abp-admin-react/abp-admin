using System;
using System.Collections.Generic;
using System.ComponentModel;
using AbpAdmin.DataDictionaries;
using Shouldly;
using Volo.Abp;
using Xunit;

namespace AbpAdmin.Domain.Tests.DataDictionaries
{
    /// <summary>
    /// EnumDictionarySyncPlanBuilder 的纯逻辑单测（T3.4 第 11 步）。
    /// 不依赖数据库：Warning、冲突抛异常、Description 回落、截断、TagType 校验都在这层验。
    /// </summary>
    public class EnumDictionarySyncPlanBuilderTests
    {
        [Fact]
        public void Should_Skip_Public_Enum_Without_Enum_Suffix_With_Warning()
        {
            var warnings = new List<string>();

            var plans = EnumDictionarySyncPlanBuilder.BuildPlans(
                [typeof(PlainState)], warnings.Add);

            plans.ShouldBeEmpty();
            warnings.Count.ShouldBe(1);
            // Warning 必须带类型全名（验收标准）
            warnings[0].ShouldContain(typeof(PlainState).FullName!);
        }

        [Fact]
        public void Should_Throw_On_Duplicate_Dictionary_Code()
        {
            // 两个不同命名空间、短名相同（去后缀后字典编码相同）的枚举必须直接抛异常，不静默覆盖
            Should.Throw<AbpException>(() =>
                EnumDictionarySyncPlanBuilder.BuildPlans(
                    [typeof(DuplicateEnum), typeof(Ns2.DuplicateEnum)], _ => { }));
        }

        [Fact]
        public void Should_Build_Plan_From_Description_And_DictionaryTag()
        {
            var plans = EnumDictionarySyncPlanBuilder.BuildPlans([typeof(SampleStateEnum)], _ => { });

            var plan = plans.ShouldHaveSingleItem();
            plan.DictionaryCode.ShouldBe("SampleState");
            plan.DictionaryDisplayText.ShouldBe("示例状态");
            plan.Items.Count.ShouldBe(3);

            // Code 是数值字符串、Description 记成员名、Order 是声明顺序（验收标准）
            plan.Items[0].Code.ShouldBe("0");
            plan.Items[0].DisplayText.ShouldBe("待处理");
            plan.Items[0].Description.ShouldBe("Pending");
            plan.Items[0].TagType.ShouldBe(DataDictionaryTagTypes.Processing);
            plan.Items[0].Order.ShouldBe(0);

            plan.Items[1].Code.ShouldBe("1");
            plan.Items[1].Order.ShouldBe(1);

            plan.Items[2].Code.ShouldBe("10"); // 数值字符串，不是成员名
        }

        [Fact]
        public void Should_Fallback_To_Member_Name_When_Description_Missing()
        {
            var warnings = new List<string>();

            var plans = EnumDictionarySyncPlanBuilder.BuildPlans([typeof(NoDescriptionEnum)], warnings.Add);

            var plan = plans.ShouldHaveSingleItem();
            plan.Items[0].DisplayText.ShouldBe("Alpha");
            warnings.ShouldContain(w => w.Contains(nameof(NoDescriptionEnum.Alpha)));
        }

        [Fact]
        public void Should_Truncate_Long_Description_With_Warning()
        {
            var warnings = new List<string>();

            var plans = EnumDictionarySyncPlanBuilder.BuildPlans([typeof(LongTextEnum)], warnings.Add);

            plans[0].Items[0].DisplayText.Length.ShouldBe(64); // DataDictionaryItemConsts.MaxDisplayTextLength
            warnings.ShouldContain(w => w.Contains("截断"));
        }

        [Fact]
        public void Should_Ignore_Invalid_TagType_With_Warning()
        {
            var warnings = new List<string>();

            var plans = EnumDictionarySyncPlanBuilder.BuildPlans([typeof(BadTagEnum)], warnings.Add);

            plans[0].Items[0].TagType.ShouldBeNull();
            warnings.ShouldContain(w => w.Contains("DictionaryTag"));
        }

        [Fact]
        public void Stable_Id_Should_Differ_Per_Tenant_And_Be_Deterministic()
        {
            var tenantId = Guid.NewGuid();

            // 同一（类型, 租户）多次同步 Id 必须一致（幂等 upsert 的前提）
            EnumDictionarySyncPlanBuilder.CreateStableDictionaryId(null, "A.B")
                .ShouldBe(EnumDictionarySyncPlanBuilder.CreateStableDictionaryId(null, "A.B"));
            EnumDictionarySyncPlanBuilder.CreateStableDictionaryId(tenantId, "A.B")
                .ShouldBe(EnumDictionarySyncPlanBuilder.CreateStableDictionaryId(tenantId, "A.B"));

            // host 与租户不能撞主键（字典逐租户各写一份）
            EnumDictionarySyncPlanBuilder.CreateStableDictionaryId(null, "A.B")
                .ShouldNotBe(EnumDictionarySyncPlanBuilder.CreateStableDictionaryId(tenantId, "A.B"));
        }
    }

    public enum PlainState
    {
        On = 0,
        Off = 1
    }

    [Description("示例状态")]
    public enum SampleStateEnum : byte
    {
        [Description("待处理")]
        [DictionaryTag(DataDictionaryTagTypes.Processing)]
        Pending = 0,

        [Description("完成")]
        [DictionaryTag(DataDictionaryTagTypes.Success)]
        Done = 1,

        [Description("其他")]
        Other = 10
    }

    public enum NoDescriptionEnum
    {
        Alpha = 0
    }

    public enum LongTextEnum
    {
        [Description("这是一个非常非常非常非常非常非常非常非常非常非常非常非常非常非常非常非常非常非常非常非常非常非常非常非常非常非常非常非常非常非常非常非常长的描述文本")]
        Long = 0
    }

    public enum BadTagEnum
    {
        [Description("坏标签")]
        [DictionaryTag("not-a-real-tag-type")]
        Bad = 0
    }

    // 与 Ns2.DuplicateEnum 构成跨命名空间短名冲突（去后缀后字典编码都是 Duplicate）
    public enum DuplicateEnum
    {
        B = 0
    }

    namespace Ns2
    {
        public enum DuplicateEnum
        {
            A = 0
        }
    }
}
