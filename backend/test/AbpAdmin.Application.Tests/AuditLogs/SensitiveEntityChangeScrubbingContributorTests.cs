using System;
using System.Linq;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Volo.Abp.Auditing;
using Xunit;

namespace AbpAdmin.AuditLogs;

/* 安全控制回归测试：AddAllEntities + 落库前脱敏。
 * 框架契约（rel-10.x 源码核实）：PostContributors 在 AuditingManager.BeforeSave 里、
 * IAuditingStore.SaveAsync 之前执行，contributor 对 AuditLogInfo 的改写会落库。
 * AuditLogContributionContext 有公共构造（IServiceProvider, AuditLogInfo），
 * 因此本控制无需宿主环境即可纯单元验证。
 */
public class SensitiveEntityChangeScrubbingContributorTests
{
    private static EntityChangeInfo MakeChange(string entityTypeFullName, params (string Name, string? Old, string? New)[] properties)
    {
        var change = new EntityChangeInfo
        {
            EntityTypeFullName = entityTypeFullName,
            EntityId = "42",
            ChangeType = EntityChangeType.Updated,
            PropertyChanges = new System.Collections.Generic.List<EntityPropertyChangeInfo>(),
        };
        foreach (var (name, old, @new) in properties)
        {
            change.PropertyChanges.Add(new EntityPropertyChangeInfo
            {
                PropertyName = name,
                PropertyTypeFullName = "System.String",
                OriginalValue = old,
                NewValue = @new,
            });
        }

        return change;
    }

    [Fact]
    public void Should_Mask_Sensitive_Properties_And_Keep_Others()
    {
        var auditInfo = new AuditLogInfo();
        auditInfo.EntityChanges.Add(MakeChange(
            "Volo.Abp.Identity.IdentityUser",
            ("PasswordHash", "AQAAAAIA", "AQAAAAIB"),
            ("DisplayName", "旧名", "新名")));
        auditInfo.EntityChanges.Add(MakeChange(
            "Volo.Abp.OpenIddict.Tokens.Token",
            ("Payload", "eyJhbGciOi旧", "eyJhbGciOi新"),
            ("Subject", "user-1", "user-1")));

        var contributor = new SensitiveEntityChangeScrubbingContributor();
        var context = new AuditLogContributionContext(
            new ServiceCollection().BuildServiceProvider(), auditInfo);

        contributor.PostContribute(context);

        var user = auditInfo.EntityChanges[0].PropertyChanges.ToDictionary(p => p.PropertyName);
        user["PasswordHash"].OriginalValue.ShouldBe("[REDACTED]");
        user["PasswordHash"].NewValue.ShouldBe("[REDACTED]");
        user["DisplayName"].OriginalValue.ShouldBe("旧名");
        user["DisplayName"].NewValue.ShouldBe("新名");

        var token = auditInfo.EntityChanges[1].PropertyChanges.ToDictionary(p => p.PropertyName);
        token["Payload"].OriginalValue.ShouldBe("[REDACTED]");
        token["Payload"].NewValue.ShouldBe("[REDACTED]");
        token["Subject"].NewValue.ShouldBe("user-1");
    }

    [Fact]
    public void Should_Mask_Case_Insensitively()
    {
        var auditInfo = new AuditLogInfo();
        auditInfo.EntityChanges.Add(MakeChange(
            "Some.Entity",
            ("clientsecret", "plain-secret", "plain-secret-2")));

        var contributor = new SensitiveEntityChangeScrubbingContributor();
        contributor.PostContribute(new AuditLogContributionContext(
            new ServiceCollection().BuildServiceProvider(), auditInfo));

        auditInfo.EntityChanges[0].PropertyChanges.Single().NewValue.ShouldBe("[REDACTED]");
    }
}
