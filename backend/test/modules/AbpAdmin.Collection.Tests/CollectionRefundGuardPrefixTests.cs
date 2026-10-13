using System.Linq;
using System.Reflection;
using AbpAdmin.Collection.Contracts;
using Shouldly;
using Xunit;

namespace AbpAdmin.Collection;

/// <summary>
/// 退款守卫的前缀不变量（计划第 2 节）：CollectionPaymentItemTypes 的每个渠道常量都落在
/// Prefix 之下，守卫按 Prefix 拒绝——加新渠道常量（M3/M4）时自动被守卫覆盖；
/// 常量若写错前缀，这里先红。
/// </summary>
public class CollectionRefundGuardPrefixTests
{
    [Fact]
    public void All_ItemType_Constants_Use_The_Module_Prefix()
    {
        var constants = typeof(CollectionPaymentItemTypes)
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(f => f.IsLiteral && f.FieldType == typeof(string) && f.Name != nameof(CollectionPaymentItemTypes.Prefix))
            .Select(f => (Name: f.Name, Value: (string)f.GetRawConstantValue()!))
            .ToList();

        constants.ShouldNotBeEmpty("至少要有微信一个渠道常量");
        constants.ShouldAllBe(c => c.Value.StartsWith(CollectionPaymentItemTypes.Prefix, System.StringComparison.Ordinal),
            $"ItemType 常量必须以 {CollectionPaymentItemTypes.Prefix} 开头（退款守卫按前缀拒绝，写错前缀 = 该渠道的支付可被渠道退款）");
    }
}
