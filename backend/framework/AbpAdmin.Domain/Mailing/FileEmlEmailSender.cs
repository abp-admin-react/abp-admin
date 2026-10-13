using System;
using System.IO;
using System.Linq;
using System.Net.Mail;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Volo.Abp.BackgroundJobs;
using Volo.Abp.Emailing;
using Volo.Abp.MultiTenancy;

namespace AbpAdmin.Mailing;

/// <summary>
/// 开发/测试环境的邮件降级发送器：把每封邮件写成 .eml 文件，供本地 E2E 观测
/// （魔法链接、双因素邮箱码、联系方式确认码等依赖邮件内容的流程）。
/// 注册时机：Mailing:UseRealSender = false 时替代 NullEmailSender（邮件不再静默丢失）。
/// 文件落盘 ContentRootPath/App_Data/outgoing-emails/{时间戳}-{主题}.eml，
/// 可用邮件客户端直接打开，正文与收件人一目了然。写盘失败仅记日志，不阻断业务流。
/// </summary>
public class FileEmlEmailSender : EmailSenderBase
{
    private readonly IHostEnvironment _hostEnvironment;
    private readonly ILogger<FileEmlEmailSender> _logger;

    public FileEmlEmailSender(
        ICurrentTenant currentTenant,
        IEmailSenderConfiguration configuration,
        IBackgroundJobManager backgroundJobManager,
        IHostEnvironment hostEnvironment,
        ILogger<FileEmlEmailSender> logger)
        : base(currentTenant, configuration, backgroundJobManager)
    {
        _hostEnvironment = hostEnvironment;
        _logger = logger;
    }

    protected override Task SendEmailAsync(MailMessage mail)
    {
        try
        {
            var directory = Path.Combine(_hostEnvironment.ContentRootPath, "App_Data", "outgoing-emails");
            Directory.CreateDirectory(directory);

            var subject = mail.Subject ?? "no-subject";
            var safeSubject = string.Join("-", subject.Split(
                Path.GetInvalidFileNameChars().Append('-').Distinct().ToArray(),
                StringSplitOptions.RemoveEmptyEntries));
            if (safeSubject.Length > 40)
            {
                safeSubject = safeSubject[..40];
            }

            var fileToken = Guid.NewGuid().ToString("N")[..8];
            var fileName = $"{DateTime.Now:yyyyMMdd-HHmmss}-{fileToken}-{safeSubject}.eml";
            var path = Path.Combine(directory, fileName);

            using var writer = new StreamWriter(path);
            writer.WriteLine($"From: {mail.From}");
            writer.WriteLine($"To: {string.Join("; ", mail.To.Select(t => t.Address))}");
            writer.WriteLine($"Subject: {subject}");
            writer.WriteLine($"Date: {DateTime.Now:R}");
            writer.WriteLine("MIME-Version: 1.0");
            writer.WriteLine("Content-Type: text/html; charset=utf-8");
            writer.WriteLine();
            writer.Write(mail.Body ?? string.Empty);

            _logger.LogInformation(
                "邮件已写入 {Path}（收件人 {To}，主题 {Subject}）",
                path, string.Join(";", mail.To.Select(t => t.Address)), subject);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "写邮件文件失败：{Subject}", mail.Subject);
        }

        return Task.CompletedTask;
    }
}
