using System;
using System.Collections.Generic;
using System.Linq;
using AbpAdmin.ClientErrors;
using AbpAdmin.Domain.ClientErrors;
using Shouldly;
using Xunit;

namespace AbpAdmin.ClientErrors;

public class PublicErrorDefinitionRegistryTests
{
    private static readonly Type ClientErrorResourceType = typeof(ClientErrorResource);

    /// <summary>
    /// 验证：Registry 构造时注册基础定义。
    /// 行为：初始包含 8 个基础错误定义。
    /// </summary>
    [Fact]
    public void Constructor_Should_Register_Base_Definitions()
    {
        var registry = new PublicErrorDefinitionRegistry();
        var definitions = registry.GetDefinitions();

        definitions.Count.ShouldBe(8);
    }

    /// <summary>
    /// 验证：基础定义包含正确的 InternalCode。
    /// 行为：每个基础定义有唯一的 InternalCode。
    /// </summary>
    [Fact]
    public void Base_Definitions_Should_Have_Unique_InternalCodes()
    {
        var registry = new PublicErrorDefinitionRegistry();
        var definitions = registry.GetDefinitions();

        var internalCodes = new HashSet<string>();
        foreach (var def in definitions)
        {
            internalCodes.Add(def.InternalCode).ShouldBeTrue($"Duplicate internal code: {def.InternalCode}");
        }
    }

    /// <summary>
    /// 验证：基础定义映射到正确的 PublicCode。
    /// 行为：Client:AuthenticationRequired → AuthenticationRequired
    /// </summary>
    [Fact]
    public void Base_Definition_Should_Map_AuthenticationRequired()
    {
        var registry = new PublicErrorDefinitionRegistry();
        var definitions = registry.GetDefinitions();

        var authDef = definitions.First(d => d.InternalCode == "Client:AuthenticationRequired");
        authDef.PublicCode.ShouldBe(ClientErrorCodes.AuthenticationRequired);
        authDef.Kind.ShouldBe(PublicErrorKind.Authentication);
    }

    /// <summary>
    /// 验证：Register 方法可添加新定义。
    /// 行为：新定义可被 GetDefinitions 返回。
    /// </summary>
    [Fact]
    public void Register_Should_Add_New_Definition()
    {
        var registry = new PublicErrorDefinitionRegistry();
        var initialCount = registry.GetDefinitions().Count;

        var newDef = new PublicErrorDefinition
        {
            InternalCode = "Custom:MyError",
            PublicCode = "MyError",
            ResourceType = ClientErrorResourceType,
            MessageKey = "MyError",
            Kind = PublicErrorKind.Business,
        };

        registry.Register(newDef);

        registry.GetDefinitions().Count.ShouldBe(initialCount + 1);
        registry.GetDefinitions().ShouldContain(d => d.InternalCode == "Custom:MyError");
    }

    /// <summary>
    /// 验证：Register 拒绝重复的 InternalCode。
    /// 行为：抛出 InvalidOperationException。
    /// </summary>
    [Fact]
    public void Register_Should_Reject_Duplicate_InternalCode()
    {
        var registry = new PublicErrorDefinitionRegistry();

        var duplicateDef = new PublicErrorDefinition
        {
            InternalCode = "Client:AuthenticationRequired",
            PublicCode = "Duplicate",
            ResourceType = ClientErrorResourceType,
            MessageKey = "Duplicate",
            Kind = PublicErrorKind.Business,
        };

        Should.Throw<InvalidOperationException>(() => registry.Register(duplicateDef));
    }

    /// <summary>
    /// 验证：Register 拒绝空的 InternalCode。
    /// 行为：抛出 ArgumentException。
    /// </summary>
    [Fact]
    public void Register_Should_Reject_Empty_InternalCode()
    {
        var registry = new PublicErrorDefinitionRegistry();

        var emptyDef = new PublicErrorDefinition
        {
            InternalCode = "",
            PublicCode = "Test",
            ResourceType = ClientErrorResourceType,
            MessageKey = "Test",
            Kind = PublicErrorKind.Business,
        };

        Should.Throw<ArgumentException>(() => registry.Register(emptyDef));
    }

    /// <summary>
    /// 验证：IPublicErrorDefinitionProvider 接口实现。
    /// 行为：GetDefinitions 返回相同集合。
    /// </summary>
    [Fact]
    public void Provider_Interface_Should_Return_Definitions()
    {
        var registry = new PublicErrorDefinitionRegistry();
        var provider = (IPublicErrorDefinitionProvider)registry;

        var providerDefs = provider.GetDefinitions();
        providerDefs.ShouldNotBeNull();
        providerDefs.ToList().Count.ShouldBe(8);
    }
}

