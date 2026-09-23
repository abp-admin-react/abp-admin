using System;
using AbpAdmin.Elasticsearch;
using Xunit;

namespace AbpAdmin.Storage.Tests.Elasticsearch;

/// <summary>
/// 数据流名解析纯单测。解析曾内联在 Program.cs 的 lambda 里不可测,
/// 2 段/空串配置会被静默改写后写进不存在的数据流(silent 自举 = 日志无声丢失)。
/// 现约定:不合规格式返回 null,调用方回退默认值并告警。
/// </summary>
public class AbpAdminElasticsearchOptionsTests
{
    [Theory]
    [InlineData("logs-abpadmin-default", "logs", "abpadmin", "default")]
    [InlineData("logs-my-app-prod", "logs", "my-app", "prod")] // dataset 段可含 '-'
    public void ParseLogDataStream_Should_Parse_Three_Segment_Names(
        string input, string expectedType, string expectedDataSet, string expectedNamespace)
    {
        var parsed = AbpAdminElasticsearchOptions.ParseLogDataStream(input);

        Assert.NotNull(parsed);
        Assert.Equal((expectedType, expectedDataSet, expectedNamespace), parsed!.Value);
    }

    [Theory]
    [InlineData("logs-default")]   // 2 段:曾静默回填 dataset
    [InlineData("logs")]           // 1 段
    [InlineData("")]               // 空串:曾产出 "-abpadmin-default" 非法名
    [InlineData("   ")]
    [InlineData("logs--default")]  // 空 dataset 段
    public void ParseLogDataStream_Should_Reject_Invalid_Names(string input)
    {
        Assert.Null(AbpAdminElasticsearchOptions.ParseLogDataStream(input));
    }

    [Fact]
    public void ParseLogDataStream_Should_Accept_Null_As_Invalid()
    {
        Assert.Null(AbpAdminElasticsearchOptions.ParseLogDataStream(null));
    }

    [Fact]
    public void IsUsable_Should_Require_Enabled_And_Url()
    {
        Assert.False(new AbpAdminElasticsearchOptions { IsEnabled = true }.IsUsable);
        Assert.False(new AbpAdminElasticsearchOptions { Url = "http://es:9200" }.IsUsable);
        Assert.True(new AbpAdminElasticsearchOptions { IsEnabled = true, Url = "http://es:9200" }.IsUsable);
    }
}
