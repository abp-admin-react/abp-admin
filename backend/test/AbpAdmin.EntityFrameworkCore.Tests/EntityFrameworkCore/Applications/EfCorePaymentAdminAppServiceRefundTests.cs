using AbpAdmin.Payments;
using Xunit;

namespace AbpAdmin.EntityFrameworkCore.Applications;

/* 管理端退款测试的 EF Core 具体类（抽象用例在 Application.Tests，
 * 见 PaymentAdminAppServiceRefundTests）。 */
[Collection(AbpAdminTestConsts.CollectionDefinitionName)]
public class EfCorePaymentAdminAppServiceRefundTests
    : PaymentAdminAppServiceRefundTests<AbpAdminEntityFrameworkCoreTestModule>
{
}
