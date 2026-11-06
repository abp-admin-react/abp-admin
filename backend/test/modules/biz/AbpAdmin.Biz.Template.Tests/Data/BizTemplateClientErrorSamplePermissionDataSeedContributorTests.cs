using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AbpAdmin.Biz.Template.Configuration;
using AbpAdmin.Biz.Template.Permissions;
using Microsoft.Extensions.Options;
using Shouldly;
using Volo.Abp.Authorization.Permissions;
using Volo.Abp.Data;
using Volo.Abp.PermissionManagement;
using Volo.Abp.Roles;
using Xunit;

namespace AbpAdmin.Biz.Template.Data;

public class BizTemplateClientErrorSamplePermissionDataSeedContributorTests
{
    private sealed class RecordingPermissionDataSeeder : IPermissionDataSeeder
    {
        public int CallCount { get; private set; }

        public string? ProviderName { get; private set; }

        public string? ProviderKey { get; private set; }

        public IReadOnlyList<string>? GrantNames { get; private set; }

        public Task SeedAsync(
            string providerName,
            string providerKey,
            IEnumerable<string> grantNames,
            Guid? tenantId = null)
        {
            CallCount++;
            ProviderName = providerName;
            ProviderKey = providerKey;
            GrantNames = grantNames.ToList();
            return Task.CompletedTask;
        }
    }

    [Fact]
    public async Task Seed_Should_Skip_When_Sample_Disabled()
    {
        var recorder = new RecordingPermissionDataSeeder();
        var contributor = new BizTemplateClientErrorSamplePermissionDataSeedContributor(
            recorder,
            Options.Create(new BizTemplateOptions { EnableClientErrorSample = false }));

        await contributor.SeedAsync(new DataSeedContext());

        recorder.CallCount.ShouldBe(0);
    }

    [Fact]
    public async Task Seed_Should_Grant_Sample_Permission_To_Admin_When_Enabled()
    {
        var recorder = new RecordingPermissionDataSeeder();
        var contributor = new BizTemplateClientErrorSamplePermissionDataSeedContributor(
            recorder,
            Options.Create(new BizTemplateOptions { EnableClientErrorSample = true }));

        await contributor.SeedAsync(new DataSeedContext());

        recorder.CallCount.ShouldBe(1);
        recorder.ProviderName.ShouldBe(RolePermissionValueProvider.ProviderName);
        recorder.ProviderKey.ShouldBe(AbpRoleConsts.AdminRoleName);
        recorder.GrantNames.ShouldNotBeNull();
        recorder.GrantNames.ShouldContain(BizTemplatePermissions.ClientErrorSample.Default);
    }
}
