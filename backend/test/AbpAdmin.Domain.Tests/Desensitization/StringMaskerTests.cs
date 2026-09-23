using Shouldly;
using Xunit;

namespace AbpAdmin.Desensitization;

public class StringMaskerTests
{
    [Theory]
    [InlineData(MaskKindEnum.Mobile, "13248765917", "132****5917")]
    [InlineData(MaskKindEnum.IdCard, "530321199204074611", "530321**********11")]
    [InlineData(MaskKindEnum.BankCard, "9988002866797031", "998800********31")]
    [InlineData(MaskKindEnum.FixedPhone, "01086551122", "0108*****22")]
    [InlineData(MaskKindEnum.ChineseName, "刘子豪", "刘**")]
    [InlineData(MaskKindEnum.Password, "1q2w3E*", "*******")]
    [InlineData(MaskKindEnum.Email, "example@gmail.com", "e****@gmail.com")]
    [InlineData(MaskKindEnum.Email, "a@b.cn", "a****@b.cn")]
    public void Should_Mask_BuiltIn_Kinds(MaskKindEnum kind, string origin, string expected)
    {
        StringMasker.Mask(origin, new MaskedAttribute(kind)).ShouldBe(expected);
    }

    [Fact]
    public void Should_Mask_Null_And_Empty_As_Is()
    {
        StringMasker.Mask(null, new MaskedAttribute(MaskKindEnum.Mobile)).ShouldBeNull();
        StringMasker.Mask("", new MaskedAttribute(MaskKindEnum.Mobile)).ShouldBe("");
    }

    [Theory]
    // 明文长度之和 ≥ 原文长度：整串替换（ruoyi 语义，短串不泄露更高比例原文）
    [InlineData("13", 3, 4, "**", "--")]
    [InlineData("13800138000", 0, 0, "***********", "-----------")]
    public void Should_Replace_All_When_Keep_Covers_Origin(string origin, int prefix, int suffix, string starExpected, string dashExpected)
    {
        StringMasker.Slider(origin, prefix, suffix, "*").ShouldBe(starExpected);
        StringMasker.Slider(origin, prefix, suffix, "-").ShouldBe(dashExpected);
    }

    [Fact]
    public void Should_Support_Custom_Slider()
    {
        var spec = new MaskedAttribute(MaskKindEnum.Custom) { PrefixKeep = 1, SuffixKeep = 2 };
        StringMasker.Mask("123456", spec).ShouldBe("1***56");
    }

    [Fact]
    public void Should_Support_Custom_Regex()
    {
        var spec = new MaskedAttribute(MaskKindEnum.Custom) { Pattern = "123", RegexReplacer = "******" };
        StringMasker.Mask("123456789", spec).ShouldBe("******456789");
    }

    [Fact]
    public void Email_Without_At_Falls_Back_To_Slider()
    {
        StringMasker.Email("noemail").ShouldBe("n******");
    }

    [Fact]
    public void Multi_Char_Replacer_Repeats_Per_Character()
    {
        StringMasker.Slider("abcdef", 1, 1, "--").ShouldBe("a--------f");
    }
}
