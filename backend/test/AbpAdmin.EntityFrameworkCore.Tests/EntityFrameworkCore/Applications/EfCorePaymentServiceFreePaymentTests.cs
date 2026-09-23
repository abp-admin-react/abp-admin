using AbpAdmin.Payments;
using Xunit;

namespace AbpAdmin.EntityFrameworkCore.Applications;

[Collection(AbpAdminTestConsts.CollectionDefinitionName)]
public class EfCorePaymentServiceFreePaymentTests
    : PaymentServiceFreePaymentTests<AbpAdminEntityFrameworkCoreTestModule>
{
}
