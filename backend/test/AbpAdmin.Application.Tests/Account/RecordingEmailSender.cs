using System.Collections.Generic;
using System.Net.Mail;
using System.Threading.Tasks;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Emailing;

namespace AbpAdmin.Account;

/// <summary>
/// 测试用 IEmailSender 替身：不真正发信，把「发送请求」记录下来供断言。
/// 防邮箱枚举的测试依赖它证明「对未知邮箱没有调用邮件服务」。
/// </summary>
public class RecordingEmailSender : IEmailSender, ISingletonDependency
{
    public List<(string To, string? Subject, string? Body)> SentMessages { get; } = new();

    public void Clear() => SentMessages.Clear();

    public Task SendAsync(string to, string? subject, string? body, bool isBodyHtml = true, AdditionalEmailSendingArgs? additionalEmailSendingArgs = null)
    {
        SentMessages.Add((to, subject, body));
        return Task.CompletedTask;
    }

    public Task SendAsync(string from, string to, string? subject, string? body, bool isBodyHtml = true, AdditionalEmailSendingArgs? additionalEmailSendingArgs = null)
    {
        SentMessages.Add((to, subject, body));
        return Task.CompletedTask;
    }

    public Task SendAsync(MailMessage mail, bool normalize = true)
    {
        SentMessages.Add((mail.To.ToString(), mail.Subject, mail.Body));
        return Task.CompletedTask;
    }

    public Task QueueAsync(string to, string subject, string body, bool isBodyHtml = true, AdditionalEmailSendingArgs? additionalEmailSendingArgs = null)
    {
        SentMessages.Add((to, subject, body));
        return Task.CompletedTask;
    }

    public Task QueueAsync(string from, string to, string subject, string body, bool isBodyHtml = true, AdditionalEmailSendingArgs? additionalEmailSendingArgs = null)
    {
        SentMessages.Add((to, subject, body));
        return Task.CompletedTask;
    }
}
