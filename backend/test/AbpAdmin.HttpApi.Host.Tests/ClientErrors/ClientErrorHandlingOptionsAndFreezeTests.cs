using System;
using System.Linq;
using Shouldly;
using Xunit;

namespace AbpAdmin.ClientErrors;

/// <summary>
/// P1 契约测试：ClientErrorHandlingOptions 默认值与限制常量（方案 5.4）。
/// </summary>
public class ClientErrorHandlingOptionsTests
{
    /// <summary>
    /// 验证：默认值安全且符合方案。
    /// </summary>
    [Fact]
    public void Defaults_Should_Be_Secure()
    {
        var options = new ClientErrorHandlingOptions();

        options.FallbackCulture.ShouldBeNull();
        options.IncludeCorrelationId.ShouldBeTrue();
        options.MaxPublicMessageLength.ShouldBe(1000);
        options.MaxValidationErrors.ShouldBe(20);
        options.MaxPublicParameterLength.ShouldBe(256);
    }

    /// <summary>
    /// 验证：限制常量为正数且 >= 默认值。
    /// </summary>
    [Fact]
    public void Limits_Should_Be_Positive_And_GTE_Defaults()
    {
        ClientErrorHandlingLimits.AbsoluteMaxPublicMessageLength.ShouldBeGreaterThan(0);
        ClientErrorHandlingLimits.AbsoluteMaxValidationErrors.ShouldBeGreaterThan(0);
        ClientErrorHandlingLimits.AbsoluteMaxPublicParameterLength.ShouldBeGreaterThan(0);

        var options = new ClientErrorHandlingOptions();
        ClientErrorHandlingLimits.AbsoluteMaxPublicMessageLength.ShouldBeGreaterThanOrEqualTo(options.MaxPublicMessageLength);
        ClientErrorHandlingLimits.AbsoluteMaxValidationErrors.ShouldBeGreaterThanOrEqualTo(options.MaxValidationErrors);
        ClientErrorHandlingLimits.AbsoluteMaxPublicParameterLength.ShouldBeGreaterThanOrEqualTo(options.MaxPublicParameterLength);
    }

    /// <summary>
    /// 验证：支持文化列表固定。
    /// </summary>
    [Fact]
    public void Supported_Cultures_Should_Be_Fixed()
    {
        ClientErrorCultures.Supported.Count.ShouldBe(3);
        ClientErrorCultures.Supported.ShouldContain("zh-Hans");
        ClientErrorCultures.Supported.ShouldContain("zh-Hant");
        ClientErrorCultures.Supported.ShouldContain("en");
    }

    /// <summary>
    /// 验证：SectionName 正确。
    /// </summary>
    [Fact]
    public void SectionName_Should_Be_Correct()
    {
        ClientErrorHandlingOptions.SectionName.ShouldBe("ClientErrorHandling");
    }
}

/// <summary>
/// P1 契约测试：AdminErrorEndpointRegistry Freeze 语义（审查项 b）。
/// </summary>
public class AdminErrorEndpointRegistryFreezeTests
{
    /// <summary>
    /// 验证：初始状态未冻结。
    /// </summary>
    [Fact]
    public void Initial_State_Should_Not_Be_Frozen()
    {
        AdminErrorEndpointRegistry.ResetForTesting();
        AdminErrorEndpointRegistry.IsFrozen.ShouldBeFalse();
        AdminErrorEndpointRegistry.ResetForTesting();
    }

    /// <summary>
    /// 验证：Freeze 后 IsFrozen 为 true。
    /// </summary>
    [Fact]
    public void Freeze_Should_Set_IsFrozen_True()
    {
        AdminErrorEndpointRegistry.ResetForTesting();
        AdminErrorEndpointRegistry.Freeze();
        AdminErrorEndpointRegistry.IsFrozen.ShouldBeTrue();
    }

    /// <summary>
    /// 验证：Freeze 后 Register 抛出异常。
    /// </summary>
    [Fact]
    public void Register_After_Freeze_Should_Throw()
    {
        AdminErrorEndpointRegistry.ResetForTesting();
        AdminErrorEndpointRegistry.Freeze();

        var registration = new AdminErrorEndpointRegistration
        {
            ExitId = "Test.AfterFreeze()",
            DeclaringType = "Test",
            MethodName = "AfterFreeze",
            ParameterSignature = "()",
            Audience = ClientErrorAudience.Public,
            Protocol = ErrorProtocol.AbpEnvelope,
        };

        Should.Throw<InvalidOperationException>(() => AdminErrorEndpointRegistry.Register(registration));
    }

