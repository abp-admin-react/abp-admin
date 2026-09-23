using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AbpAdmin.Permissions;
using EasyAbp.PaymentService.Payments;
using EasyAbp.PaymentService.Refunds;
using Microsoft.AspNetCore.Authorization;
using Volo.Abp;
using Volo.Abp.DistributedLocking;
using Volo.Abp.Domain.Entities;
using Volo.Abp.Uow;

namespace AbpAdmin.Payments;

[Authorize(AbpAdminPermissions.Payments.Default)]
public class PaymentAdminAppService : AbpAdminAppService, IPaymentAdminAppService
{
    private readonly IPaymentManager _paymentManager;
    private readonly IPaymentRepository _paymentRepository;
    private readonly IAbpDistributedLock _distributedLock;

    public PaymentAdminAppService(
        IPaymentManager paymentManager,
        IPaymentRepository paymentRepository,
        IAbpDistributedLock distributedLock)
    {
        _paymentManager = paymentManager;
        _paymentRepository = paymentRepository;
        _distributedLock = distributedLock;
    }

    /// <summary>
    /// 按支付单行项的可退余额拆分退款。
    /// <paramref name="input"/>.RefundAmount 为 null 时表示全额退款（按行项剩余可退总额，服务端计算）；
    /// 指定金额时必须 &gt; 0 且不得超过 Σ(ActualPaymentAmount - RefundAmount - PendingRefundAmount)；
    /// 超额或无可退余额则失败，不截断成部分退。
    /// 客户端可以乱填金额，服务端以库里的行项为唯一真相。
    /// 退款额分配基于读出的行项快照：并发退款（两个管理员同时操作同一单）由
    /// abpadmin:refund:{id} 分布式锁串行化，防止双双通过"剩余可退"校验后超退。
    /// </summary>
    /// <exception cref="EntityNotFoundException">支付单不存在。</exception>
    /// <exception cref="BusinessException">金额非法、超过剩余可退，或已有退款正在处理。</exception>
    [Authorize(AbpAdminPermissions.Payments.Refund)]
    public virtual async Task RefundAsync(Guid id, RefundPaymentInput input)
    {
        // 第二参是"获取锁的等待超时"而非持锁时长：等待会阻塞并发方，这里要的是快速失败
        await using var lockHandle = await _distributedLock.TryAcquireAsync(
            $"abpadmin:refund:{id}", TimeSpan.Zero);
        if (lockHandle == null)
        {
            throw new BusinessException(AbpAdminDomainErrorCodes.Payments.RefundInProgress)
                .WithData("PaymentId", id);
        }

        // 退款写库包进 requiresNew 事务型 UoW 并在锁释放前 CompleteAsync：
        // 外层约定 UoW 由拦截器在方法返回后才提交，若沿用外层，锁释放早于提交，
        // 并发退款在窗口内拿锁会读到未提交的 PendingRefundAmount 而超退。
        using (var uow = UnitOfWorkManager.Begin(requiresNew: true, isTransactional: true))
        {
            var queryable = await _paymentRepository.WithDetailsAsync();
            var payment = await AsyncExecuter.FirstOrDefaultAsync(queryable.Where(x => x.Id == id));
            if (payment == null)
            {
                throw new EntityNotFoundException(typeof(Payment), id);
            }

            // null = 全额退款：金额完全由服务端按行项剩余可退计算
            var remaining = input.RefundAmount ?? payment.PaymentItems
                .Select(x => x.ActualPaymentAmount - x.RefundAmount - x.PendingRefundAmount)
                .Where(x => x > 0)
                .Sum();

            if (remaining <= 0)
            {
                throw new BusinessException(AbpAdminDomainErrorCodes.Payments.RefundAmountInvalid);
            }

            var refundItems = new List<CreateRefundItemInput>();
            // 行项加载顺序不保证（EF Include 返回序随查询计划/引擎变化，集成测试实测同库内可翻转）；
            // 贪心分配必须确定性：按 ItemKey 升序（支付单创建后不变），同一单在任何环境重放分配一致。
            // 不能用 Id 排序——ABP 默认 IGuidGenerator 是 SimpleGuidGenerator（Guid.NewGuid），不携带创建序
            foreach (var item in payment.PaymentItems.OrderBy(x => x.ItemKey))
            {
                var refundable = item.ActualPaymentAmount - item.RefundAmount - item.PendingRefundAmount;
                if (refundable <= 0)
                {
                    continue;
                }

                var amount = remaining < refundable ? remaining : refundable;
                refundItems.Add(new CreateRefundItemInput
                {
                    PaymentItemId = item.Id,
                    RefundAmount = amount,
                    StaffRemark = input.DisplayReason
                });
                remaining -= amount;
                if (remaining <= 0)
                {
                    break;
                }
            }

            if (refundItems.Count == 0 || remaining > 0)
            {
                throw new BusinessException(AbpAdminDomainErrorCodes.Payments.RefundAmountInvalid);
            }

            await _paymentManager.StartRefundAsync(payment, new CreateRefundInput
            {
                PaymentId = payment.Id,
                DisplayReason = input.DisplayReason,
                StaffRemark = input.DisplayReason,
                RefundItems = refundItems
            });

            await uow.CompleteAsync();
        }
    }
}
