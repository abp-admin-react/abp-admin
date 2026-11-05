using System;
using System.Collections.Generic;
using System.Linq;
using Shouldly;
using Xunit;

namespace AbpAdmin.ClientErrors;

/// <summary>
/// P1 契约测试：公开错误出口组合规则（方案 5.2 + 方案 7.2）。
/// 验证 401/403/429/基础码 kind 一致性等基础规则。
/// </summary>
public class PublicErrorExitContractTests
{
    /// <summary>
    /// 验证：401 只允许 Authentication kind，publicCode 只能 null 或 AuthenticationRequired。
    /// </summary>
    [Fact]
    public void Http401_Should_Only_Allow_Authentication_Kind()
    {
        var valid = new PublicErrorExitCombination
        {
            PublicCode = null,
            Kind = PublicErrorKind.Authentication,
            HttpStatus = 401,
        };
        PublicErrorExitContract.IsAllowed(valid).ShouldBeTrue();

        var validWithCode = new PublicErrorExitCombination
        {
            PublicCode = ClientErrorCodes.AuthenticationRequired,
            Kind = PublicErrorKind.Authentication,
            HttpStatus = 401,
        };
        PublicErrorExitContract.IsAllowed(validWithCode).ShouldBeTrue();

        var invalid = new PublicErrorExitCombination
        {
            PublicCode = ClientErrorCodes.Forbidden,
            Kind = PublicErrorKind.Forbidden,
            HttpStatus = 401,
        };
        PublicErrorExitContract.IsAllowed(invalid).ShouldBeFalse();
    }

    /// <summary>
    /// 验证：403 只允许 Business 或 Forbidden kind，禁止 Authentication。
    /// </summary>
    [Fact]
    public void Http403_Should_Disallow_Authentication_Kind()
    {
        var validBusiness = new PublicErrorExitCombination
        {
            PublicCode = ClientErrorCodes.OperationFailed,
            Kind = PublicErrorKind.Business,
            HttpStatus = 403,
        };
        PublicErrorExitContract.IsAllowed(validBusiness).ShouldBeTrue();

        var validForbidden = new PublicErrorExitCombination
        {
            PublicCode = ClientErrorCodes.Forbidden,
            Kind = PublicErrorKind.Forbidden,
            HttpStatus = 403,
        };
        PublicErrorExitContract.IsAllowed(validForbidden).ShouldBeTrue();

        var invalid = new PublicErrorExitCombination
        {
            PublicCode = ClientErrorCodes.AuthenticationRequired,
            Kind = PublicErrorKind.Authentication,
            HttpStatus = 403,
        };
        PublicErrorExitContract.IsAllowed(invalid).ShouldBeFalse();
    }

    /// <summary>
    /// 验证：429 只允许 RateLimited kind，publicCode 只能 null 或 RateLimited。
    /// </summary>
    [Fact]
    public void Http429_Should_Only_Allow_RateLimited_Kind()
    {
        var valid = new PublicErrorExitCombination
        {
            PublicCode = ClientErrorCodes.RateLimited,
            Kind = PublicErrorKind.RateLimited,
            HttpStatus = 429,
        };
        PublicErrorExitContract.IsAllowed(valid).ShouldBeTrue();

        var invalid = new PublicErrorExitCombination
        {
            PublicCode = ClientErrorCodes.Forbidden,
            Kind = PublicErrorKind.Forbidden,
            HttpStatus = 429,
        };
        PublicErrorExitContract.IsAllowed(invalid).ShouldBeFalse();
    }

    /// <summary>
    /// 验证：基础公开码的 kind 必须与 ClientErrorKindMapping 一致。
    /// </summary>
    [Fact]
    public void Base_PublicCode_Kind_Must_Be_Consistent()
    {
        var valid = new PublicErrorExitCombination
        {
            PublicCode = ClientErrorCodes.NotFound,
            Kind = PublicErrorKind.NotFound,
            HttpStatus = 404,
        };
        PublicErrorExitContract.IsAllowed(valid).ShouldBeTrue();

        var invalid = new PublicErrorExitCombination
        {
            PublicCode = ClientErrorCodes.NotFound,
            Kind = PublicErrorKind.Business,
            HttpStatus = 404,
        };
        PublicErrorExitContract.IsAllowed(invalid).ShouldBeFalse();
    }

    /// <summary>
    /// 验证：Authentication kind 只能用于 401。
    /// </summary>
    [Fact]
    public void Authentication_Kind_Requires_401()
    {
        var invalid = new PublicErrorExitCombination
        {
            PublicCode = ClientErrorCodes.AuthenticationRequired,
            Kind = PublicErrorKind.Authentication,
            HttpStatus = 500,
        };
        PublicErrorExitContract.IsAllowed(invalid).ShouldBeFalse();
    }