public class PublicErrorCatalogTests
{
    private static readonly Type ClientErrorResourceType = typeof(ClientErrorResource);

    /// <summary>
    /// 验证：Catalog 使用提供的定义集合。
    /// 行为：Definitions 属性返回所有定义。
    /// </summary>
    [Fact]
    public void Constructor_Should_Store_Definitions()
    {
        var definitions = new List<PublicErrorDefinition>
        {
            new() { InternalCode = "Test:1", PublicCode = "Test1", ResourceType = ClientErrorResourceType, MessageKey = "T1", Kind = PublicErrorKind.Business },
            new() { InternalCode = "Test:2", PublicCode = "Test2", ResourceType = ClientErrorResourceType, MessageKey = "T2", Kind = PublicErrorKind.Server },
        };

        var catalog = new PublicErrorCatalog(definitions);

        catalog.Definitions.Count.ShouldBe(2);
    }

    /// <summary>
    /// 验证：Catalog 拒绝 null 定义集合。
    /// 行为：抛出 ArgumentNullException。
    /// </summary>
    [Fact]
    public void Constructor_Should_Reject_Null_Definitions()
    {
        Should.Throw<ArgumentNullException>(() => new PublicErrorCatalog(null!));
    }

    /// <summary>
    /// 验证：FindByInternalCode 找到存在的定义。
    /// 行为：返回匹配的 PublicErrorDefinition。
    /// </summary>
    [Fact]
    public void FindByInternalCode_Should_Find_Existing()
    {
        var definitions = new List<PublicErrorDefinition>
        {
            new() { InternalCode = "Test:FindMe", PublicCode = "Found", ResourceType = ClientErrorResourceType, MessageKey = "Find", Kind = PublicErrorKind.Business },
        };

        var catalog = new PublicErrorCatalog(definitions);
        var result = catalog.FindByInternalCode("Test:FindMe");

        result.ShouldNotBeNull();
        result.PublicCode.ShouldBe("Found");
    }

    /// <summary>
    /// 验证：FindByInternalCode 对空字符串返回 null。
    /// 行为：不抛出异常，返回 null。
    /// </summary>
    [Fact]
    public void FindByInternalCode_Should_Return_Null_For_Empty()
    {
        var catalog = new PublicErrorCatalog(new List<PublicErrorDefinition>());

        var result = catalog.FindByInternalCode("");

        result.ShouldBeNull();
    }

    /// <summary>
    /// 验证：FindByInternalCode 使用 Ordinal 比较。
    /// 行为：大小写敏感匹配。
    /// </summary>
    [Fact]
    public void FindByInternalCode_Should_Use_Ordinal_Comparison()
    {
        var definitions = new List<PublicErrorDefinition>
        {
            new() { InternalCode = "Test:Case", PublicCode = "Found", ResourceType = ClientErrorResourceType, MessageKey = "Case", Kind = PublicErrorKind.Business },
        };

        var catalog = new PublicErrorCatalog(definitions);

        catalog.FindByInternalCode("Test:case").ShouldBeNull();
        catalog.FindByInternalCode("Test:Case").ShouldNotBeNull();
    }

    /// <summary>
    /// 验证：FindByPublicCode 找到存在的定义。
    /// 行为：返回匹配的 PublicErrorDefinition。
    /// </summary>
    [Fact]
    public void FindByPublicCode_Should_Find_Existing()
    {
        var definitions = new List<PublicErrorDefinition>
        {
            new() { InternalCode = "Test:Code", PublicCode = "PublicCode", ResourceType = ClientErrorResourceType, MessageKey = "Code", Kind = PublicErrorKind.Business },
        };

        var catalog = new PublicErrorCatalog(definitions);
        var result = catalog.FindByPublicCode("PublicCode");

        result.ShouldNotBeNull();
        result.InternalCode.ShouldBe("Test:Code");
    }

    /// <summary>
    /// 验证：FindByPublicCode 不存在时返回 null。
    /// 行为：返回 null，不抛出异常。
    /// </summary>
    [Fact]
    public void FindByPublicCode_Should_Return_Null_For_NonExisting()
    {
        var catalog = new PublicErrorCatalog(new List<PublicErrorDefinition>());

        var result = catalog.FindByPublicCode("NonExistent");

        result.ShouldBeNull();
    }
}

public class ClientErrorProjectionCoreTests
{
    private static readonly Type ClientErrorResourceType = typeof(ClientErrorResource);
    private readonly PublicErrorCatalog _catalog;

