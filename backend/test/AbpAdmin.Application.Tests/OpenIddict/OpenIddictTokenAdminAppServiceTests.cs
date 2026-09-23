using System;
using System.Linq;
using System.Threading.Tasks;
using AbpAdmin.OpenIddict;
using Shouldly;
using Volo.Abp;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Modularity;
using OpenIddict.Abstractions;
using Volo.Abp.OpenIddict.Applications;
using Volo.Abp.OpenIddict.Authorizations;
using Volo.Abp.OpenIddict.Tokens;
using Volo.Abp.Uow;
using Xunit;

namespace AbpAdmin.OpenIddict;

/* 令牌/授权管理测试：吊销幂等与级联、按用户吊销对称性、组合过滤（Subject+ApplicationId
 * 同时生效）、Prune 保留期（刚兑换的令牌必须保留供重放检测）。 */
public abstract class OpenIddictTokenAdminAppServiceTests<TStartupModule> : AbpAdminApplicationTestBase<TStartupModule>
    where TStartupModule : IAbpModule
{
    private readonly IOpenIddictTokenAdminAppService _appService;
    private readonly IRepository<OpenIddictToken, Guid> _tokenRepository;
    private readonly IRepository<OpenIddictAuthorization, Guid> _authorizationRepository;
    private readonly IRepository<OpenIddictApplication, Guid> _applicationRepository;

    protected OpenIddictTokenAdminAppServiceTests()
    {
        _appService = GetRequiredService<IOpenIddictTokenAdminAppService>();
        _tokenRepository = GetRequiredService<IRepository<OpenIddictToken, Guid>>();
        _authorizationRepository = GetRequiredService<IRepository<OpenIddictAuthorization, Guid>>();
        _applicationRepository = GetRequiredService<IRepository<OpenIddictApplication, Guid>>();
    }

    private async Task<(OpenIddictApplication app, Guid subject1, Guid subject2)> SeedAsync()
    {
        await WithUnitOfWorkAsync(async () =>
        {
            await _tokenRepository.DeleteAsync(x => true);
            await _authorizationRepository.DeleteAsync(x => true);
            await _applicationRepository.DeleteAsync(x => true);
        });

        var app = new OpenIddictApplication(Guid.NewGuid()) { ClientId = "test-client" };
        var subject1 = Guid.NewGuid();
        var subject2 = Guid.NewGuid();

        await WithUnitOfWorkAsync(async () =>
        {
            await _applicationRepository.InsertAsync(app, autoSave: true);
            await _tokenRepository.InsertAsync(new OpenIddictToken(Guid.NewGuid())
            {
                ApplicationId = app.Id,
                Subject = subject1.ToString(),
                Type = "refresh_token",
                Status = OpenIddictConstants.Statuses.Valid,
                CreationDate = DateTime.UtcNow.AddDays(-1),
            }, autoSave: true);
            await _tokenRepository.InsertAsync(new OpenIddictToken(Guid.NewGuid())
            {
                ApplicationId = app.Id,
                Subject = subject2.ToString(),
                Type = "access_token",
                Status = OpenIddictConstants.Statuses.Valid,
                CreationDate = DateTime.UtcNow.AddDays(-2),
            }, autoSave: true);
            await _authorizationRepository.InsertAsync(new OpenIddictAuthorization(Guid.NewGuid())
            {
                ApplicationId = app.Id,
                Subject = subject1.ToString(),
                Status = OpenIddictConstants.Statuses.Valid,
                Type = "permanent",
                CreationDate = DateTime.UtcNow.AddDays(-1),
            }, autoSave: true);
        });

        return (app, subject1, subject2);
    }

    [Fact]
    public async Task GetTokens_Should_Apply_Subject_And_Application_Together()
    {
        var (app, subject1, _) = await SeedAsync();

        // Subject 与 ApplicationId 同传：两者同时生效（回归：曾因 if/else 静默丢弃 ApplicationId）
        var result = await _appService.GetTokensAsync(new OpenIddictTokenListInput
        {
            Subject = subject1.ToString(),
            ApplicationId = app.Id,
            SkipCount = 0,
            MaxResultCount = 10,
        });

        result.TotalCount.ShouldBe(1);
        result.Items.Single().Subject.ShouldBe(subject1.ToString());
        result.Items.Single().ApplicationClientId.ShouldBe("test-client");
    }

    [Fact]
    public async Task Revoke_Token_Should_Be_Idempotent()
    {
        var (_, subject1, _) = await SeedAsync();
        var token = (await _tokenRepository.GetListAsync(x => x.Subject == subject1.ToString())).Single();

        await _appService.RevokeTokenAsync(token.Id);
        await _appService.RevokeTokenAsync(token.Id); // 二次吊销不抛异常

        var after = await _tokenRepository.FindAsync(token.Id);
        after!.Status.ShouldBe(OpenIddictConstants.Statuses.Revoked);
    }

    [Fact]
    public async Task Revoke_Authorization_Should_Cascade_Tokens()
    {
        var (_, subject1, _) = await SeedAsync();
        var authorization = (await _authorizationRepository.GetListAsync(x => x.Subject == subject1.ToString())).Single();
        var token = (await _tokenRepository.GetListAsync(x => x.Subject == subject1.ToString())).Single();
        token.AuthorizationId = authorization.Id;
        await WithUnitOfWorkAsync(() => _tokenRepository.UpdateAsync(token, autoSave: true));

        await _appService.RevokeAuthorizationAsync(authorization.Id);

        (await _authorizationRepository.FindAsync(authorization.Id))!.Status
            .ShouldBe(OpenIddictConstants.Statuses.Revoked);
        (await _tokenRepository.FindAsync(token.Id))!.Status
            .ShouldBe(OpenIddictConstants.Statuses.Revoked);
    }

    [Fact]
    public async Task Revoke_By_Subject_Should_Revoke_Tokens_And_Authorizations_And_Reject_Empty()
    {
        var (_, subject1, _) = await SeedAsync();

        await Should.ThrowAsync<BusinessException>(async () =>
        {
            await _appService.RevokeBySubjectAsync("  ");
        });

        var count = await _appService.RevokeBySubjectAsync(subject1.ToString());
        count.ShouldBe(1);

        var token = (await _tokenRepository.GetListAsync(x => x.Subject == subject1.ToString())).Single();
        token.Status.ShouldBe(OpenIddictConstants.Statuses.Revoked);
        var authorization = (await _authorizationRepository.GetListAsync(x => x.Subject == subject1.ToString())).Single();
        authorization.Status.ShouldBe(OpenIddictConstants.Statuses.Revoked);
    }

    [Fact]
    public async Task Prune_Should_Keep_Recently_Redeemed_Tokens()
    {
        var (_, subject1, _) = await SeedAsync();
        var token = (await _tokenRepository.GetListAsync(x => x.Subject == subject1.ToString())).Single();
        token.RedemptionDate = DateTime.UtcNow.AddMinutes(-5); // 刚兑换 5 分钟
        await WithUnitOfWorkAsync(() => _tokenRepository.UpdateAsync(token, autoSave: true));

        var result = await _appService.PruneAsync();

        // 刚兑换的令牌必须保留（14 天保留窗供重放检测），不会被立即清理
        (await _tokenRepository.FindAsync(token.Id)).ShouldNotBeNull();
        _ = result;
    }
}
