using System;
using AbpAdmin.TokenGateway.Data;
using Shouldly;
using Xunit;

namespace AbpAdmin.TokenGateway;

/// <summary>
/// 设计时工厂路径哨兵（机制说明见 AbpAdmin.EntityFrameworkCore.Tests 同名测试）。
/// 本工厂为 env 优先、文件层全部 optional 的宽容形态，任何路径下都能构造出上下文，
/// 因此只有"成功"一种合格形态——若未来改为 fail-fast 起链，再对齐哨兵口径。
/// </summary>
public class DesignTimeDbContextFactoryTests
{
    [Fact]
    public void TokenGateway_Factory_Resolves_Configuration_Without_Path_Rot()
    {
        Should.NotThrow(() => new TokenGatewayDbContextFactory().CreateDbContext(Array.Empty<string>()));
    }
}
