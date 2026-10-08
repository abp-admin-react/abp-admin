using Shouldly;
using Xunit;

namespace AbpAdmin.EntityFrameworkCore.Domains;

/* 启动预检的占位串判定（纯函数）：第一次跑没配置 PG 时，
 * 出厂占位串/空串必须被判为「未配置」，给出可行动的致命错误而不是深层 Npgsql 堆栈。
 */
public class PostgresStartupPreflightPlaceholderTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Null_Or_Whitespace_Is_Unconfigured(string? connectionString)
    {
        PostgresStartupPreflight.IsUnconfiguredPlaceholder(connectionString).ShouldBeTrue();
    }

    [Theory]
    [InlineData("Host=localhost;Port=5432;Database=abpadmin;Username=postgres;Password=CHANGE_ME")]
    [InlineData("Host=localhost;Password=change_me")] // 占位标记大小写不敏感
    [InlineData("CHANGE_ME")]
    [InlineData("Host=db.local;Username=app;Password=x" + "CHANGE_ME" + "y")] // 真实密码恰含标记：接受的假阳性（方向安全=拒绝启动要求复查）
    public void Placeholder_Marker_Anywhere_Is_Unconfigured(string connectionString)
    {
        PostgresStartupPreflight.IsUnconfiguredPlaceholder(connectionString).ShouldBeTrue();
    }

    [Fact]
    public void Real_Connection_String_Is_Configured()
    {
        PostgresStartupPreflight.IsUnconfiguredPlaceholder(
                "Host=192.168.10.250;Port=5432;Database=abp_test2;Username=abp_test2;Password=s3cret")
            .ShouldBeFalse();
    }

    [Fact]
    public void Real_Password_Containing_Lookalike_But_Not_Marker_Is_Configured()
    {
        // 边界：标记是带下划线的 CHANGE_ME——真实密码含无下划线的「changeme」子串不算占位
        PostgresStartupPreflight.IsUnconfiguredPlaceholder(
                "Host=db;Password=My changeme pass")
            .ShouldBeFalse();
    }
}
