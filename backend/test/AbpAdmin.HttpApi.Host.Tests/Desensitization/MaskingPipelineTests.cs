using System;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using AbpAdmin.Desensitization;
using Shouldly;
using Xunit;

namespace AbpAdmin.Desensitization;

/* 脱敏管线纯单测（审查 test 透镜 F1/F2：PII 决策路径零覆盖）。
 * 覆盖：STJ resolver 接线（attribute/registry 两种规则源、非 string 跳过、派生 DTO）、
 * converter 的权限分支（授予→明文 / 拒绝→脱敏 / Gate 未初始化→fail-closed 脱敏）。
 */
public class MaskingPipelineTests
{
    private class SampleDto
    {
        public string Name { get; set; } = "张三";

        [Masked(MaskKindEnum.Mobile)]
        public string? Phone { get; set; }

        [Masked(MaskKindEnum.Custom, PrefixKeep = 1, SuffixKeep = 1)]
        public string? Token { get; set; }

        public int Age { get; set; } = 20;

        [Masked(MaskKindEnum.Email)]
        public int WrongTypedMask { get; set; } = 7; // 非 string：规则应跳过不崩溃
    }

    private sealed class DerivedDto : SampleDto;

    private static JsonSerializerOptions BuildOptions(MaskingRuleRegistry? registry = null)
    {
        var options = new JsonSerializerOptions
        {
            // 断言按原文比较中文；MVC 真实管线的转义策略与本测试目的无关
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        };
        MaskingJsonSetup.Configure(options, registry ?? new MaskingRuleRegistry());
        return options;
    }

    [Fact]
    public void Attribute_Rules_Mask_String_Properties()
    {
        MaskingPermissionGate.Current = null; // fail-closed：无门=一律脱敏

        var json = JsonSerializer.Serialize(new SampleDto { Phone = "13248765917", Token = "abcdef" }, BuildOptions());

        json.ShouldContain("\"Phone\":\"132****5917\"");
        json.ShouldContain("\"Token\":\"a****f\"");
        json.ShouldContain("\"Name\":\"张三\""); // 无规则字段原样
    }

    [Fact]
    public void Non_String_Masked_Property_Is_Skipped_Without_Crash()
    {
        MaskingPermissionGate.Current = null;

        var json = JsonSerializer.Serialize(new SampleDto(), BuildOptions());

        json.ShouldContain("\"WrongTypedMask\":7");
    }

    [Fact]
    public void Registry_Rule_Applies_To_Derived_Dto()
    {
        MaskingPermissionGate.Current = null;
        var registry = new MaskingRuleRegistry()
            .Add<SampleDto>(x => x.Name, new MaskedAttribute(MaskKindEnum.ChineseName));

        var json = JsonSerializer.Serialize(new DerivedDto { Phone = "13248765917" }, BuildOptions(registry));

        // 基类注册的规则命中派生类同名字段；attribute 规则（Phone）继续生效
        json.ShouldContain("\"Name\":\"张*\"");
        json.ShouldContain("\"Phone\":\"132****5917\"");
    }

    [Fact]
    public void Registry_Add_Rejects_NonMember_Selector()
    {
        var registry = new MaskingRuleRegistry();

        Should.Throw<ArgumentException>(() =>
            registry.Add<SampleDto>(x => x.Age.ToString(), new MaskedAttribute(MaskKindEnum.Password)));
    }

    [Fact]
    public void Null_String_Still_Serializes_As_Null()
    {
        MaskingPermissionGate.Current = null;

        var json = JsonSerializer.Serialize(new SampleDto { Phone = null }, BuildOptions());

        json.ShouldContain("\"Phone\":null");
    }

    [Fact]
    public void Granted_Plaintext_Permission_Writes_Plaintext_And_Caches_Per_Request()
    {
        // 请求内 IPermissionChecker 授权 → 明文；同请求多次序列化只查一次权限（HttpContext.Items 缓存）
        var checker = new CountingPermissionChecker(granted: true);
        var httpContext = new DefaultHttpContext();
        var services = new Microsoft.Extensions.DependencyInjection.ServiceCollection();
        services.AddSingleton<Volo.Abp.Authorization.Permissions.IPermissionChecker>(checker);
        httpContext.RequestServices = services.BuildServiceProvider();
        var accessor = new StubAccessor { HttpContext = httpContext };
        MaskingPermissionGate.Current = new MaskingPermissionGate(accessor, Microsoft.Extensions.Logging.Abstractions.NullLogger<MaskingPermissionGate>.Instance);
        try
        {
            var options = BuildOptions(new MaskingRuleRegistry()
                .Add<SampleDto>(x => x.Name, new MaskedAttribute(MaskKindEnum.ChineseName)
                {
                    PlaintextPermission = "Test.Perm",
                }));

            for (var i = 0; i < 5; i++)
            {
                JsonSerializer.Serialize(new SampleDto(), options).ShouldContain("\"Name\":\"张三\"");
            }

            checker.CallCount.ShouldBe(1);
        }
        finally
        {
            MaskingPermissionGate.Current = null;
        }
    }

    private sealed class StubAccessor : IHttpContextAccessor
    {
        public HttpContext? HttpContext { get; set; }
    }

    private sealed class CountingPermissionChecker(bool granted) : Volo.Abp.Authorization.Permissions.IPermissionChecker
    {
        public int CallCount { get; private set; }

        public Task<bool> IsGrantedAsync(string name) => Count();

        public Task<bool> IsGrantedAsync(System.Security.Claims.ClaimsPrincipal? claimsPrincipal, string name) => Count();

        public Task<Volo.Abp.Authorization.Permissions.MultiplePermissionGrantResult> IsGrantedAsync(params string[] names)
            => throw new NotSupportedException("数组重载不在本测试范围");

        public Task<Volo.Abp.Authorization.Permissions.MultiplePermissionGrantResult> IsGrantedAsync(System.Security.Claims.ClaimsPrincipal? claimsPrincipal, params string[] names)
            => throw new NotSupportedException("数组重载不在本测试范围");

        private Task<bool> Count()
        {
            CallCount++;
            return Task.FromResult(granted);
        }
    }
}
