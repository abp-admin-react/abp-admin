using System;
using System.Security.Claims;
using System.Threading.Tasks;
using AbpAdmin.Biz.Template.Services.Dtos;
using Shouldly;
using Volo.Abp;
using Volo.Abp.Application.Dtos;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Security.Claims;
using Volo.Abp.Guids;
using Volo.Abp.SettingManagement;
using Xunit;

namespace AbpAdmin.Biz.Template.Services;

/// <summary>
/// 第②层（MaxPageSize 钳制）与第③层（配额参数）的消费行为测试。
/// 这里钉住的边界正是注释里声明的契约：配额 ≥ 判定、0=不限、非法值回落默认（fail-closed）、
/// 单页钳制上界；MaxResultCount ≤ 0 由 ABP DTO 校验拒绝（钳制下界仅作直调防御）。
/// </summary>
public class BizProjectAppServiceTests : BizTemplateTestBase
{
    private readonly IBizProjectAppService _appService;
    private readonly IRepository<Entities.BizProject, Guid> _repository;
    private readonly ISettingManager _settingManager;
    private readonly ICurrentPrincipalAccessor _currentPrincipalAccessor;
    private readonly IGuidGenerator _guidGenerator;

    public BizProjectAppServiceTests()
    {
        _appService = GetRequiredService<IBizProjectAppService>();
        _repository = GetRequiredService<IRepository<Entities.BizProject, Guid>>();
        _settingManager = GetRequiredService<ISettingManager>();
        _currentPrincipalAccessor = GetRequiredService<ICurrentPrincipalAccessor>();
        _guidGenerator = GetRequiredService<IGuidGenerator>();
    }

    /// <summary>以指定用户身份执行（配额按 CreatorId 分桶，需要可认证的当前用户）。</summary>
    private IDisposable AsUser(Guid userId)
    {
        return _currentPrincipalAccessor.Change(new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim(AbpClaimTypes.UserId, userId.ToString()),
        })));
    }

    private async Task SeedRowsAsync(int count)
    {
        await WithUnitOfWorkAsync(async () =>
        {
            for (var i = 0; i < count; i++)
            {
                await _repository.InsertAsync(
                    new Entities.BizProject(_guidGenerator.Create(), $"项目-{i}"), autoSave: true);
            }
        });
    }

    [Fact]
    public async Task Quota_Should_Reject_When_Reached()
    {
        var userId = Guid.NewGuid();
        await _settingManager.SetGlobalAsync(
            Settings.BizTemplateSettings.Project.MaxProjectsPerUser, "2");

        using (AsUser(userId))
        {
            await _appService.CreateAsync(new CreateUpdateBizProjectDto { Name = "一" });
            await _appService.CreateAsync(new CreateUpdateBizProjectDto { Name = "二" });

            var ex = await Should.ThrowAsync<UserFriendlyException>(async () =>
                await _appService.CreateAsync(new CreateUpdateBizProjectDto { Name = "三" }));

            // 位置占位符 {0} 已由上限值填充——同时钉住本地化格式化不抛 FormatException
            ex.Message.ShouldContain("2");
        }
    }

    [Fact]
    public async Task Quota_Zero_Should_Be_Unlimited()
    {
        var userId = Guid.NewGuid();
        await _settingManager.SetGlobalAsync(
            Settings.BizTemplateSettings.Project.MaxProjectsPerUser, "0");

        using (AsUser(userId))
        {
            for (var i = 0; i < 3; i++)
            {
                await _appService.CreateAsync(new CreateUpdateBizProjectDto { Name = $"P{i}" });
            }

            (await _repository.GetCountAsync()).ShouldBeGreaterThanOrEqualTo(3);
        }
    }

    [Fact]
    public async Task Quota_Invalid_Stored_Value_Should_Fall_Back_To_Default_Not_Unlimited()
    {
        var userId = Guid.NewGuid();

        // 非法值（非数字）按 fail-closed 回落默认 100：不能像 0 一样变成"不限"，
        // 可观察差异在第 101 次创建——不限则全部放行，回落默认则在 100 处拒绝。
        await _settingManager.SetGlobalAsync(
            Settings.BizTemplateSettings.Project.MaxProjectsPerUser, "not-a-number");

        using (AsUser(userId))
        {
            for (var i = 0; i < 100; i++)
            {
                await _appService.CreateAsync(new CreateUpdateBizProjectDto { Name = $"P{i}" });
            }

            await Should.ThrowAsync<UserFriendlyException>(async () =>
                await _appService.CreateAsync(new CreateUpdateBizProjectDto { Name = "P-101" }));
        }
    }

    [Fact]
    public async Task Page_Size_Should_Be_Clamped_By_Configured_Max()
    {
        await SeedRowsAsync(105);
        await _settingManager.SetGlobalAsync(
            Settings.BizTemplateSettings.Project.MaxProjectsPerUser, "0");

        // 请求超上限：被基线 MaxPageSize=100 钳住
        var oversized = await _appService.GetListAsync(new PagedAndSortedResultRequestDto
        {
            SkipCount = 0,
            MaxResultCount = 500,
        });
        oversized.Items.Count.ShouldBe(100);
        oversized.TotalCount.ShouldBeGreaterThanOrEqualTo(105);

        // 正常请求不受钳制影响
        var normal = await _appService.GetListAsync(new PagedAndSortedResultRequestDto
        {
            SkipCount = 0,
            MaxResultCount = 10,
        });
        normal.Items.Count.ShouldBe(10);

        // MaxResultCount ≤ 0：被 ABP 的 DTO 校验拦下（LimitedResultRequestDto 的 Range(1,…)），
        // 到不了服务端钳制——这是经校验通道的真实 API 契约；服务内 Clamp 下界只是直调防御。
        await Should.ThrowAsync<Volo.Abp.Validation.AbpValidationException>(async () =>
            await _appService.GetListAsync(new PagedAndSortedResultRequestDto
            {
                SkipCount = 0,
                MaxResultCount = 0,
            }));
    }
}
