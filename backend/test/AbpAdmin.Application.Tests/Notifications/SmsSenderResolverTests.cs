using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AbpAdmin.RateLimiting;
using AbpAdmin.Settings;
using EasyAbp.Abp.Aliyun.Common;
using EasyAbp.Abp.TencentCloud.Common.Requester;
using EasyAbp.Abp.TencentCloud.Sms.SendSms;
using Shouldly;
using Volo.Abp;
using Volo.Abp.Modularity;
using Volo.Abp.SettingManagement;
using Volo.Abp.Sms;
using Xunit;

namespace AbpAdmin.Notifications;

/// <summary>
/// T3.5 SmsSenderResolver 测试：唯一 ISmsSender 入口、按设置项分发、限流、
/// 两家厂商的消息映射（凭据走加密设置项，请求器是录制替身）。
/// 每个用例用不同的手机号隔离限流计数（同一份进程内分布式缓存）。
/// </summary>
public abstract class SmsSenderResolverTests<TStartupModule> : AbpAdminApplicationTestBase<TStartupModule>
    where TStartupModule : IAbpModule
{
    private readonly ISmsSender _smsSender;
    private readonly ISettingManager _settingManager;
    private readonly RecordingAliyunApiRequester _aliyunRequester;
    private readonly RecordingTencentCloudApiRequester _tencentRequester;

    protected SmsSenderResolverTests()
    {
        _smsSender = GetRequiredService<ISmsSender>();
        _settingManager = GetRequiredService<ISettingManager>();
        _aliyunRequester = (RecordingAliyunApiRequester)GetRequiredService<IAliyunApiRequester>();
        _tencentRequester = (RecordingTencentCloudApiRequester)GetRequiredService<ITencentCloudApiRequester>();
    }

    private async Task SetProviderAsync(string? provider)
    {
        await WithUnitOfWorkAsync(async () =>
            await _settingManager.SetGlobalAsync(AbpAdminSettings.Sms.Provider, provider));
    }

    [Fact]
    public void ISmsSender_Should_Resolve_To_SmsSenderResolver()
    {
        _smsSender.ShouldBeOfType<Sms.SmsSenderResolver>();
    }

    [Fact]
    public async Task Null_Provider_Should_Log_And_Not_Throw()
    {
        // 默认即 Null，显式设回去防止别的用例残留
        await SetProviderAsync(AbpAdmin.Sms.AbpAdminSmsProviders.Null);

        await WithUnitOfWorkAsync(async () =>
            await _smsSender.SendAsync(new SmsMessage("13800000001", "test")));
    }

    [Fact]
    public async Task Aliyun_Without_Credentials_Should_Throw_Clear_Exception()
    {
        await SetProviderAsync(AbpAdmin.Sms.AbpAdminSmsProviders.Aliyun);

        try
        {
            // 带上模板属性，让"凭据未配置"成为第一个失败点（否则先撞模板未配置）
            var exception = await Assert.ThrowsAsync<BusinessException>(async () =>
                await WithUnitOfWorkAsync(async () =>
                {
                    var message = new SmsMessage("13800000002", "{\"code\":\"1\"}");
                    message.Properties[Sms.AliyunSmsSender.SignNamePropertyKey] = "测试签名";
                    message.Properties[Sms.AliyunSmsSender.TemplateCodePropertyKey] = "SMS_999";
                    await _smsSender.SendAsync(message);
                }));

            exception.Code.ShouldBe(AbpAdminDomainErrorCodes.Notifications.SmsAliyunCredentialNotConfigured);
        }
        finally
        {
            await SetProviderAsync(AbpAdmin.Sms.AbpAdminSmsProviders.Null);
        }
    }

    [Fact]
    public async Task Aliyun_Should_Map_Message_To_Request()
    {
        await SetProviderAsync(AbpAdmin.Sms.AbpAdminSmsProviders.Aliyun);
        _aliyunRequester.Reset();

        try
        {
            await WithUnitOfWorkAsync(async () =>
            {
                await _settingManager.SetGlobalAsync(AbpAdminSettings.Sms.AliyunAccessKeyId, "ut-akid");
                await _settingManager.SetGlobalAsync(AbpAdminSettings.Sms.AliyunAccessKeySecret, "ut-secret");

                var message = new SmsMessage("13800000003", "{\"code\":\"654321\"}");
                message.Properties[Sms.AliyunSmsSender.SignNamePropertyKey] = "测试签名";
                message.Properties[Sms.AliyunSmsSender.TemplateCodePropertyKey] = "SMS_999";
                await _smsSender.SendAsync(message);
            });

            var (request, url) = _aliyunRequester.Calls.ShouldHaveSingleItem();
            url.ShouldContain("dysmsapi");
            request.RequestParameters["PhoneNumbers"].ShouldBe("13800000003");
            request.RequestParameters["SignName"].ShouldBe("测试签名");
            request.RequestParameters["TemplateCode"].ShouldBe("SMS_999");
            request.RequestParameters["TemplateParam"].ShouldBe("{\"code\":\"654321\"}");
        }
        finally
        {
            await WithUnitOfWorkAsync(async () =>
            {
                await _settingManager.SetGlobalAsync(AbpAdminSettings.Sms.AliyunAccessKeyId, null);
                await _settingManager.SetGlobalAsync(AbpAdminSettings.Sms.AliyunAccessKeySecret, null);
            });
            await SetProviderAsync(AbpAdmin.Sms.AbpAdminSmsProviders.Null);
        }
    }

    [Fact]
    public async Task TencentCloud_Should_Map_Message_And_Read_Encrypted_Settings()
    {
        await SetProviderAsync(AbpAdmin.Sms.AbpAdminSmsProviders.TencentCloud);
        _tencentRequester.Reset();

        try
        {
            await WithUnitOfWorkAsync(async () =>
            {
                await _settingManager.SetGlobalAsync(AbpAdminSettings.Sms.TencentCloudSecretId, "ut-sid");
                await _settingManager.SetGlobalAsync(AbpAdminSettings.Sms.TencentCloudSecretKey, "ut-skey");
                await _settingManager.SetGlobalAsync(AbpAdminSettings.Sms.TencentCloudSmsSdkAppid, "1400123456");
                await _settingManager.SetGlobalAsync(AbpAdminSettings.Sms.TencentCloudSign, "测试签名");

                var message = new SmsMessage("13800000004", "{\"code\":\"1\"}");
                message.Properties[Sms.AbpAdminTencentCloudSmsSender.TemplateIdPropertyKey] = "888888";
                message.Properties[Sms.AbpAdminTencentCloudSmsSender.TemplateParamSetPropertyKey] = new[] { "654321" };
                await _smsSender.SendAsync(message);
            });

            var (request, endPoint, options) = _tencentRequester.Calls.ShouldHaveSingleItem();
            var sendRequest = request.ShouldBeOfType<SendSmsRequest>();
            endPoint.ShouldBe("sms.tencentcloudapi.com");
            options.ShouldNotBeNull();
            options!.SecretId.ShouldBe("ut-sid");
            options.SecretKey.ShouldBe("ut-skey");
            // 11 位 1 开头手机号补 +86；请求体是 JSON（SetRequestBody 序列化进 HttpRequestMessage.Content）
            var body = await sendRequest.HttpRequestMessage.Content!.ReadAsStringAsync();
            body.ShouldContain("+8613800000004");
            body.ShouldContain("888888");
            body.ShouldContain("654321");
        }
        finally
        {
            await WithUnitOfWorkAsync(async () =>
            {
                await _settingManager.SetGlobalAsync(AbpAdminSettings.Sms.TencentCloudSecretId, null);
                await _settingManager.SetGlobalAsync(AbpAdminSettings.Sms.TencentCloudSecretKey, null);
                await _settingManager.SetGlobalAsync(AbpAdminSettings.Sms.TencentCloudSmsSdkAppid, null);
                await _settingManager.SetGlobalAsync(AbpAdminSettings.Sms.TencentCloudSign, null);
            });
            await SetProviderAsync(AbpAdmin.Sms.AbpAdminSmsProviders.Null);
        }
    }

    [Fact]
    public async Task Rate_Limit_Should_Throw_After_Threshold()
    {
        await SetProviderAsync(AbpAdmin.Sms.AbpAdminSmsProviders.Null);
        // 验证码策略：同一手机号 1 小时 3 次（T2.5 SmsVerificationCode）。
        // 消息必须带 Purpose=VerificationCode 标记（VerificationCodeSmsMessageFactory 发的都带），
        // 未打标的消息走 SmsNotification 策略（阈值独立配置，见 SmsRateLimitingPolicies）。
        var phone = "138" + Random.Shared.Next(10000000, 99999999);

        for (var i = 0; i < 3; i++)
        {
            await WithUnitOfWorkAsync(async () =>
                await _smsSender.SendAsync(CreateVerificationCodeMessage(phone)));
        }

        await Assert.ThrowsAsync<AbpAdminOperationRateLimitingException>(async () =>
            await WithUnitOfWorkAsync(async () =>
                await _smsSender.SendAsync(CreateVerificationCodeMessage(phone))));
    }

    [Fact]
    public async Task Rate_Limit_Should_Separate_VerificationCode_And_Notification_Buckets()
    {
        await SetProviderAsync(AbpAdmin.Sms.AbpAdminSmsProviders.Null);
        var phone = "139" + Random.Shared.Next(10000000, 99999999);

        // 验证码桶打满（3 条后第 4 条 429）
        for (var i = 0; i < 3; i++)
        {
            await WithUnitOfWorkAsync(async () =>
                await _smsSender.SendAsync(CreateVerificationCodeMessage(phone)));
        }
        await Assert.ThrowsAsync<AbpAdminOperationRateLimitingException>(async () =>
            await WithUnitOfWorkAsync(async () =>
                await _smsSender.SendAsync(CreateVerificationCodeMessage(phone))));

        // 未打标的消息（运营通知）走独立桶，不受验证码阈值影响
        await WithUnitOfWorkAsync(async () =>
            await _smsSender.SendAsync(new SmsMessage(phone, "notification")));
    }

    private static SmsMessage CreateVerificationCodeMessage(string phoneNumber)
    {
        var message = new SmsMessage(phoneNumber, "test");
        message.Properties[Sms.SmsMessagePropertyKeys.Purpose] =
            Sms.SmsMessagePropertyKeys.PurposeVerificationCode;
        return message;
    }
}