    public ClientErrorProjectionCoreTests()
    {
        var definitions = new List<PublicErrorDefinition>
        {
            new() { InternalCode = "Test:Auth", PublicCode = ClientErrorCodes.AuthenticationRequired, ResourceType = ClientErrorResourceType, MessageKey = "AuthenticationRequired", Kind = PublicErrorKind.Authentication },
            new() { InternalCode = "Test:Forbidden", PublicCode = ClientErrorCodes.Forbidden, ResourceType = ClientErrorResourceType, MessageKey = "Forbidden", Kind = PublicErrorKind.Forbidden },
            new() { InternalCode = "Test:RateLimited", PublicCode = ClientErrorCodes.RateLimited, ResourceType = ClientErrorResourceType, MessageKey = "RateLimited", Kind = PublicErrorKind.RateLimited },
        };
        _catalog = new PublicErrorCatalog(definitions);
    }

    /// <summary>
    /// 验证：Project 投影已知 InternalCode。
    /// 行为：返回正确的 PublicCode 和 Kind。
    /// </summary>
    [Fact]
    public void Project_Should_Map_Known_InternalCode()
    {
        var projection = new ClientErrorProjectionCore(_catalog);

        var input = new PublicErrorProjectionInput
        {
            InternalCode = "Test:Auth",
            CorrelationId = "corr-123",
        };

        var result = projection.Project(input);

        result.PublicCode.ShouldBe(ClientErrorCodes.AuthenticationRequired);
        result.Kind.ShouldBe(PublicErrorKind.Authentication);
        result.CorrelationId.ShouldBe("corr-123");
    }

    /// <summary>
    /// 验证：Project 未知 InternalCode 返回 OperationFailed。
    /// 行为：PublicCode 为 OperationFailed，Kind 为 Business。
    /// </summary>
    [Fact]
    public void Project_Should_Return_OperationFailed_For_Unknown()
    {
        var projection = new ClientErrorProjectionCore(_catalog);

        var input = new PublicErrorProjectionInput
        {
            InternalCode = "Unknown:Error",
        };

        var result = projection.Project(input);

        result.PublicCode.ShouldBe(ClientErrorCodes.OperationFailed);
        result.Kind.ShouldBe(PublicErrorKind.Business);
    }

    /// <summary>
    /// 验证：无 InternalCode 返回 Unexpected。
    /// 行为：PublicCode 为 Unexpected，Kind 为 Server。
    /// </summary>
    [Fact]
    public void Project_Should_Return_Unexpected_When_No_Code()
    {
        var projection = new ClientErrorProjectionCore(_catalog);

        var input = new PublicErrorProjectionInput();

        var result = projection.Project(input);

        result.PublicCode.ShouldBe(ClientErrorCodes.Unexpected);
        result.Kind.ShouldBe(PublicErrorKind.Server);
    }

    /// <summary>
    /// 验证：Project 返回本地化回退消息。
    /// 行为：消息符合 FallbackMessage 映射。
    /// </summary>
    [Fact]
    public void Project_Should_Return_Fallback_Message()
    {
        var projection = new ClientErrorProjectionCore(_catalog);

        var input = new PublicErrorProjectionInput { InternalCode = "Test:Auth" };

        var result = projection.Project(input);

        result.Message.ShouldBe("Authentication required. Please log in.");
    }

    /// <summary>
    /// 验证：Project 拒绝 null 输入。
    /// 行为：抛出 ArgumentNullException。
    /// </summary>
    [Fact]
    public void Project_Should_Reject_Null_Input()
    {
        var projection = new ClientErrorProjectionCore(_catalog);

        Should.Throw<ArgumentNullException>(() => projection.Project(null!));
    }

    /// <summary>
    /// 验证：Project 传递 RetryAfterSeconds。
    /// 行为：结果包含相同的值。
    /// </summary>
    [Fact]
    public void Project_Should_Pass_Through_RetryAfterSeconds()
    {
        var projection = new ClientErrorProjectionCore(_catalog);

        var input = new PublicErrorProjectionInput
        {
            InternalCode = "Test:RateLimited",
            RetryAfterSeconds = 60,
        };

        var result = projection.Project(input);

        result.RetryAfterSeconds.ShouldBe(60);
    }

    /// <summary>
    /// 验证：Project 过滤 ExceptionData。
    /// 行为：未批准字段被丢弃。
    /// </summary>
    [Fact]
    public void Project_Should_Filter_ExceptionData()
    {
        var projection = new ClientErrorProjectionCore(_catalog);

        var input = new PublicErrorProjectionInput
        {
            InternalCode = "Test:Auth",
            ExceptionData = new Dictionary<string, object?>
            {
                ["correlationId"] = "corr-123",
                ["secretData"] = "should-be-filtered",
            },
        };

        var result = projection.Project(input);

        result.Data.ShouldNotBeNull();
        result.Data.ContainsKey("secretData").ShouldBeFalse();
    }
}

