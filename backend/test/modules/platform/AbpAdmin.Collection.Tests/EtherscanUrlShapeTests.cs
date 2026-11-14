using System.Collections.Generic;
using AbpAdmin.Collection.Chain;
using Microsoft.AspNetCore.WebUtilities;
using Shouldly;
using Xunit;

namespace AbpAdmin.Collection;

/// <summary>
/// Etherscan V2 兜底 URL 组装形状锁：
/// 回退路径（裸端点无 "?"）曾拼出 "v2/api&contractaddress=…"——连查询串起点都没有；
/// key 池路径（基座以 ?apikey=KEY 结尾）缺 chainid=56 会被 V2 拒绝。
/// </summary>
public class EtherscanUrlShapeTests
{
    [Theory]
    [MemberData(nameof(Cases))]
    public void Url_Always_Has_Transfer_Query_And_Preserves_Explicit_Fields(string endpoint, string expectedPrefix)
    {
        var url = AnkrBscChainGateway.BuildEtherscanUrl(endpoint, "&contractaddress=0xc&page=1");
        url.ShouldStartWith(expectedPrefix);
        var query = QueryHelpers.ParseQuery(new System.Uri(url).Query);
        query["chainid"].ToString().ShouldBe("56");
        query["module"].ToString().ShouldBe("account");
        query["action"].ToString().ShouldBe("tokentx");
        url.ShouldEndWith("&contractaddress=0xc&page=1");
        // 任何形态都不允许出现 "api&"（丢 ? 的实锤形态）
        url.ShouldNotContain("api&");
    }

    public static IEnumerable<object[]> Cases()
    {
        // 回退路径：宿主配置裸端点（无任何查询参数）
        yield return new object[]
        {
            "https://api.etherscan.io/v2/api",
            "https://api.etherscan.io/v2/api?chainid=56",
        };
        // key 池路径：BuildEtherscanBase 截出的基座 + key
        yield return new object[]
        {
            "https://api.etherscan.io/v2/api?apikey=ABC123",
            "https://api.etherscan.io/v2/api?apikey=ABC123&chainid=56",
        };
        // 已配置的字段保持原样，只补齐缺项。
        yield return new object[]
        {
            "https://api.etherscan.io/v2/api?chainid=56",
            "https://api.etherscan.io/v2/api?chainid=56&module=account&action=tokentx&contractaddress=",
        };
        // 自带其它查询参数（无 chainid）：& 连接后补 chainid（唯一未覆盖分支）
        yield return new object[]
        {
            "https://api.etherscan.io/v2/api?foo=1",
            "https://api.etherscan.io/v2/api?foo=1&chainid=56&module=account&action=tokentx&contractaddress=",
        };
        yield return new object[]
        {
            "https://api.etherscan.io/v2/api?CHAINID=56&ACTION=tokentx&module=account&apikey=ABC123",
            "https://api.etherscan.io/v2/api?CHAINID=56&ACTION=tokentx&module=account&apikey=ABC123&contractaddress=",
        };
    }
}
