using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AbpAdmin.Settings;
using EasyAbp.Abp.SettingUi;
using Shouldly;
using Volo.Abp.Modularity;
using Volo.Abp.SettingManagement;
using Volo.Abp.Settings;
using Xunit;

namespace AbpAdmin.Notifications;

/// <summary>
/// T3.5 SettingUi 加密设置项脱敏测试（信息泄露风险点的验收）：
/// GET /api/setting-ui 不下发加密项明文；提交空值不清空已保存的密钥。
/// </summary>
public abstract class SettingUiMaskingTests<TStartupModule> : AbpAdminApplicationTestBase<TStartupModule>
    where TStartupModule : IAbpModule
{
    private readonly ISettingUiAppService _settingUiAppService;
    private readonly ISettingManager _settingManager;
    private readonly ISettingProvider _settingProvider;

    protected SettingUiMaskingTests()
    {
        _settingUiAppService = GetRequiredService<ISettingUiAppService>();
        _settingManager = GetRequiredService<ISettingManager>();
        _settingProvider = GetRequiredService<ISettingProvider>();
    }

    [Fact]
    public void SettingUiAppService_Should_Be_Replaced_By_Masked_Implementation()
    {
        // ABP 应用服务经 Castle 动态代理（本例是 IApplicationService 接口代理），
        // 解开代理拿目标对象再判断类型
        var target = _settingUiAppService is Castle.DynamicProxy.IProxyTargetAccessor accessor
            ? accessor.DynProxyGetTarget()
            : _settingUiAppService;
        target.ShouldBeAssignableTo<AbpAdminSettingUiAppService>();
    }

    [Fact]
    public async Task Encrypted_Settings_Should_Not_Be_Returned_In_Plaintext()
    {
        await WithUnitOfWorkAsync(async () =>
        {
            await _settingManager.SetGlobalAsync(AbpAdminSettings.Sms.AliyunAccessKeySecret, "top-secret-value");
            await _settingManager.SetGlobalAsync(AbpAdminSettings.Sms.AliyunAccessKeyId, "plain-akid");
            await _settingManager.SetGlobalAsync("Abp.Mailing.Smtp.Password", "smtp-password-123");
        });

        try
        {
            var groups = await WithUnitOfWorkAsync(async () =>
                await _settingUiAppService.GroupSettingDefinitionsAsync());

            var allInfos = groups.SelectMany(g => g.SettingInfos).ToList();

            // 加密项：值为 null（脱敏）且控件类型为密码框，不是明文
            var secret = allInfos.First(x => x.Name == AbpAdminSettings.Sms.AliyunAccessKeySecret);
            secret.Value.ShouldBeNull();
            secret.Properties["Type"].ShouldBe("password");
            var smtpPassword = allInfos.First(x => x.Name == "Abp.Mailing.Smtp.Password");
            smtpPassword.Value.ShouldBeNull();
            smtpPassword.Properties["Type"].ShouldBe("password");

            // 非加密项：正常下发明文
            var akid = allInfos.First(x => x.Name == AbpAdminSettings.Sms.AliyunAccessKeyId);
            akid.Value.ShouldBe("plain-akid");
        }
        finally
        {
            await WithUnitOfWorkAsync(async () =>
            {
                await _settingManager.SetGlobalAsync(AbpAdminSettings.Sms.AliyunAccessKeySecret, null);
                await _settingManager.SetGlobalAsync(AbpAdminSettings.Sms.AliyunAccessKeyId, null);
                await _settingManager.SetGlobalAsync("Abp.Mailing.Smtp.Password", null);
            });
        }
    }

    [Fact]
    public async Task Empty_Value_Should_Not_Clear_Encrypted_Setting()
    {
        await WithUnitOfWorkAsync(async () =>
            await _settingManager.SetGlobalAsync(AbpAdminSettings.Sms.TencentCloudSecretKey, "existing-secret"));

        try
        {
            // 模拟 SettingUi 保存：整组提交时加密项带的是空值（前端不回显），不应覆盖已保存的密钥
            await WithUnitOfWorkAsync(async () =>
                await _settingUiAppService.SetSettingValuesAsync(new Dictionary<string, string>
                {
                    ["Setting_AbpAdmin_Sms_TencentCloud_SecretKey"] = ""
                }));

            var value = await WithUnitOfWorkAsync(async () =>
                await _settingProvider.GetOrNullAsync(AbpAdminSettings.Sms.TencentCloudSecretKey));
            value.ShouldBe("existing-secret");

            // 非空值正常更新
            await WithUnitOfWorkAsync(async () =>
                await _settingUiAppService.SetSettingValuesAsync(new Dictionary<string, string>
                {
                    ["Setting_AbpAdmin_Sms_TencentCloud_SecretKey"] = "new-secret"
                }));

            value = await WithUnitOfWorkAsync(async () =>
                await _settingProvider.GetOrNullAsync(AbpAdminSettings.Sms.TencentCloudSecretKey));
            value.ShouldBe("new-secret");
        }
        finally
        {
            await WithUnitOfWorkAsync(async () =>
                await _settingManager.SetGlobalAsync(AbpAdminSettings.Sms.TencentCloudSecretKey, null));
        }
    }

    [Fact]
    public async Task Explicit_Reset_Should_Clear_Encrypted_Setting()
    {
        // 曾经的副作用：ResetSettingValuesAsync 走 SetAsync(name, null)，
        // 被"空值不覆盖"守卫拦下 → 加密项永远无法重置清空。现在显式重置绕过守卫。
        await WithUnitOfWorkAsync(async () =>
            await _settingManager.SetGlobalAsync(AbpAdminSettings.Sms.TencentCloudSecretKey, "to-be-reset"));

        try
        {
            await WithUnitOfWorkAsync(async () =>
                await _settingUiAppService.ResetSettingValuesAsync(
                    [AbpAdminSettings.Sms.TencentCloudSecretKey]));

            var value = await WithUnitOfWorkAsync(async () =>
                await _settingProvider.GetOrNullAsync(AbpAdminSettings.Sms.TencentCloudSecretKey));
            value.ShouldBeNull();
        }
        finally
        {
            await WithUnitOfWorkAsync(async () =>
                await _settingManager.SetGlobalAsync(AbpAdminSettings.Sms.TencentCloudSecretKey, null));
        }
    }

    /// <summary>
    /// 操作日志纪律（模块五收口）：Set/Reset 各写一条语义日志，
    /// 只记设置名——密钥值与空值项的键名都不得出现（审计参数已被 IgnoredTypes 关掉，
    /// 这条日志是"谁改了什么"的唯一轨迹，泄露值等于审计脱敏白做）。
    /// 日志经 UoW OnCompleted 在提交后写（SQLite 单写锁，requiresNew 日志 UoW 不能
    /// 在业务事务内开）——WithUnitOfWorkAsync 的 CompleteAsync 会同步触发断言路径。
    /// </summary>
    [Fact]
    public async Task Set_And_Reset_Should_Write_Operation_Log_With_Names_Only()
    {
        // 经接口解析再 cast 回录制器（专用模块里显式注册的映射，见 RecordingOperationLogWriter 注释）
        var recorder = GetRequiredService<AbpAdmin.OperationLogs.IOperationLogWriter>()
            .ShouldBeAssignableTo<RecordingOperationLogWriter>()!;
        recorder.Entries.Clear();

        await WithUnitOfWorkAsync(async () =>
        {
            await _settingUiAppService.SetSettingValuesAsync(new Dictionary<string, string>
            {
                ["Setting_AbpAdmin_Account_PreventEmailEnumeration"] = "true",
                // 空值项（加密项不回显）不应出现在日志的 Extra 里
                ["Setting_AbpAdmin_Sms_Aliyun_AccessKeySecret"] = ""
            });
        });

        await WithUnitOfWorkAsync(async () =>
        {
            await _settingUiAppService.ResetSettingValuesAsync(
                [AbpAdminSettings.Sms.TencentCloudSecretKey]);
        });

        recorder.Entries.Count.ShouldBe(2);

        var setEntry = recorder.Entries[0];
        setEntry.SubType.ShouldBe("保存设置值");
        setEntry.Success.ShouldBeTrue();
        // Extra 记的是还原后的设置名（Abp.X.Y），不是表单键（Setting_Abp_X_Y）
        setEntry.Extra.ShouldContain("AbpAdmin.Account.PreventEmailEnumeration");
        setEntry.Extra.ShouldNotContain("Setting_");
        setEntry.Extra.ShouldNotContain("AbpAdmin.Sms.Aliyun.AccessKeySecret");

        var resetEntry = recorder.Entries[1];
        resetEntry.SubType.ShouldBe("重置设置值");
        resetEntry.Success.ShouldBeTrue();
        resetEntry.Extra.ShouldContain(AbpAdminSettings.Sms.TencentCloudSecretKey);

        // 密钥值永不落库：任何字段都不得含真实提交值
        foreach (var entry in recorder.Entries)
        {
            entry.Extra.ShouldNotContain("new-secret");
            entry.Action.ShouldNotContain("new-secret");
        }
    }
}
