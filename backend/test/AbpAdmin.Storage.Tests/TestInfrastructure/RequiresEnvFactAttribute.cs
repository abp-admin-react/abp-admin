using System;
using Xunit;

namespace AbpAdmin.Storage.Tests.TestInfrastructure;

/// <summary>
/// 环境变量门控的 Fact:发现期读指定环境变量,未配置/为空时把用例标记为 Skipped。
/// 相比"测试体内判空后 return"的写法(结果记 Passed,输出在默认日志下不可见),
/// Skip 是测试报告里的独立状态,CI 上能真正区分"跳过"与"通过"。
/// </summary>
public sealed class RequiresEnvFactAttribute : FactAttribute
{
    /// <param name="environmentVariableName">必需的环境变量名;未配置时用例显示为 Skipped。</param>
    public RequiresEnvFactAttribute(string environmentVariableName)
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(environmentVariableName)))
        {
            Skip = $"SKIPPED: 环境变量 {environmentVariableName} 未配置";
        }
    }
}
