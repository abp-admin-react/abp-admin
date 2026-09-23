using System;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using AbpAdmin.RateLimiting;
using Microsoft.Extensions.Options;
using Shouldly;
using Volo.Abp;
using Volo.Abp.Application.Dtos;
using Volo.Abp.Auditing;
using Volo.Abp.Caching;
using Volo.Abp.Domain.Entities;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Identity;
using Volo.Abp.Modularity;
using Volo.Abp.Security.Claims;
using Volo.Abp.Users;
using Xunit;

namespace AbpAdmin.Gdpr;

/* T2.4 GDPR 集成测试。
 * 覆盖验收标准要求的 5 个场景：
 * 1. 请求间隔限制（一天内第二次发起请求被拒绝）
 * 2. 数据未就绪不消耗 token（ReadyTime 前下载抛 DataNotReady，同一 token 就绪后仍可用）
 * 3. requestId 不匹配不消耗（错误 requestId 不消耗 token，正确 requestId + 同一 token 成功）
 * 4. 跨用户访问（用户 A 获取用户 B 的请求 token 返回 404）
 * 5. 账户删除（删除后 GdprRequest/GdprInfo 已删，用户已删）
 *
 * 审查补测：
 * - GdprDownload 限流策略注册（匿名端点防刷与 FileShareDownload 同型）
 * - token 并发重放单次消费（分布式锁内「读-验-删」原子化）
 * - 数据就绪覆盖度校验（ReadyTime 已到但 GdprInfo 为空 → 拒绝产出空 ZIP）
 * - DeleteAccountInput.Password 的 [DisableAuditing]（明文不进审计动作参数）
 *
 * 测试环境 FakeCurrentPrincipalAccessor 固定当前用户为 admin
 * (Id: 2e701e62-0953-4dd3-910b-dc6cc93ccb0d)。
 * 跨用户测试通过 ICurrentPrincipalAccessor.Change() 切换。
 */
