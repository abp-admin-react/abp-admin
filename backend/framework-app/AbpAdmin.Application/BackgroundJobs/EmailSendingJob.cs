using System.Threading.Tasks;
using Volo.Abp.BackgroundJobs;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Emailing;

namespace AbpAdmin.BackgroundJobs;

[BackgroundJobName("abpadmin.email-sending")]
public class EmailSendingArgs
{
    public string EmailAddress { get; set; } = default!;

    public string Subject { get; set; } = default!;

    public string? Body { get; set; }
}

public class EmailSendingJob : AsyncBackgroundJob<EmailSendingArgs>, ITransientDependency
{
    private readonly IEmailSender _emailSender;

    public EmailSendingJob(IEmailSender emailSender)
    {
        _emailSender = emailSender;
    }

    public override async Task ExecuteAsync(EmailSendingArgs args)
    {
        await _emailSender.SendAsync(args.EmailAddress, args.Subject, args.Body ?? string.Empty);
    }
}