    /// <summary>
    /// 验证：Freeze 后 Clear 抛出异常。
    /// </summary>
    [Fact]
    public void Clear_After_Freeze_Should_Throw()
    {
        AdminErrorEndpointRegistry.ResetForTesting();
        AdminErrorEndpointRegistry.Freeze();

        Should.Throw<InvalidOperationException>(() => AdminErrorEndpointRegistry.Clear());
    }

    /// <summary>
    /// 验证：Freeze 后读取（GetAll/FindByExitId）仍然可用。
    /// </summary>
    [Fact]
    public void Reads_After_Freeze_Should_Work()
    {
        AdminErrorEndpointRegistry.ResetForTesting();
        var registration = new AdminErrorEndpointRegistration
        {
            ExitId = "Test.ReadAfterFreeze()",
            DeclaringType = "Test",
            MethodName = "ReadAfterFreeze",
            ParameterSignature = "()",
            Audience = ClientErrorAudience.Public,
            Protocol = ErrorProtocol.AbpEnvelope,
        };
        AdminErrorEndpointRegistry.Register(registration);
        AdminErrorEndpointRegistry.Freeze();

        var all = AdminErrorEndpointRegistry.GetAll();
        all.Count.ShouldBeGreaterThan(0);

        var found = AdminErrorEndpointRegistry.FindByExitId("Test.ReadAfterFreeze()");
        found.ShouldNotBeNull();
        found.Audience.ShouldBe(ClientErrorAudience.Public);
    }

    /// <summary>
    /// 验证：GetAll 返回快照副本，修改不影响注册表。
    /// </summary>
    [Fact]
    public void GetAll_Should_Return_Snapshot_Copy()
    {
        AdminErrorEndpointRegistry.ResetForTesting();
        AdminErrorEndpointRegistry.Register(new AdminErrorEndpointRegistration
        {
            ExitId = "Test.Snapshot()",
            DeclaringType = "Test",
            MethodName = "Snapshot",
            ParameterSignature = "()",
            Audience = ClientErrorAudience.Public,
            Protocol = ErrorProtocol.AbpEnvelope,
        });

        var snapshot1 = AdminErrorEndpointRegistry.GetAll().ToList();
        var countBefore = snapshot1.Count;

        snapshot1.Clear();

        var snapshot2 = AdminErrorEndpointRegistry.GetAll();
        snapshot2.Count.ShouldBe(countBefore, "修改快照不应影响注册表");
    }
}

/// <summary>
/// P1 契约测试：AdminErrorEndpointPublicInfo 公开视图形状（审查项 c）。
/// </summary>
public class AdminErrorEndpointPublicInfoTests
{
    /// <summary>
    /// 验证：公开视图仅包含安全字段，不暴露反射细节。
    /// </summary>
    [Fact]
    public void Public_View_Should_Exclude_Reflection_Details()
    {
        var properties = typeof(AdminErrorEndpointPublicInfo)
            .GetProperties()
            .Select(p => p.Name)
            .ToHashSet();

        properties.ShouldContain("ExitId");
        properties.ShouldContain("Audience");
        properties.ShouldContain("Protocol");
        properties.ShouldContain("HttpStatusCode");
        properties.ShouldContain("ApprovedDataFields");
        properties.ShouldContain("AcceptanceTestId");

        properties.ShouldNotContain("DeclaringType");
        properties.ShouldNotContain("MethodName");
        properties.ShouldNotContain("ParameterSignature");
        properties.ShouldNotContain("RouteTemplate");
    }

    /// <summary>
    /// 验证：GetPublicView 返回只包含公开信息的集合。
    /// </summary>
    [Fact]
    public void GetPublicView_Should_Return_Public_Only()
    {
        AdminErrorEndpointRegistry.ResetForTesting();
        AdminErrorEndpointRegistry.Register(new AdminErrorEndpointRegistration
        {
            ExitId = "Test.PublicView()",
            DeclaringType = "TestController",
            MethodName = "PublicView",
            ParameterSignature = "()",
            RouteTemplate = "api/test/{id}",
            Audience = ClientErrorAudience.AdminCompatibility,
            Protocol = ErrorProtocol.AbpEnvelope,
            ApprovedDataFields = ["id"],
        });

        var publicView = AdminErrorEndpointRegistry.GetPublicView();
        publicView.Count.ShouldBeGreaterThan(0);

        var first = publicView.First();
        first.ExitId.ShouldNotBeNullOrEmpty();
        first.Audience.ShouldBeOneOf(
            ClientErrorAudience.Public,
            ClientErrorAudience.Shared,
            ClientErrorAudience.AdminCompatibility);

        first.ApprovedDataFields.ShouldNotBeNull();
    }
}
