using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AbpAdmin.Migrations
{
    /// <inheritdoc />
    public partial class RemoveEasyAbpPaymentService : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                UPDATE "AppMenus"
                SET "PermissionName" = 'AbpAdmin.Collection.Payments.View'
                WHERE "Path" = '/administration/payments' AND "PermissionName" = 'AbpAdmin.Payments';

                CREATE TEMPORARY TABLE "__RetiredPaymentPermissions" ON COMMIT DROP AS
                SELECT "Name" FROM "AbpPermissions"
                WHERE "GroupName" IN ('EasyAbp.PaymentService', 'EasyAbp.PaymentService.Prepayment',
                    'EasyAbp.PaymentService.WeChatPay')
                UNION
                SELECT unnest(ARRAY['AbpAdmin.Payments', 'AbpAdmin.Payments.Manage',
                    'AbpAdmin.Payments.Refund', 'AbpAdmin.Payments.Prepayment']);

                UPDATE "AbpPermissions"
                SET "ParentName" = NULL
                WHERE "ParentName" IN (SELECT "Name" FROM "__RetiredPaymentPermissions")
                  AND "Name" NOT IN (SELECT "Name" FROM "__RetiredPaymentPermissions");

                DELETE FROM "AbpPermissionGrants"
                WHERE "Name" IN (SELECT "Name" FROM "__RetiredPaymentPermissions")
                   OR "Name" LIKE 'EasyAbp.PaymentService.%';
                DELETE FROM "AbpPermissions"
                WHERE "Name" IN (SELECT "Name" FROM "__RetiredPaymentPermissions");
                DELETE FROM "AbpPermissionGroups"
                WHERE "Name" IN ('EasyAbp.PaymentService', 'EasyAbp.PaymentService.Prepayment',
                    'EasyAbp.PaymentService.WeChatPay');

                CREATE TEMPORARY TABLE "__RetiredPaymentSettings" ON COMMIT DROP AS
                SELECT unnest(ARRAY['AbpAdmin.Payment.WeChatPay.MchId', 'AbpAdmin.Payment.WeChatPay.ApiKey',
                    'AbpAdmin.Payment.WeChatPay.Certificate', 'AbpAdmin.Payment.WeChatPay.CertificateSecret',
                    'AbpAdmin.Payment.WeChatPay.NotifyUrl', 'Finance.Prepayment.FreezeWrites']) AS "Name";

                DELETE FROM "AbpSettings"
                WHERE "Name" IN (SELECT "Name" FROM "__RetiredPaymentSettings")
                   OR "Name" LIKE 'EasyAbp.PaymentService.%';
                DELETE FROM "AbpSettingDefinitions"
                WHERE "Name" IN (SELECT "Name" FROM "__RetiredPaymentSettings")
                   OR "Name" LIKE 'EasyAbp.PaymentService.%';
                """);

            migrationBuilder.DropTable(
                name: "EasyAbpPaymentServicePaymentItems");

            migrationBuilder.DropTable(
                name: "EasyAbpPaymentServicePrepaymentAccounts");

            migrationBuilder.DropTable(
                name: "EasyAbpPaymentServicePrepaymentTransactions");

            migrationBuilder.DropTable(
                name: "EasyAbpPaymentServicePrepaymentWithdrawalRecords");

            migrationBuilder.DropTable(
                name: "EasyAbpPaymentServicePrepaymentWithdrawalRequests");

            migrationBuilder.DropTable(
                name: "EasyAbpPaymentServiceRefundItems");

            migrationBuilder.DropTable(
                name: "EasyAbpPaymentServiceWeChatPayPaymentRecords");

            migrationBuilder.DropTable(
                name: "EasyAbpPaymentServiceWeChatPayRefundRecords");

            migrationBuilder.DropTable(
                name: "EasyAbpPaymentServicePayments");

            migrationBuilder.DropTable(
                name: "EasyAbpPaymentServiceRefunds");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // 只恢复表结构和菜单绑定。已退役的业务数据、权限和设置不会恢复。
            migrationBuilder.Sql("""
                UPDATE "AppMenus"
                SET "PermissionName" = 'AbpAdmin.Payments'
                WHERE "Path" = '/administration/payments'
                  AND "PermissionName" = 'AbpAdmin.Collection.Payments.View';
                """);

            migrationBuilder.CreateTable(
                name: "EasyAbpPaymentServicePayments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ActualPaymentAmount = table.Column<decimal>(type: "numeric(20,8)", nullable: false),
                    CanceledTime = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    CompletionTime = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    ConcurrencyStamp = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    CreationTime = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    CreatorId = table.Column<Guid>(type: "uuid", nullable: true),
                    Currency = table.Column<string>(type: "text", nullable: true),
                    DeleterId = table.Column<Guid>(type: "uuid", nullable: true),
                    DeletionTime = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    ExternalTradingCode = table.Column<string>(type: "text", nullable: true),
                    ExtraProperties = table.Column<string>(type: "text", nullable: false),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    LastModificationTime = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    LastModifierId = table.Column<Guid>(type: "uuid", nullable: true),
                    OriginalPaymentAmount = table.Column<decimal>(type: "numeric(20,8)", nullable: false),
                    PayeeAccount = table.Column<string>(type: "text", nullable: true),
                    PaymentDiscount = table.Column<decimal>(type: "numeric(20,8)", nullable: false),
                    PaymentMethod = table.Column<string>(type: "text", nullable: true),
                    PendingRefundAmount = table.Column<decimal>(type: "numeric(20,8)", nullable: false),
                    RefundAmount = table.Column<decimal>(type: "numeric(20,8)", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: true),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EasyAbpPaymentServicePayments", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "EasyAbpPaymentServicePrepaymentAccounts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AccountGroupName = table.Column<string>(type: "text", nullable: true),
                    Balance = table.Column<decimal>(type: "numeric(20,8)", nullable: false),
                    ConcurrencyStamp = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    CreationTime = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    CreatorId = table.Column<Guid>(type: "uuid", nullable: true),
                    DeleterId = table.Column<Guid>(type: "uuid", nullable: true),
                    DeletionTime = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    ExtraProperties = table.Column<string>(type: "text", nullable: false),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    LastModificationTime = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    LastModifierId = table.Column<Guid>(type: "uuid", nullable: true),
                    LockedBalance = table.Column<decimal>(type: "numeric(20,8)", nullable: false),
                    PendingTopUpPaymentId = table.Column<Guid>(type: "uuid", nullable: true),
                    PendingWithdrawalAmount = table.Column<decimal>(type: "numeric(20,8)", nullable: false),
                    PendingWithdrawalRecordId = table.Column<Guid>(type: "uuid", nullable: true),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: true),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EasyAbpPaymentServicePrepaymentAccounts", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "EasyAbpPaymentServicePrepaymentTransactions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    AccountUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    ActionName = table.Column<string>(type: "text", nullable: true),
                    ChangedBalance = table.Column<decimal>(type: "numeric(20,8)", nullable: false),
                    ConcurrencyStamp = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    CreationTime = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    CreatorId = table.Column<Guid>(type: "uuid", nullable: true),
                    Currency = table.Column<string>(type: "text", nullable: true),
                    ExternalTradingCode = table.Column<string>(type: "text", nullable: true),
                    ExtraProperties = table.Column<string>(type: "text", nullable: false),
                    OriginalBalance = table.Column<decimal>(type: "numeric(20,8)", nullable: false),
                    PaymentId = table.Column<Guid>(type: "uuid", nullable: true),
                    PaymentMethod = table.Column<string>(type: "text", nullable: true),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: true),
                    TransactionType = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EasyAbpPaymentServicePrepaymentTransactions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "EasyAbpPaymentServicePrepaymentWithdrawalRecords",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(20,8)", nullable: false),
                    CancellationTime = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    CompletionTime = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    ConcurrencyStamp = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    CreationTime = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    CreatorId = table.Column<Guid>(type: "uuid", nullable: true),
                    DeleterId = table.Column<Guid>(type: "uuid", nullable: true),
                    DeletionTime = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    ExtraProperties = table.Column<string>(type: "text", nullable: false),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    LastModificationTime = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    LastModifierId = table.Column<Guid>(type: "uuid", nullable: true),
                    ResultErrorCode = table.Column<string>(type: "text", nullable: true),
                    ResultErrorMessage = table.Column<string>(type: "text", nullable: true),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: true),
                    WithdrawalMethod = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EasyAbpPaymentServicePrepaymentWithdrawalRecords", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "EasyAbpPaymentServicePrepaymentWithdrawalRequests",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    AccountUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(20,8)", nullable: false),
                    ConcurrencyStamp = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    CreationTime = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    CreatorId = table.Column<Guid>(type: "uuid", nullable: true),
                    DeleterId = table.Column<Guid>(type: "uuid", nullable: true),
                    DeletionTime = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    ExtraProperties = table.Column<string>(type: "text", nullable: false),
                    IsApproved = table.Column<bool>(type: "boolean", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    LastModificationTime = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    LastModifierId = table.Column<Guid>(type: "uuid", nullable: true),
                    ReviewTime = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    ReviewerUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EasyAbpPaymentServicePrepaymentWithdrawalRequests", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "EasyAbpPaymentServiceRefunds",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CanceledTime = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    CompletedTime = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    ConcurrencyStamp = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    CreationTime = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    CreatorId = table.Column<Guid>(type: "uuid", nullable: true),
                    Currency = table.Column<string>(type: "text", nullable: true),
                    CustomerRemark = table.Column<string>(type: "text", nullable: true),
                    DeleterId = table.Column<Guid>(type: "uuid", nullable: true),
                    DeletionTime = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    DisplayReason = table.Column<string>(type: "text", nullable: true),
                    ExternalTradingCode = table.Column<string>(type: "text", nullable: true),
                    ExtraProperties = table.Column<string>(type: "text", nullable: false),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    LastModificationTime = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    LastModifierId = table.Column<Guid>(type: "uuid", nullable: true),
                    PaymentId = table.Column<Guid>(type: "uuid", nullable: false),
                    RefundAmount = table.Column<decimal>(type: "numeric(20,8)", nullable: false),
                    RefundPaymentMethod = table.Column<string>(type: "text", nullable: true),
                    StaffRemark = table.Column<string>(type: "text", nullable: true),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EasyAbpPaymentServiceRefunds", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "EasyAbpPaymentServiceWeChatPayPaymentRecords",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Amount = table.Column<string>(type: "text", nullable: true),
                    AppId = table.Column<string>(type: "text", nullable: true),
                    Attach = table.Column<string>(type: "text", nullable: true),
                    BankType = table.Column<string>(type: "text", nullable: true),
                    CashFee = table.Column<int>(type: "integer", nullable: false),
                    CashFeeType = table.Column<string>(type: "text", nullable: true),
                    ConcurrencyStamp = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    CouponCount = table.Column<int>(type: "integer", nullable: true),
                    CouponFee = table.Column<int>(type: "integer", nullable: true),
                    CouponFees = table.Column<string>(type: "text", nullable: true),
                    CouponIds = table.Column<string>(type: "text", nullable: true),
                    CouponTypes = table.Column<string>(type: "text", nullable: true),
                    CreationTime = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    CreatorId = table.Column<Guid>(type: "uuid", nullable: true),
                    DeviceInfo = table.Column<string>(type: "text", nullable: true),
                    ErrCode = table.Column<string>(type: "text", nullable: true),
                    ErrCodeDes = table.Column<string>(type: "text", nullable: true),
                    ExtraProperties = table.Column<string>(type: "text", nullable: false),
                    FeeType = table.Column<string>(type: "text", nullable: true),
                    IsSubscribe = table.Column<string>(type: "text", nullable: true),
                    MchId = table.Column<string>(type: "text", nullable: true),
                    Openid = table.Column<string>(type: "text", nullable: true),
                    OutTradeNo = table.Column<string>(type: "text", nullable: true),
                    Payer = table.Column<string>(type: "text", nullable: true),
                    PaymentId = table.Column<Guid>(type: "uuid", nullable: false),
                    PromotionDetail = table.Column<string>(type: "text", nullable: true),
                    ResultCode = table.Column<string>(type: "text", nullable: true),
                    ReturnCode = table.Column<string>(type: "text", nullable: true),
                    ReturnMsg = table.Column<string>(type: "text", nullable: true),
                    SceneInfo = table.Column<string>(type: "text", nullable: true),
                    SettlementTotalFee = table.Column<int>(type: "integer", nullable: true),
                    SuccessTime = table.Column<string>(type: "text", nullable: true),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: true),
                    TimeEnd = table.Column<string>(type: "text", nullable: true),
                    TotalFee = table.Column<int>(type: "integer", nullable: false),
                    TradeState = table.Column<string>(type: "text", nullable: true),
                    TradeStateDesc = table.Column<string>(type: "text", nullable: true),
                    TradeType = table.Column<string>(type: "text", nullable: true),
                    TransactionId = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EasyAbpPaymentServiceWeChatPayPaymentRecords", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "EasyAbpPaymentServiceWeChatPayRefundRecords",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Amount = table.Column<string>(type: "text", nullable: true),
                    AppId = table.Column<string>(type: "text", nullable: true),
                    CashFee = table.Column<int>(type: "integer", nullable: false),
                    CashFeeType = table.Column<string>(type: "text", nullable: true),
                    CashRefundFee = table.Column<int>(type: "integer", nullable: true),
                    Channel = table.Column<string>(type: "text", nullable: true),
                    ConcurrencyStamp = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    CouponIds = table.Column<string>(type: "text", nullable: true),
                    CouponRefundCount = table.Column<int>(type: "integer", nullable: true),
                    CouponRefundFee = table.Column<int>(type: "integer", nullable: true),
                    CouponRefundFees = table.Column<string>(type: "text", nullable: true),
                    CouponTypes = table.Column<string>(type: "text", nullable: true),
                    CreateTime = table.Column<string>(type: "text", nullable: true),
                    CreationTime = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    CreatorId = table.Column<Guid>(type: "uuid", nullable: true),
                    ExtraProperties = table.Column<string>(type: "text", nullable: false),
                    FeeType = table.Column<string>(type: "text", nullable: true),
                    FundsAccount = table.Column<string>(type: "text", nullable: true),
                    MchId = table.Column<string>(type: "text", nullable: true),
                    OutRefundNo = table.Column<string>(type: "text", nullable: true),
                    OutTradeNo = table.Column<string>(type: "text", nullable: true),
                    PaymentId = table.Column<Guid>(type: "uuid", nullable: false),
                    PromotionDetail = table.Column<string>(type: "text", nullable: true),
                    RefundAccount = table.Column<string>(type: "text", nullable: true),
                    RefundFee = table.Column<int>(type: "integer", nullable: false),
                    RefundId = table.Column<string>(type: "text", nullable: true),
                    RefundRecvAccout = table.Column<string>(type: "text", nullable: true),
                    RefundRequestSource = table.Column<string>(type: "text", nullable: true),
                    RefundStatus = table.Column<string>(type: "text", nullable: true),
                    ReturnCode = table.Column<string>(type: "text", nullable: true),
                    ReturnMsg = table.Column<string>(type: "text", nullable: true),
                    SettlementRefundFee = table.Column<int>(type: "integer", nullable: true),
                    SettlementTotalFee = table.Column<int>(type: "integer", nullable: true),
                    Status = table.Column<string>(type: "text", nullable: true),
                    SuccessTime = table.Column<string>(type: "text", nullable: true),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: true),
                    TotalFee = table.Column<int>(type: "integer", nullable: false),
                    TransactionId = table.Column<string>(type: "text", nullable: true),
                    UserReceivedAccount = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EasyAbpPaymentServiceWeChatPayRefundRecords", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "EasyAbpPaymentServicePaymentItems",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ActualPaymentAmount = table.Column<decimal>(type: "numeric(20,8)", nullable: false),
                    CreationTime = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    CreatorId = table.Column<Guid>(type: "uuid", nullable: true),
                    DeleterId = table.Column<Guid>(type: "uuid", nullable: true),
                    DeletionTime = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    ExtraProperties = table.Column<string>(type: "text", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    ItemKey = table.Column<string>(type: "text", nullable: true),
                    ItemType = table.Column<string>(type: "text", nullable: true),
                    LastModificationTime = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    LastModifierId = table.Column<Guid>(type: "uuid", nullable: true),
                    OriginalPaymentAmount = table.Column<decimal>(type: "numeric(20,8)", nullable: false),
                    PaymentDiscount = table.Column<decimal>(type: "numeric(20,8)", nullable: false),
                    PaymentId = table.Column<Guid>(type: "uuid", nullable: true),
                    PendingRefundAmount = table.Column<decimal>(type: "numeric(20,8)", nullable: false),
                    RefundAmount = table.Column<decimal>(type: "numeric(20,8)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EasyAbpPaymentServicePaymentItems", x => x.Id);
                    table.ForeignKey(
                        name: "FK_EasyAbpPaymentServicePaymentItems_EasyAbpPaymentServicePaym~",
                        column: x => x.PaymentId,
                        principalTable: "EasyAbpPaymentServicePayments",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "EasyAbpPaymentServiceRefundItems",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CreationTime = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    CreatorId = table.Column<Guid>(type: "uuid", nullable: true),
                    CustomerRemark = table.Column<string>(type: "text", nullable: true),
                    DeleterId = table.Column<Guid>(type: "uuid", nullable: true),
                    DeletionTime = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    ExtraProperties = table.Column<string>(type: "text", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    LastModificationTime = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    LastModifierId = table.Column<Guid>(type: "uuid", nullable: true),
                    PaymentItemId = table.Column<Guid>(type: "uuid", nullable: false),
                    RefundAmount = table.Column<decimal>(type: "numeric(20,8)", nullable: false),
                    RefundId = table.Column<Guid>(type: "uuid", nullable: true),
                    StaffRemark = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EasyAbpPaymentServiceRefundItems", x => x.Id);
                    table.ForeignKey(
                        name: "FK_EasyAbpPaymentServiceRefundItems_EasyAbpPaymentServiceRefun~",
                        column: x => x.RefundId,
                        principalTable: "EasyAbpPaymentServiceRefunds",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_EasyAbpPaymentServicePaymentItems_PaymentId",
                table: "EasyAbpPaymentServicePaymentItems",
                column: "PaymentId");

            migrationBuilder.CreateIndex(
                name: "IX_EasyAbpPaymentServicePrepaymentAccounts_UserId",
                table: "EasyAbpPaymentServicePrepaymentAccounts",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_EasyAbpPaymentServicePrepaymentTransactions_AccountId",
                table: "EasyAbpPaymentServicePrepaymentTransactions",
                column: "AccountId");

            migrationBuilder.CreateIndex(
                name: "IX_EasyAbpPaymentServicePrepaymentTransactions_AccountUserId",
                table: "EasyAbpPaymentServicePrepaymentTransactions",
                column: "AccountUserId");

            migrationBuilder.CreateIndex(
                name: "IX_EasyAbpPaymentServiceRefundItems_RefundId",
                table: "EasyAbpPaymentServiceRefundItems",
                column: "RefundId");

            migrationBuilder.CreateIndex(
                name: "IX_EasyAbpPaymentServiceWeChatPayPaymentRecords_PaymentId",
                table: "EasyAbpPaymentServiceWeChatPayPaymentRecords",
                column: "PaymentId");

            migrationBuilder.CreateIndex(
                name: "IX_EasyAbpPaymentServiceWeChatPayRefundRecords_OutRefundNo",
                table: "EasyAbpPaymentServiceWeChatPayRefundRecords",
                column: "OutRefundNo");

            migrationBuilder.CreateIndex(
                name: "IX_EasyAbpPaymentServiceWeChatPayRefundRecords_PaymentId",
                table: "EasyAbpPaymentServiceWeChatPayRefundRecords",
                column: "PaymentId");
        }
    }
}
