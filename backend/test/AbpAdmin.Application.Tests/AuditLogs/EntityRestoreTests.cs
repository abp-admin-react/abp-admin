using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Shouldly;
using Volo.Abp;
using Volo.Abp.Auditing;
using Volo.Abp.AuditLogging;
using Volo.Abp.Guids;
using Volo.Abp.Identity;
using Volo.Abp.Modularity;
using Xunit;

namespace AbpAdmin.AuditLogs;

/* 实体变更一键回滚（RestoreEntityChangeAsync）集成测试。
 *
 * 数据策略与 AuditLogStatisticsTests 一致：审计侧手工插入 AuditLog/EntityChange（属性值按
 * EntityHistoryHelper 的真实形态以 JSON 序列化落库），业务侧用真实 IdentityUser——
 * 回滚的真实效果（属性被写回）必须落在真实仓储上验证，不能只用审计侧假数据自证。
 * 用户名带唯一标记，测试库在 collection 内共享。
 *
 * 注意：EntityChange.Id 在其构造器内部由 IGuidGenerator 生成（入参只有 AuditLogId），
 * 调用 RestoreEntityChangeAsync 必须用 CreateEntityChangeForUserAsync 返回对象的 Id，
 * 不能用审计日志的 Id。
 */
public abstract class EntityRestoreTests<TStartupModule> : AuditLogTestBase<TStartupModule>
    where TStartupModule : IAbpModule
{
    private readonly IAuditLogAppService _auditLogAppService;
    private readonly IGuidGenerator _guidGenerator;
    private readonly IdentityUserManager _userManager;
    private readonly IIdentityUserRepository _userRepository;

    protected EntityRestoreTests()
    {
        _auditLogAppService = GetRequiredService<IAuditLogAppService>();
        _guidGenerator = GetRequiredService<IGuidGenerator>();
        _userManager = GetRequiredService<IdentityUserManager>();
        _userRepository = GetRequiredService<IIdentityUserRepository>();
    }

    [Fact]
    public async Task Restore_Should_Write_Original_Values_Back()
    {
        // Arrange：真实用户先被"改坏"（Name/Surname/IsExternal 变更），审计里存着改前的 JSON 值。
        // 属性选择有讲究：IdentityUser 只有 Name/Surname/IsExternal 是公开 setter，
        // 这三个恰好覆盖 string 与 bool 两种 JSON 反序列化路径。
        var user = await CreateTestUserAsync();
        const string originalName = "AliceRestore";
        const string changedName = "BobBroken";
        const string originalSurname = "Wang";
        const string changedSurname = "Li";

        var change = await CreateEntityChangeForUserAsync(
            user, EntityChangeType.Updated,
            new Dictionary<string, (object? original, object current)>
            {
                ["Name"] = (originalName, changedName),
                ["Surname"] = (originalSurname, changedSurname),
                ["IsExternal"] = (true, false),
            });

        await WithUnitOfWorkAsync(async () =>
        {
            var tracked = await _userRepository.FindAsync(user.Id);
            tracked!.Name = changedName;
            tracked.Surname = changedSurname;
            tracked.IsExternal = false;
            await _userRepository.UpdateAsync(tracked);
        });

        // Act
        var result = await _auditLogAppService.RestoreEntityChangeAsync(new RestoreEntityChangeInput
        {
            EntityChangeId = change.Id,
            EntityId = user.Id.ToString(),
            EntityTypeFullName = "Volo.Abp.Identity.IdentityUser"
        });

        // Assert：值写回，跳过列表为空
        result.RestoredProperties.ShouldBe(new[] { "Name", "Surname", "IsExternal" }, ignoreOrder: true);
        result.SkippedProperties.ShouldBeEmpty();

        await WithUnitOfWorkAsync(async () =>
        {
            var reloaded = await _userRepository.FindAsync(user.Id);
            reloaded!.Name.ShouldBe(originalName);
            reloaded.Surname.ShouldBe(originalSurname);
            reloaded.IsExternal.ShouldBe(true);
        });
    }

    [Fact]
    public async Task Restore_Should_Skip_Unresolvable_Properties_Without_Breaking_Others()
    {
        var user = await CreateTestUserAsync();

        var change = await CreateEntityChangeForUserAsync(
            user, EntityChangeType.Updated,
            new Dictionary<string, (object? original, object current)>
            {
                ["Name"] = ("AliceSkip", "BobSkip"),
                // 影子属性/已删除属性：实体上解析不到，应跳过而非整体失败
                ["GhostProperty"] = ("x", "y"),
            });

        await WithUnitOfWorkAsync(async () =>
        {
            var tracked = await _userRepository.FindAsync(user.Id);
            tracked!.Name = "BobSkip";
            await _userRepository.UpdateAsync(tracked);
        });

        var result = await _auditLogAppService.RestoreEntityChangeAsync(new RestoreEntityChangeInput
        {
            EntityChangeId = change.Id,
            EntityId = user.Id.ToString(),
            EntityTypeFullName = "Volo.Abp.Identity.IdentityUser"
        });

        result.RestoredProperties.ShouldBe(new[] { "Name" });
        var skipped = result.SkippedProperties.ShouldHaveSingleItem();
        skipped.PropertyName.ShouldBe("GhostProperty");
        skipped.Reason.ShouldNotBeNullOrEmpty();

        await WithUnitOfWorkAsync(async () =>
        {
            var reloaded = await _userRepository.FindAsync(user.Id);
            reloaded!.Name.ShouldBe("AliceSkip");
        });
    }

    [Fact]
    public async Task Restore_Should_Reject_Non_Updated_Change()
    {
        var user = await CreateTestUserAsync();

        var change = await CreateEntityChangeForUserAsync(
            user, EntityChangeType.Created,
            new Dictionary<string, (object? original, object current)>
            {
                ["Name"] = (null, "BobCreated")
            });

        var exception = await Should.ThrowAsync<UserFriendlyException>(() =>
            _auditLogAppService.RestoreEntityChangeAsync(new RestoreEntityChangeInput
            {
                EntityChangeId = change.Id,
                EntityId = user.Id.ToString(),
                EntityTypeFullName = "Volo.Abp.Identity.IdentityUser"
            }));

        exception.Code.ShouldBe(AbpAdminDomainErrorCodes.EntityRestore.OnlyUpdated);
    }

    [Fact]
    public async Task Restore_Should_Reject_Change_From_Another_Entity()
    {
        var user = await CreateTestUserAsync();

        var change = await CreateEntityChangeForUserAsync(
            user, EntityChangeType.Updated,
            new Dictionary<string, (object? original, object current)>
            {
                ["Name"] = ("a", "b")
            });

        // 用别人的实体 Id（同一类型）来认领这条变更 → 拒绝（按错误码区分拒绝路径）
        var exception = await Should.ThrowAsync<UserFriendlyException>(() =>
            _auditLogAppService.RestoreEntityChangeAsync(new RestoreEntityChangeInput
            {
                EntityChangeId = change.Id,
                EntityId = Guid.NewGuid().ToString(),
                EntityTypeFullName = "Volo.Abp.Identity.IdentityUser"
            }));
        exception.Code.ShouldBe(AbpAdminDomainErrorCodes.EntityChange.EntityMismatch);
    }

    [Fact]
    public async Task Restore_Should_Reject_Entity_Type_Outside_Whitelist()
    {
        // 白名单外实体（AddAllEntities 审计里有记录也不允许回滚）：用一段不存在于白名单的实体类型名
        var exception = await Should.ThrowAsync<UserFriendlyException>(() =>
            _auditLogAppService.RestoreEntityChangeAsync(new RestoreEntityChangeInput
            {
                EntityChangeId = Guid.NewGuid(),
                EntityId = Guid.NewGuid().ToString(),
                EntityTypeFullName = "Volo.Abp.Identity.IdentityClaimType"
            }));
        exception.Code.ShouldBe(AbpAdminDomainErrorCodes.EntityRestore.EntityTypeNotAllowed);
    }

    [Fact]
    public async Task Restore_Should_Not_Write_Redacted_Values_Into_Sensitive_Properties()
    {
        // SecurityStamp 在脱敏黑名单内：即使审计记录里存着掩码，回滚也必须跳过而不是写回
        var user = await CreateTestUserAsync();
        const string currentStamp = "stamp-after-change";

        var change = await CreateEntityChangeForUserAsync(
            user, EntityChangeType.Updated,
            new Dictionary<string, (object? original, object current)>
            {
                ["SecurityStamp"] = ("[REDACTED]", currentStamp),
                ["Name"] = ("AliceRedact", "BobRedact"),
            });

        await WithUnitOfWorkAsync(async () =>
        {
            var tracked = await _userRepository.FindAsync(user.Id);
            tracked!.Name = "BobRedact";
            await _userRepository.UpdateAsync(tracked);
        });

        var result = await _auditLogAppService.RestoreEntityChangeAsync(new RestoreEntityChangeInput
        {
            EntityChangeId = change.Id,
            EntityId = user.Id.ToString(),
            EntityTypeFullName = "Volo.Abp.Identity.IdentityUser"
        });

        // SecurityStamp 被跳过并说明；Name 正常还原
        result.RestoredProperties.ShouldBe(new[] { "Name" });
        var skipped = result.SkippedProperties.ShouldHaveSingleItem();
        skipped.PropertyName.ShouldBe("SecurityStamp");

        await WithUnitOfWorkAsync(async () =>
        {
            var reloaded = await _userRepository.FindAsync(user.Id);
            reloaded!.Name.ShouldBe("AliceRedact");
        });
    }

    [Fact]
    public async Task Restore_Then_Restore_Again_Should_Roll_Forward()
    {
        // 前滚闭环：回滚生成新的 Updated 变更，对其再回滚可把值改回"被改坏"的状态
        var user = await CreateTestUserAsync();

        var change = await CreateEntityChangeForUserAsync(
            user, EntityChangeType.Updated,
            new Dictionary<string, (object? original, object current)>
            {
                ["Name"] = ("AliceForward", "BobForward"),
            });

        await WithUnitOfWorkAsync(async () =>
        {
            var tracked = await _userRepository.FindAsync(user.Id);
            tracked!.Name = "BobForward";
            await _userRepository.UpdateAsync(tracked);
        });

        await _auditLogAppService.RestoreEntityChangeAsync(new RestoreEntityChangeInput
        {
            EntityChangeId = change.Id,
            EntityId = user.Id.ToString(),
            EntityTypeFullName = "Volo.Abp.Identity.IdentityUser"
        });

        // 回滚本身是受审计变更：从审计库取出该实体最新一条 Updated 记录再回滚（前滚）
        var history = await _auditLogAppService.GetEntityChangeHistoryAsync(new GetEntityChangeHistoryInput
        {
            EntityTypeFullName = "Volo.Abp.Identity.IdentityUser",
            EntityId = user.Id.ToString(),
            MaxResultCount = 5,
        });
        var forwardChange = history.Items.FirstOrDefault(x => x.Id != change.Id && x.ChangeType == (byte)EntityChangeType.Updated);
        forwardChange.ShouldNotBeNull("回滚应生成一条新的 Updated 变更记录");
        forwardChange.PropertyChanges.ShouldContain(p => p.PropertyName == "Name");

        await _auditLogAppService.RestoreEntityChangeAsync(new RestoreEntityChangeInput
        {
            EntityChangeId = forwardChange.Id,
            EntityId = user.Id.ToString(),
            EntityTypeFullName = "Volo.Abp.Identity.IdentityUser"
        });

        await WithUnitOfWorkAsync(async () =>
        {
            var reloaded = await _userRepository.FindAsync(user.Id);
            reloaded!.Name.ShouldBe("BobForward");
        });
    }

    [Fact]
    public async Task Restore_Should_Throw_When_Nothing_Restorable()
    {
        var user = await CreateTestUserAsync();

        var change = await CreateEntityChangeForUserAsync(
            user, EntityChangeType.Updated,
            new Dictionary<string, (object? original, object current)>
            {
                ["GhostProperty"] = ("x", "y")
            });

        var before = await WithUnitOfWorkAsync(() => _userRepository.FindAsync(user.Id));

        var exception = await Should.ThrowAsync<UserFriendlyException>(() =>
            _auditLogAppService.RestoreEntityChangeAsync(new RestoreEntityChangeInput
            {
                EntityChangeId = change.Id,
                EntityId = user.Id.ToString(),
                EntityTypeFullName = "Volo.Abp.Identity.IdentityUser"
            }));

        exception.Message.ShouldNotBeNullOrEmpty();
        await WithUnitOfWorkAsync(async () =>
        {
            var after = await _userRepository.FindAsync(user.Id);
            after!.Name.ShouldBe(before!.Name);
        });
    }

    private async Task<IdentityUser> CreateTestUserAsync()
    {
        var userId = _guidGenerator.Create();
        var userName = $"restore_{userId:N}";
        var user = new IdentityUser(userId, userName, $"{userName}@abpadmin.test");
        user.Name = "AliceRestore";

        (await _userManager.CreateAsync(user)).Succeeded.ShouldBeTrue();
        return user;
    }

    /// <summary>
    /// 插入一条审计日志并返回其中的实体变更记录（Id 在 EntityChange 构造器内生成）。
    /// 属性值按 ABP EntityHistoryHelper 的落库形态（JsonSerializer.Serialize）写入，
    /// 保证反序列化路径与生产一致。
    /// </summary>
    private async Task<EntityChange> CreateEntityChangeForUserAsync(
        IdentityUser user,
        EntityChangeType changeType,
        Dictionary<string, (object? Original, object Current)> propertyValues)
    {
        var auditLogId = _guidGenerator.Create();
        var changeInfo = new EntityChangeInfo
        {
            ChangeTime = DateTime.UtcNow,
            ChangeType = changeType,
            EntityId = user.Id.ToString(),
            EntityTypeFullName = "Volo.Abp.Identity.IdentityUser",
            PropertyChanges = propertyValues.Select(kv => new EntityPropertyChangeInfo
            {
                PropertyName = kv.Key,
                PropertyTypeFullName = (kv.Value.Original ?? kv.Value.Current).GetType().FullName!,
                OriginalValue = kv.Value.Original == null ? null : JsonSerializer.Serialize(kv.Value.Original),
                NewValue = JsonSerializer.Serialize(kv.Value.Current)
            }).ToList()
        };
        var change = new EntityChange(_guidGenerator, auditLogId, changeInfo, tenantId: null);

        await CreateAuditLogAsync(
            DateTime.UtcNow,
            $"restore-test/{auditLogId:N}",
            entityChanges: new List<EntityChange> { change });

        return change;
    }
}
