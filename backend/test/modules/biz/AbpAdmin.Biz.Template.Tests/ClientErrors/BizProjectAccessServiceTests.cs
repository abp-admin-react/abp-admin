using System;
using System.Security.Claims;
using System.Threading.Tasks;
using AbpAdmin.Biz.Template.Entities;
using AbpAdmin.Biz.Template.Services;
using Shouldly;
using Volo.Abp;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Guids;
using Volo.Abp.Security.Claims;
using Xunit;

namespace AbpAdmin.Biz.Template.ClientErrors;

public class BizProjectAccessServiceTests : BizTemplateTestBase
{
    private readonly IBizProjectAccessService _accessService;
    private readonly IRepository<BizProject, Guid> _repository;
    private readonly ICurrentPrincipalAccessor _currentPrincipalAccessor;
    private readonly IGuidGenerator _guidGenerator;

    public BizProjectAccessServiceTests()
    {
        _accessService = GetRequiredService<IBizProjectAccessService>();
        _repository = GetRequiredService<IRepository<BizProject, Guid>>();
        _currentPrincipalAccessor = GetRequiredService<ICurrentPrincipalAccessor>();
        _guidGenerator = GetRequiredService<IGuidGenerator>();
    }

    private IDisposable AsUser(Guid userId)
    {
        return _currentPrincipalAccessor.Change(new ClaimsPrincipal(new ClaimsIdentity(
        [
            new Claim(AbpClaimTypes.UserId, userId.ToString()),
        ])));
    }

    [Fact]
    public async Task GetOwnedActive_Should_Map_Missing_OtherUser_And_Inactive_To_Same_Code()
    {
        var ownerId = Guid.NewGuid();
        var otherUserId = Guid.NewGuid();
        Guid ownedId = default;
        Guid inactiveId = default;

        using (AsUser(ownerId))
        {
            await WithUnitOfWorkAsync(async () =>
            {
                var owned = await _repository.InsertAsync(
                    new BizProject(_guidGenerator.Create(), "owned-active"), autoSave: true);
                ownedId = owned.Id;

                var inactive = await _repository.InsertAsync(
                    new BizProject(_guidGenerator.Create(), "owned-inactive", isActive: false), autoSave: true);
                inactiveId = inactive.Id;
            });
        }

        await WithUnitOfWorkAsync(async () =>
        {
            var missing = await Should.ThrowAsync<BusinessException>(() =>
                _accessService.GetOwnedActiveAsync(Guid.NewGuid(), ownerId));
            var otherUser = await Should.ThrowAsync<BusinessException>(() =>
                _accessService.GetOwnedActiveAsync(ownedId, otherUserId));
            var inactive = await Should.ThrowAsync<BusinessException>(() =>
                _accessService.GetOwnedActiveAsync(inactiveId, ownerId));

            missing.Code.ShouldBe(BizTemplateClientErrorCodes.ProjectUnavailable);
            otherUser.Code.ShouldBe(BizTemplateClientErrorCodes.ProjectUnavailable);
            inactive.Code.ShouldBe(BizTemplateClientErrorCodes.ProjectUnavailable);
        });
    }

    [Fact]
    public async Task GetOwnedActive_Should_Return_Owner_Active_Project()
    {
        var ownerId = Guid.NewGuid();
        Guid projectId = default;

        using (AsUser(ownerId))
        {
            await WithUnitOfWorkAsync(async () =>
            {
                var project = await _repository.InsertAsync(
                    new BizProject(_guidGenerator.Create(), "visible"), autoSave: true);
                projectId = project.Id;
            });
        }

        await WithUnitOfWorkAsync(async () =>
        {
            var project = await _accessService.GetOwnedActiveAsync(projectId, ownerId);
            project.Id.ShouldBe(projectId);
            project.Name.ShouldBe("visible");
        });
    }

    [Fact]
    public async Task GetOwnedList_Should_Exclude_Inactive_Projects()
    {
        var ownerId = Guid.NewGuid();

        using (AsUser(ownerId))
        {
            await WithUnitOfWorkAsync(async () =>
            {
                await _repository.InsertAsync(
                    new BizProject(_guidGenerator.Create(), "active"), autoSave: true);
                await _repository.InsertAsync(
                    new BizProject(_guidGenerator.Create(), "inactive", isActive: false), autoSave: true);
            });
        }

        await WithUnitOfWorkAsync(async () =>
        {
            var items = await _accessService.GetOwnedListAsync(ownerId);
            items.ShouldContain(project => project.Name == "active");
            items.ShouldNotContain(project => project.Name == "inactive");
        });
    }
}