public class BusinessDataFilterTests
{
    /// <summary>
    /// 验证：FilterDefault 仅返回 correlationId。
    /// 行为：其他字段被过滤。
    /// </summary>
    [Fact]
    public void FilterDefault_Should_Only_Return_CorrelationId()
    {
        var source = new Dictionary<string, object?>
        {
            ["correlationId"] = "corr-123",
            ["secretData"] = "filtered",
            ["other"] = "data",
        };

        var result = BusinessDataFilter.FilterDefault(source);

        result.Count.ShouldBe(1);
        result["correlationId"].ShouldBe("corr-123");
    }

    /// <summary>
    /// 验证：FilterDefault 处理 null 源。
    /// 行为：返回空字典。
    /// </summary>
    [Fact]
    public void FilterDefault_Should_Handle_Null_Source()
    {
        var result = BusinessDataFilter.FilterDefault(null);

        result.Count.ShouldBe(0);
    }

    /// <summary>
    /// 验证：FilterWithApprovedFields 按指定字段过滤。
    /// 行为：只保留批准的字段。
    /// </summary>
    [Fact]
    public void FilterWithApprovedFields_Should_Filter_By_Approved_Set()
    {
        var approvedFields = new ApprovedDataFieldSet(new[] { "fieldA", "fieldB" });

        var source = new Dictionary<string, object?>
        {
            ["fieldA"] = "valueA",
            ["fieldB"] = "valueB",
            ["fieldC"] = "filtered",
        };

        var result = BusinessDataFilter.FilterWithApprovedFields(source, approvedFields);

        result.Count.ShouldBe(2);
        result["fieldA"].ShouldBe("valueA");
        result.ContainsKey("fieldC").ShouldBeFalse();
    }

    /// <summary>
    /// 验证：FilterWithApprovedFields 拒绝 null source。
    /// 行为：返回空字典（源为空则过滤后也为空）。
    /// </summary>
    [Fact]
    public void FilterWithApprovedFields_Should_Handle_Null_Source()
    {
        var approvedFields = new ApprovedDataFieldSet(new[] { "fieldA", "fieldB" });

        var result = BusinessDataFilter.FilterWithApprovedFields(null, approvedFields);

        result.Count.ShouldBe(0);
    }

    /// <summary>
    /// 验证：FilterWithEndpointRegistration 使用端点登记的字段。
    /// 行为：使用 registration.ApprovedDataFields 过滤。
    /// </summary>
    [Fact]
    public void FilterWithEndpointRegistration_Should_Use_Registration_Fields()
    {
        var registration = new AdminErrorEndpointRegistration
        {
            ExitId = "TestController.Test()",
            DeclaringType = "TestController",
            MethodName = "Test",
            ParameterSignature = "()",
            Audience = ClientErrorAudience.AdminCompatibility,
            Protocol = ErrorProtocol.AbpEnvelope,
            ApprovedDataFields = new List<string> { "allowed" },
        };

        var source = new Dictionary<string, object?>
        {
            ["allowed"] = "kept",
            ["disallowed"] = "filtered",
        };

        var result = BusinessDataFilter.FilterWithEndpointRegistration(source, registration);

        result.Count.ShouldBe(1);
        result["allowed"].ShouldBe("kept");
    }

    /// <summary>
    /// 验证：FilterWithEndpointRegistration 处理 null registration。
    /// 行为：返回空字典。
    /// </summary>
    [Fact]
    public void FilterWithEndpointRegistration_Should_Handle_Null_Registration()
    {
        var source = new Dictionary<string, object?> { ["key"] = "value" };

        var result = BusinessDataFilter.FilterWithEndpointRegistration(source, null);

        result.Count.ShouldBe(0);
    }

    /// <summary>
    /// 验证：FilterWithEndpointRegistration 处理无 ApprovedDataFields 的 registration。
    /// 行为：返回空字典。
    /// </summary>
    [Fact]
    public void FilterWithEndpointRegistration_Should_Handle_Empty_ApprovedFields()
    {
        var registration = new AdminErrorEndpointRegistration
        {
            ExitId = "TestController.Test()",
            DeclaringType = "TestController",
            MethodName = "Test",
            ParameterSignature = "()",
            Audience = ClientErrorAudience.AdminCompatibility,
            Protocol = ErrorProtocol.AbpEnvelope,
        };

        var source = new Dictionary<string, object?> { ["key"] = "value" };

        var result = BusinessDataFilter.FilterWithEndpointRegistration(source, registration);

        result.Count.ShouldBe(0);
    }
}