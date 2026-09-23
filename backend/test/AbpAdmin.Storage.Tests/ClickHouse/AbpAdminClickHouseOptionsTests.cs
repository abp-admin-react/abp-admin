using AbpAdmin.ClickHouse;
using Xunit;

namespace AbpAdmin.Storage.Tests.ClickHouse;

/// <summary>
/// ClickHouse 选项谓词单测:IsUsable 是模块注册/健康检查注册的单一启用判定,
/// 与 ES 侧 <see cref="Elasticsearch.AbpAdminElasticsearchOptionsTests"/> 成对覆盖。
/// </summary>
public class AbpAdminClickHouseOptionsTests
{
    [Fact]
    public void IsUsable_Should_Require_Enabled_And_ConnectionString()
    {
        Assert.False(new AbpAdminClickHouseOptions { IsEnabled = true }.IsUsable);
        Assert.False(new AbpAdminClickHouseOptions { ConnectionString = "Host=fake" }.IsUsable);
        Assert.True(new AbpAdminClickHouseOptions
        {
            IsEnabled = true,
            ConnectionString = "Host=fake",
        }.IsUsable);
    }
}
