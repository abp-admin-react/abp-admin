using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using EasyAbp.Abp.Aliyun.Common;
using EasyAbp.Abp.Aliyun.Common.Model;
using EasyAbp.Abp.TencentCloud.Common;
using EasyAbp.Abp.TencentCloud.Common.Requester;
using EasyAbp.Abp.TencentCloud.Sms.SendSms;
using AliyunRequest = EasyAbp.Abp.Aliyun.Common.Model.ICommonRequest;
using AliyunResponse = EasyAbp.Abp.Aliyun.Common.Model.ICommonResponse;
using TencentRequest = EasyAbp.Abp.TencentCloud.Common.Models.ICommonRequest;
using TencentResponse = EasyAbp.Abp.TencentCloud.Common.Models.ICommonResponse;

namespace AbpAdmin.Notifications;

/// <summary>
/// 阿里云 API 请求器的录制替身（T3.5 测试）：替代 SettingBasedAliyunApiRequester，
/// 避免真实外呼；记录请求参数供断言，固定返回 Code = "OK"。
/// </summary>
public class RecordingAliyunApiRequester : IAliyunApiRequester
{
    public List<(AliyunRequest Request, string Url)> Calls { get; } = new();

    /// <summary>下一次调用返回的 Code（默认 "OK" 成功；测试可改成失败码）。</summary>
    public string NextResponseCode { get; set; } = "OK";

    public Task<TResponse> SendRequestAsync<TResponse>(AliyunRequest request, string url)
        where TResponse : AliyunResponse
    {
        Calls.Add((request, url));

        var response = Activator.CreateInstance<TResponse>();
        if (response is CommonResponse commonResponse)
        {
            commonResponse.Code = NextResponseCode;
            commonResponse.Message = NextResponseCode == "OK" ? "OK" : "mock failure";
        }

        return Task.FromResult(response);
    }

    public void Reset()
    {
        Calls.Clear();
        NextResponseCode = "OK";
    }
}

/// <summary>
/// 腾讯云 API 请求器的录制替身（T3.5 测试）：记录请求与凭据，固定返回 SendStatusSet[0].Code = "Ok"。
/// </summary>
public class RecordingTencentCloudApiRequester : ITencentCloudApiRequester
{
    public List<(TencentRequest Request, string? EndPoint, AbpTencentCloudCommonOptions? Options)> Calls { get; } = new();

    public string NextStatusCode { get; set; } = "Ok";

    public Task<TResponse> SendRequestAsync<TResponse>(TencentRequest request, string endpoint)
        where TResponse : TencentResponse
    {
        return SendRequestAsync<TResponse>(request, endpoint, null!);
    }

    public Task<TResponse> SendRequestAsync<TResponse>(TencentRequest request, string endpoint, AbpTencentCloudCommonOptions options)
        where TResponse : TencentResponse
    {
        Calls.Add((request, endpoint, options));

        var response = Activator.CreateInstance<TResponse>();
        if (response is SendSmsResponse sendSmsResponse)
        {
            sendSmsResponse.SendStatusSet = new List<SendStatusSet>
            {
                new() { Code = NextStatusCode, Message = NextStatusCode }
            };
        }

        return Task.FromResult(response);
    }

    public void Reset()
    {
        Calls.Clear();
        NextStatusCode = "Ok";
    }
}
