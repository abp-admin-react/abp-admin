using System;
using System.ComponentModel.DataAnnotations;
using System.Threading.Tasks;
using Volo.Abp.Application.Services;

namespace AbpAdmin.Payments;

public interface IPaymentAdminAppService : IApplicationService
{
    Task RefundAsync(Guid id, RefundPaymentInput input);
}

public class RefundPaymentInput
{
    /// <summary>
    /// 退款金额。null 表示全额退款：按行项剩余可退总额由服务端计算
    /// （前端不做 decimal 运算，避免 IEEE-754 精度尾差导致"超额 0.0000…1"被拒）。
    /// </summary>
    [Range(typeof(decimal), "0.01", "79228162514264337593543950335")]
    public decimal? RefundAmount { get; set; }

    [StringLength(256)]
    public string? DisplayReason { get; set; }
}