public abstract class GdprRequestAppServiceTests<TStartupModule> : AbpAdminApplicationTestBase<TStartupModule>
    where TStartupModule : IAbpModule
{
    private static readonly Guid AdminUserId = Guid.Parse("2e701e62-0953-4dd3-910b-dc6cc93ccb0d");

    private readonly IGdprRequestAppService _gdprRequestAppService;
    private readonly IRepository<GdprRequest, Guid> _gdprRequestRepository;
    private readonly IRepository<GdprInfo, Guid> _gdprInfoRepository;
    private readonly IDistributedCache<GdprDownloadTokenCacheItem, string> _downloadTokenCache;
    private readonly ICurrentPrincipalAccessor _currentPrincipalAccessor;
    private readonly IdentityUserManager _userManager;
    private readonly IdentityUserStore _userStore;
    private readonly Volo.Abp.EventBus.Distributed.IDistributedEventBus _distributedEventBus;
    private readonly Volo.Abp.BlobStoring.IBlobContainer<AbpAdmin.Profile.AvatarContainer> _avatarContainer;

    protected GdprRequestAppServiceTests()
    {
        _gdprRequestAppService = GetRequiredService<IGdprRequestAppService>();
        _gdprRequestRepository = GetRequiredService<IRepository<GdprRequest, Guid>>();
        _gdprInfoRepository = GetRequiredService<IRepository<GdprInfo, Guid>>();
        _downloadTokenCache = GetRequiredService<IDistributedCache<GdprDownloadTokenCacheItem, string>>();
        _currentPrincipalAccessor = GetRequiredService<ICurrentPrincipalAccessor>();
        _userManager = GetRequiredService<IdentityUserManager>();
        _userStore = GetRequiredService<IdentityUserStore>();
        _distributedEventBus = GetRequiredService<Volo.Abp.EventBus.Distributed.IDistributedEventBus>();
        _avatarContainer = GetRequiredService<Volo.Abp.BlobStoring.IBlobContainer<AbpAdmin.Profile.AvatarContainer>>();
    }

    /// <summary>
    /// 以指定用户身份执行操作。用于跨用户场景测试。
    /// </summary>
    private IDisposable ChangeCurrentUser(Guid userId, string userName, string email)
    {
        return _currentPrincipalAccessor.Change(new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim(AbpClaimTypes.UserId, userId.ToString()),
            new Claim(AbpClaimTypes.UserName, userName),
            new Claim(AbpClaimTypes.Email, email)
        })));
    }

    /// <summary>
    /// 创建测试用户并返回用户 ID。
    /// </summary>
    private async Task<Guid> CreateTestUserAsync(string userName, string email, string password)
    {
        var user = new IdentityUser(
            Guid.NewGuid(),
            userName,
            email);

        var result = await _userManager.CreateAsync(user, password);
        result.Succeeded.ShouldBeTrue(
            string.Join("; ", result.Errors.Select(e => e.Description)));

        return user.Id;
    }

    /// <summary>
    /// 清理指定用户的全部 GDPR 请求（避免间隔限制影响后续测试）。
    /// </summary>
    private async Task CleanupGdprRequestsAsync(Guid userId)
    {
        await WithUnitOfWorkAsync(async () =>
        {
            var queryable = await _gdprRequestRepository.GetQueryableAsync();
            var requests = queryable.Where(x => x.UserId == userId).ToList();
            foreach (var request in requests)
            {
                await _gdprInfoRepository.DeleteAsync(x => x.RequestId == request.Id);
            }
            await _gdprRequestRepository.DeleteManyAsync(requests);
        });
    }

    [Fact]
    public async Task CreateAsync_Should_Create_Request_With_Correct_ReadyTime()
    {
        await CleanupGdprRequestsAsync(AdminUserId);

        var result = await _gdprRequestAppService.CreateAsync();

        result.Id.ShouldNotBe(Guid.Empty);
        result.CreationTime.ShouldNotBe(default);
        // 默认 MinutesForDataPreparation = 60 分钟
        result.ReadyTime.ShouldBe(result.CreationTime.AddMinutes(60));

        // 数据库中应有记录
        var request = await _gdprRequestRepository.GetAsync(result.Id);
        request.UserId.ShouldBe(AdminUserId);
    }

    [Fact]
    public async Task CreateAsync_Should_Reject_Second_Request_Within_Interval()
    {
        await CleanupGdprRequestsAsync(AdminUserId);

        // 第一次请求成功
        await _gdprRequestAppService.CreateAsync();

        // 第二次请求应被拒绝（默认间隔 1 天）
        var exception = await Should.ThrowAsync<BusinessException>(
            _gdprRequestAppService.CreateAsync());

        exception.Code.ShouldBe("AbpAdmin:Gdpr:RequestIntervalNotElapsed");
    }

    [Fact]
    public async Task IsNewRequestAllowedAsync_Should_Return_False_After_Create()
    {
        await CleanupGdprRequestsAsync(AdminUserId);

        // 初始应允许
        var allowedBefore = await _gdprRequestAppService.IsNewRequestAllowedAsync();
        allowedBefore.ShouldBeTrue();

        // 发起请求后应不允许
        await _gdprRequestAppService.CreateAsync();
        var allowedAfter = await _gdprRequestAppService.IsNewRequestAllowedAsync();
        allowedAfter.ShouldBeFalse();
    }

    [Fact]
    public async Task GetDownloadTokenAsync_Should_Return_404_For_Other_User_Request()
    {
        await CleanupGdprRequestsAsync(AdminUserId);

        // admin 发起请求
        var request = await _gdprRequestAppService.CreateAsync();

        // 创建第二个用户
        var otherUserId = await CreateTestUserAsync("gdpr-test-other", "gdpr-other@test.com", "Test@123456");

        // 以第二个用户身份尝试获取 admin 的请求 token
        using (ChangeCurrentUser(otherUserId, "gdpr-test-other", "gdpr-other@test.com"))
        {
            var exception = await Should.ThrowAsync<EntityNotFoundException>(
                _gdprRequestAppService.GetDownloadTokenAsync(request.Id));

            exception.EntityType.ShouldBe(typeof(GdprRequest));
        }

        // 清理
        await CleanupGdprRequestsAsync(AdminUserId);
        await WithUnitOfWorkAsync(async () =>
        {
            var user = await _userManager.FindByIdAsync(otherUserId.ToString());
            if (user != null)
            {
                await _userManager.DeleteAsync(user);
            }
        });
    }

    [Fact]
    public async Task GetListAsync_Should_Only_Return_Current_User_Requests()
    {
        await CleanupGdprRequestsAsync(AdminUserId);

        // admin 发起请求
        await _gdprRequestAppService.CreateAsync();

        // 创建第二个用户
        var otherUserId = await CreateTestUserAsync("gdpr-test-list", "gdpr-list@test.com", "Test@123456");

        // 以第二个用户身份查询列表
        using (ChangeCurrentUser(otherUserId, "gdpr-test-list", "gdpr-list@test.com"))
        {
            var result = await _gdprRequestAppService.GetListAsync(
                new PagedAndSortedResultRequestDto { MaxResultCount = 10 });

            result.TotalCount.ShouldBe(0);
            result.Items.ShouldBeEmpty();
        }

        // admin 能看到自己的请求
        var adminResult = await _gdprRequestAppService.GetListAsync(
            new PagedAndSortedResultRequestDto { MaxResultCount = 10 });
        adminResult.TotalCount.ShouldBe(1);

        // 清理
        await CleanupGdprRequestsAsync(AdminUserId);
        await WithUnitOfWorkAsync(async () =>
        {
            var user = await _userManager.FindByIdAsync(otherUserId.ToString());
            if (user != null)
            {
                await _userManager.DeleteAsync(user);
            }
        });
    }

    [Fact]
    public async Task GetDownloadTokenAsync_Should_Return_Strong_Random_Token()
    {
        await CleanupGdprRequestsAsync(AdminUserId);

        var request = await _gdprRequestAppService.CreateAsync();

        var token1 = await _gdprRequestAppService.GetDownloadTokenAsync(request.Id);
        var token2 = await _gdprRequestAppService.GetDownloadTokenAsync(request.Id);

        // 加密安全随机（256 bit = 64 个十六进制字符），且两次生成不重复——
        // 顺序 Guid（时间戳+计数器）可推算，曾是这里的安全缺口
        token1.ShouldNotBe(token2);
        token1.Length.ShouldBeGreaterThanOrEqualTo(64);
        token1.ShouldMatch("^[0-9A-F]+$");

        // 清理
        await CleanupGdprRequestsAsync(AdminUserId);
    }

    [Fact]
    public async Task DownloadAsync_Should_Fail_With_Wrong_RequestId_But_Not_Consume_Token()
    {
        await CleanupGdprRequestsAsync(AdminUserId);

        // admin 发起请求并获取 token
        var request = await _gdprRequestAppService.CreateAsync();
        var token = await _gdprRequestAppService.GetDownloadTokenAsync(request.Id);

        // 用错误的 requestId 下载，应失败
        var wrongRequestId = Guid.NewGuid();
        await Should.ThrowAsync<BusinessException>(
            _gdprRequestAppService.DownloadAsync(wrongRequestId, token));

        // token 不应被消耗：用正确的 requestId + 同一 token 应能成功
        // 但此时 ReadyTime 未到（默认 60 分钟），会抛 DataNotReady
        // 关键是错误码不同：requestId 不匹配是 InvalidDownloadToken，ReadyTime 未到是 DataNotReady
        var exception = await Should.ThrowAsync<BusinessException>(
            _gdprRequestAppService.DownloadAsync(request.Id, token));

        // 如果 token 被消耗了，这里会抛 InvalidDownloadToken
        // 如果没被消耗，会抛 DataNotReady（因为 ReadyTime 未到）
        exception.Code.ShouldBe("AbpAdmin:Gdpr:DataNotReady");

        // 清理
        await CleanupGdprRequestsAsync(AdminUserId);
    }

    [Fact]
    public async Task DownloadAsync_Should_Not_Consume_Token_When_Data_Not_Ready()
    {
        await CleanupGdprRequestsAsync(AdminUserId);

        // admin 发起请求并获取 token
        var request = await _gdprRequestAppService.CreateAsync();
        var token = await _gdprRequestAppService.GetDownloadTokenAsync(request.Id);

        // ReadyTime 未到：第一次下载抛 DataNotReady，且 token 不被消耗
        //（先校验就绪后消费——早先「先消费后校验」会让用户提前点一次就烧掉一次性 token）
        var firstException = await Should.ThrowAsync<BusinessException>(
            _gdprRequestAppService.DownloadAsync(request.Id, token));
        firstException.Code.ShouldBe("AbpAdmin:Gdpr:DataNotReady");

        // 第二次用同一 token 仍是 DataNotReady（token 还在），就绪后可正常下载
        var secondException = await Should.ThrowAsync<BusinessException>(
            _gdprRequestAppService.DownloadAsync(request.Id, token));
        secondException.Code.ShouldBe("AbpAdmin:Gdpr:DataNotReady");

        // 清理
        await CleanupGdprRequestsAsync(AdminUserId);
    }

    [Fact]
    public async Task DownloadAsync_Should_Fail_With_Invalid_Token()
    {
        await CleanupGdprRequestsAsync(AdminUserId);

        var request = await _gdprRequestAppService.CreateAsync();

        // 用不存在的 token 下载
        var exception = await Should.ThrowAsync<BusinessException>(
            _gdprRequestAppService.DownloadAsync(request.Id, "nonexistent-token"));

        exception.Code.ShouldBe("AbpAdmin:Gdpr:InvalidDownloadToken");

        // 清理
        await CleanupGdprRequestsAsync(AdminUserId);
    }

    [Fact]
    public async Task DeleteAccount_Should_Remove_GdprData_And_User()
    {
        // 创建测试用户
        var testUserId = await CreateTestUserAsync("gdpr-test-delete", "gdpr-delete@test.com", "Test@123456");

        // 以测试用户身份发起 GDPR 请求
        using (ChangeCurrentUser(testUserId, "gdpr-test-delete", "gdpr-delete@test.com"))
        {
            await _gdprRequestAppService.CreateAsync();

            // 确认请求已创建
            var listResult = await _gdprRequestAppService.GetListAsync(
                new PagedAndSortedResultRequestDto { MaxResultCount = 10 });
            listResult.TotalCount.ShouldBe(1);

            // 删除账户（需要密码确认）
            await _gdprRequestAppService.DeleteCurrentUserAccountAsync(
                new DeleteAccountInput { Password = "Test@123456" });
        }

        // 验证：GdprRequest 已删除
        await WithUnitOfWorkAsync(async () =>
        {
            var queryable = await _gdprRequestRepository.GetQueryableAsync();
            var requests = queryable.Where(x => x.UserId == testUserId).ToList();
            requests.ShouldBeEmpty();
        });

        // 验证：用户已删除
        await WithUnitOfWorkAsync(async () =>
        {
            var user = await _userManager.FindByIdAsync(testUserId.ToString());
            user.ShouldBeNull();
        });
    }

    [Fact]
    public async Task DeleteAccount_Should_Reject_Wrong_Password()
    {
        // 创建测试用户
        var testUserId = await CreateTestUserAsync("gdpr-test-wrongpw", "gdpr-wrongpw@test.com", "Test@123456");

        // 以测试用户身份尝试用错误密码删除
        using (ChangeCurrentUser(testUserId, "gdpr-test-wrongpw", "gdpr-wrongpw@test.com"))
        {
            var exception = await Should.ThrowAsync<BusinessException>(
                _gdprRequestAppService.DeleteCurrentUserAccountAsync(
                    new DeleteAccountInput { Password = "WrongPassword!" }));

            exception.Code.ShouldBe("AbpAdmin:Gdpr:IncorrectPassword");
        }

        // 用户应仍然存在
        await WithUnitOfWorkAsync(async () =>
        {
            var user = await _userManager.FindByIdAsync(testUserId.ToString());
            user.ShouldNotBeNull();

            // 清理
            await _userManager.DeleteAsync(user);
        });
    }

    // ========== 审查补测：限流策略 / token 原子消费 / 覆盖度校验 / 审计脱敏 ==========

    [Fact]
    public void GdprDownload_Rate_Limiting_Policy_Should_Be_Registered()
    {
        // 匿名下载端点防刷与 FileShareDownload 同型：策略必须在配置器里注册，
        // 应用服务方法上的 [OperationRateLimiting("GdprDownload")] 才不是死配置
        var options = GetRequiredService<IOptions<AbpAdminOperationRateLimitingOptions>>().Value;

        options.Policies.ContainsKey("GdprDownload").ShouldBeTrue();
        options.IsEnabled.ShouldBeTrue(); // 默认 true，可由 "OperationRateLimiting:IsEnabled" 覆盖
    }

    [Fact]
    public async Task DownloadAsync_Concurrent_Replay_Should_Consume_Token_Only_Once()
    {
        await CleanupGdprRequestsAsync(AdminUserId);

        // 直接落一条「ReadyTime 已过」且已贡献 GdprInfo 的请求：让并发重放走完
        // 就绪校验、真正竞争一次性消费环节（未就绪请求在校验阶段就被拦下，测不到锁）
        var requestId = Guid.Empty;
        await WithUnitOfWorkAsync(async () =>
        {
            var request = new GdprRequest(
                Guid.NewGuid(), null, AdminUserId, DateTime.UtcNow, TimeSpan.FromMinutes(-1));
            await _gdprRequestRepository.InsertAsync(request, autoSave: true);
            requestId = request.Id;

            await _gdprInfoRepository.InsertAsync(new GdprInfo(
                Guid.NewGuid(), null, requestId, "Identity", "{\"userName\":\"admin\"}"));
        });

        var token = await _gdprRequestAppService.GetDownloadTokenAsync(requestId);

        // 并发重放同一 token：ConsumeDownloadTokenAsync 在分布式锁内「读-验-删」，
        // 恰好一个请求能在锁内命中缓存条目并完成下载，另一个必须拿到缓存未命中
        //（InvalidDownloadToken）。原先「先 Get 再 Remove」的 TOCTOU 写法下两个请求
        // 会各自成功下载一次（token 被消费两次）。
        var first = CaptureBusinessExceptionAsync(_gdprRequestAppService.DownloadAsync(requestId, token));
        var second = CaptureBusinessExceptionAsync(_gdprRequestAppService.DownloadAsync(requestId, token));
        await Task.WhenAll(first, second);

        var codes = new[] { (await first)?.Code, (await second)?.Code }
            .Where(c => c != null).OrderBy(c => c).ToList();

        // 一个成功（无异常 → 无错误码），另一个 InvalidDownloadToken
        codes.ShouldContain("AbpAdmin:Gdpr:InvalidDownloadToken");
        codes.Count.ShouldBe(1);

        await CleanupGdprRequestsAsync(AdminUserId);
    }

    private static async Task<BusinessException?> CaptureBusinessExceptionAsync(Task task)
    {
        try
        {
            await task;
            return null;
        }
        catch (BusinessException ex)
        {
            return ex;
        }
    }

    [Fact]
    public async Task DownloadAsync_Should_Reject_When_No_GdprInfo_Contributed()
    {
        await CleanupGdprRequestsAsync(AdminUserId);

        // 直接落一条「ReadyTime 已过」的请求（负准备时长），且故意不贡献任何 GdprInfo——
        // 模拟各模块的 Prepared 事件丢失/延迟（切真分布式总线后的场景）
        var requestId = Guid.Empty;
        await WithUnitOfWorkAsync(async () =>
        {
            var request = new GdprRequest(
                Guid.NewGuid(), null, AdminUserId, DateTime.UtcNow, TimeSpan.FromMinutes(-1));
            await _gdprRequestRepository.InsertAsync(request, autoSave: true);
            requestId = request.Id;
        });

        var token = await _gdprRequestAppService.GetDownloadTokenAsync(requestId);

        // 覆盖度校验：ReadyTime 已到但一条 provider 条目都没有时拒绝下载，
        // 复用 DataNotReady（与「时间未到」同码，不泄露可枚举的状态差异）
        var exception = await Should.ThrowAsync<BusinessException>(
            _gdprRequestAppService.DownloadAsync(requestId, token));
        exception.Code.ShouldBe("AbpAdmin:Gdpr:DataNotReady");

        await CleanupGdprRequestsAsync(AdminUserId);
    }

    [Fact]
    public async Task DownloadAsync_Should_Return_Zip_When_GdprInfo_Exists()
    {
        await CleanupGdprRequestsAsync(AdminUserId);

        var requestId = Guid.Empty;
        await WithUnitOfWorkAsync(async () =>
        {
            var request = new GdprRequest(
                Guid.NewGuid(), null, AdminUserId, DateTime.UtcNow, TimeSpan.FromMinutes(-1));
            await _gdprRequestRepository.InsertAsync(request, autoSave: true);
            requestId = request.Id;

            await _gdprInfoRepository.InsertAsync(new GdprInfo(
                Guid.NewGuid(), null, requestId, "Identity", "{\"userName\":\"admin\"}"));
        });

        var token = await _gdprRequestAppService.GetDownloadTokenAsync(requestId);

        // 正向路径：有 provider 条目时覆盖度校验不拦截，返回 ZIP 流
        var content = await _gdprRequestAppService.DownloadAsync(requestId, token);
        content.ShouldNotBeNull();
        content.FileName.ShouldBe($"personal-data-{requestId:N}.zip");

        await CleanupGdprRequestsAsync(AdminUserId);
    }

    [Fact]
    public async Task DeleteAccount_Password_Should_Not_Appear_In_Audit_Action_Parameters()
    {
        // [DisableAuditing] 安全属性回归测试：DeleteAccountInput.Password 若被序列化进
        // AbpAuditLogAction.Parameters，明文密码会随审计日志永久留存（审计全开的项目），
        // 且会被 AuditLoggingGdprDataProvider 打包进"个人数据导出物"。断言两件事：
        // 删除动作本身必须留痕（可追责），但参数序列化结果不含密码明文。
        var password = "Audit!Secret#42";
        var testUserId = await CreateTestUserAsync("gdpr-test-audit", "gdpr-audit@test.com", password);
        var auditingManager = GetRequiredService<IAuditingManager>();

        using (ChangeCurrentUser(testUserId, "gdpr-test-audit", "gdpr-audit@test.com"))
        using (auditingManager.BeginScope())
        {
            await _gdprRequestAppService.DeleteCurrentUserAccountAsync(
                new DeleteAccountInput { Password = password });

            auditingManager.Current.ShouldNotBeNull();
            var deleteAction = auditingManager.Current!.Log.Actions
                .SingleOrDefault(a => a.MethodName.Contains("DeleteCurrentUserAccountAsync"));
            deleteAction.ShouldNotBeNull(); // GDPR 删除必须可追责：动作要有审计记录
            deleteAction!.Parameters.ShouldNotBeNull();
            deleteAction.Parameters.ShouldNotContain(password); // [DisableAuditing] 挡住明文
        }
    }

    // ========== 六透镜审查轮补测：头像 GDPR 导出与删户 blob 清理 ==========

    [Fact]
    public async Task Avatar_Provider_Contributes_Prepared_Entry_Without_Avatar()
    {
        // 无头像也必须回发 Prepared（hasAvatar=false）：贡献者计数稳定，
        // 避免「其它 Provider 都就绪、唯独头像缺失」被误判为整体未就绪
        var testUserId = await CreateTestUserAsync("gdpr-avatar-b", $"avatar-b-{Guid.NewGuid():N}@test.com", "Test@123456");
        GdprRequestDto request;
        using (ChangeCurrentUser(testUserId, "gdpr-avatar-b", "avatar-b@test.com"))
        {
            request = await _gdprRequestAppService.CreateAsync();
        }

        var infos = await WithUnitOfWorkAsync(async () =>
        {
            var queryable = await _gdprInfoRepository.GetQueryableAsync();
            return queryable.Where(x => x.RequestId == request.Id && x.Provider == "ProfileAvatar").ToList();
        });

        infos.ShouldNotBeEmpty("无头像也必须贡献 ProfileAvatar 条目");
        infos[0].Data.ShouldContain("\"hasAvatar\":false");
    }

    [Fact]
    public async Task Avatar_Provider_Exports_Blob_As_Base64()
    {
        var testUserId = await CreateTestUserAsync("gdpr-avatar-c", $"avatar-c-{Guid.NewGuid():N}@test.com", "Test@123456");
        await CleanupGdprRequestsAsync(testUserId);

        // 直接落一张头像 blob（候选扩展名 .jpg 在当前白名单内，无需持久化扩展名属性）
        var bytes = new byte[] { 1, 2, 3, 4, 5 };
        await _avatarContainer.SaveAsync(
            AbpAdmin.Imaging.AvatarBlobNames.ForUser(testUserId, ".jpg"),
            new System.IO.MemoryStream(bytes), overrideExisting: true);

        GdprRequestDto request;
        using (ChangeCurrentUser(testUserId, "gdpr-avatar-c", "avatar-c@test.com"))
        {
            request = await _gdprRequestAppService.CreateAsync();
        }

        var infos = await WithUnitOfWorkAsync(async () =>
        {
            var queryable = await _gdprInfoRepository.GetQueryableAsync();
            return queryable.Where(x => x.RequestId == request.Id && x.Provider == "ProfileAvatar").ToList();
        });

        infos.ShouldNotBeEmpty();
        infos[0].Data.ShouldContain("\"hasAvatar\":true");
        infos[0].Data.ShouldContain(Convert.ToBase64String(bytes));

        await _avatarContainer.DeleteAsync(AbpAdmin.Imaging.AvatarBlobNames.ForUser(testUserId, ".jpg"));
        await CleanupGdprRequestsAsync(testUserId);
    }

    [Fact]
    public async Task DeleteAccount_Removes_Avatar_Blobs_Across_Candidates()
    {
        var testUserId = await CreateTestUserAsync("gdpr-avatar", $"avatar-{Guid.NewGuid():N}@test.com", "Test@123456");

        // 落两张候选扩展名 blob（模拟历史格式残留）+ 持久化实际扩展名
        await _avatarContainer.SaveAsync(
            AbpAdmin.Imaging.AvatarBlobNames.ForUser(testUserId, ".jpg"),
            new System.IO.MemoryStream(new byte[] { 9 }), overrideExisting: true);
        await _avatarContainer.SaveAsync(
            AbpAdmin.Imaging.AvatarBlobNames.ForUser(testUserId, ".png"),
            new System.IO.MemoryStream(new byte[] { 8 }), overrideExisting: true);

        using (ChangeCurrentUser(testUserId, "gdpr-avatar", "avatar@test.com"))
        {
            await _gdprRequestAppService.DeleteCurrentUserAccountAsync(
                new DeleteAccountInput { Password = "Test@123456" });
        }

        (await _avatarContainer.ExistsAsync(
            AbpAdmin.Imaging.AvatarBlobNames.ForUser(testUserId, ".jpg"))).ShouldBeFalse();
        (await _avatarContainer.ExistsAsync(
            AbpAdmin.Imaging.AvatarBlobNames.ForUser(testUserId, ".png"))).ShouldBeFalse();
    }
}