    /// <summary>
    /// 验证：Validation kind 只能用于 400。
    /// </summary>
    [Fact]
    public void Validation_Kind_Requires_400()
    {
        var valid = new PublicErrorExitCombination
        {
            PublicCode = ClientErrorCodes.ValidationFailed,
            Kind = PublicErrorKind.Validation,
            HttpStatus = 400,
        };
        PublicErrorExitContract.IsAllowed(valid).ShouldBeTrue();

        var invalid = new PublicErrorExitCombination
        {
            PublicCode = ClientErrorCodes.ValidationFailed,
            Kind = PublicErrorKind.Validation,
            HttpStatus = 500,
        };
        PublicErrorExitContract.IsAllowed(invalid).ShouldBeFalse();
    }

    /// <summary>
    /// 验证：NotFound kind 只能用于 404。
    /// </summary>
    [Fact]
    public void NotFound_Kind_Requires_404()
    {
        var valid = new PublicErrorExitCombination
        {
            PublicCode = ClientErrorCodes.NotFound,
            Kind = PublicErrorKind.NotFound,
            HttpStatus = 404,
        };
        PublicErrorExitContract.IsAllowed(valid).ShouldBeTrue();

        var invalid = new PublicErrorExitCombination
        {
            PublicCode = ClientErrorCodes.NotFound,
            Kind = PublicErrorKind.NotFound,
            HttpStatus = 400,
        };
        PublicErrorExitContract.IsAllowed(invalid).ShouldBeFalse();
    }

    /// <summary>
    /// 验证：Conflict kind 只能用于 409。
    /// </summary>
    [Fact]
    public void Conflict_Kind_Requires_409()
    {
        var valid = new PublicErrorExitCombination
        {
            PublicCode = ClientErrorCodes.Conflict,
            Kind = PublicErrorKind.Conflict,
            HttpStatus = 409,
        };
        PublicErrorExitContract.IsAllowed(valid).ShouldBeTrue();

        var invalid = new PublicErrorExitCombination
        {
            PublicCode = ClientErrorCodes.Conflict,
            Kind = PublicErrorKind.Conflict,
            HttpStatus = 400,
        };
        PublicErrorExitContract.IsAllowed(invalid).ShouldBeFalse();
    }

    /// <summary>
    /// 验证：Server kind 只能用于 5xx。
    /// </summary>
    [Fact]
    public void Server_Kind_Requires_5xx()
    {
        var valid = new PublicErrorExitCombination
        {
            PublicCode = ClientErrorCodes.Unexpected,
            Kind = PublicErrorKind.Server,
            HttpStatus = 503,
        };
        PublicErrorExitContract.IsAllowed(valid).ShouldBeTrue();

        var invalid = new PublicErrorExitCombination
        {
            PublicCode = ClientErrorCodes.Unexpected,
            Kind = PublicErrorKind.Server,
            HttpStatus = 400,
        };
        PublicErrorExitContract.IsAllowed(invalid).ShouldBeFalse();
    }

    /// <summary>
    /// 验证：Business kind 只能用于 4xx 或 5xx。
    /// </summary>
    [Theory]
    [InlineData(400, true)]
    [InlineData(403, true)]
    [InlineData(500, true)]
    public void Business_Kind_Requires_4xx_Or_5xx(int status, bool expected)
    {
        var combo = new PublicErrorExitCombination
        {
            PublicCode = ClientErrorCodes.OperationFailed,
            Kind = PublicErrorKind.Business,
            HttpStatus = status,
        };
        PublicErrorExitContract.IsAllowed(combo).ShouldBe(expected);
    }

    /// <summary>
    /// 验证：HTTP 状态码 < 400 在构造时抛异常（属性 init 验证）。
    /// </summary>
    [Fact]
    public void HttpStatus_Below_400_Should_Throw_At_Construction()
    {
        Should.Throw<ArgumentOutOfRangeException>(() =>
            new PublicErrorExitCombination
            {
                Kind = PublicErrorKind.Business,
                HttpStatus = 200,
            });
    }

    /// <summary>
    /// 验证：Validate 方法在非法组合时抛出 ArgumentException。
    /// </summary>
    [Fact]
    public void Validate_Should_Throw_For_Invalid_Combo()
    {
        var invalid = new PublicErrorExitCombination
        {
            PublicCode = ClientErrorCodes.NotFound,
            Kind = PublicErrorKind.Business,
            HttpStatus = 404,
        };
        Should.Throw<ArgumentException>(() => PublicErrorExitContract.Validate(invalid));
    }
}
