using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AbpAdmin.Ledger.Migrations
{
    /// <inheritdoc />
    public partial class F1_PostingEngine : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_FinVouchers_TenantId_BizType_BizRefId_Phase_BatchKey",
                table: "FinVouchers");

            migrationBuilder.DropIndex(
                name: "IX_FinSubAccounts_TenantId_AccountId_OwnerType_OwnerId_Currenc~",
                table: "FinSubAccounts");

            migrationBuilder.DropIndex(
                name: "IX_FinPostingRules_TenantId_BizType_Phase_Version",
                table: "FinPostingRules");

            migrationBuilder.DropIndex(
                name: "IX_FinPeriods_TenantId_Year_Month",
                table: "FinPeriods");

            migrationBuilder.DropIndex(
                name: "IX_FinBalanceProjections_TenantId_SubAccountId",
                table: "FinBalanceProjections");

            migrationBuilder.DropIndex(
                name: "IX_FinAccounts_TenantId_Code",
                table: "FinAccounts");

            migrationBuilder.AddColumn<string>(
                name: "Fingerprint",
                table: "FinVouchers",
                type: "character varying(64)",
                maxLength: 64,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<Guid>(
                name: "UserId",
                table: "FinVouchers",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "AuditedAt",
                table: "FinManualAdjusts",
                type: "timestamp without time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CreditOwnerId",
                table: "FinManualAdjusts",
                type: "character varying(64)",
                maxLength: 64,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<int>(
                name: "CreditOwnerType",
                table: "FinManualAdjusts",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "DebitOwnerId",
                table: "FinManualAdjusts",
                type: "character varying(64)",
                maxLength: 64,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<int>(
                name: "DebitOwnerType",
                table: "FinManualAdjusts",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<Guid>(
                name: "DeleterId",
                table: "FinManualAdjusts",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "DeletionTime",
                table: "FinManualAdjusts",
                type: "timestamp without time zone",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsDeleted",
                table: "FinManualAdjusts",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "RejectReason",
                table: "FinManualAdjusts",
                type: "character varying(512)",
                maxLength: 512,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ReversalVoucherId",
                table: "FinManualAdjusts",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "SubmittedAt",
                table: "FinManualAdjusts",
                type: "timestamp without time zone",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_FinVouchers_ReversalOf",
                table: "FinVouchers",
                column: "ReversalOf");

            migrationBuilder.CreateIndex(
                name: "IX_FinVouchers_TenantId_BizType_BizRefId_Phase_BatchKey",
                table: "FinVouchers",
                columns: new[] { "TenantId", "BizType", "BizRefId", "Phase", "BatchKey" },
                unique: true)
                .Annotation("Npgsql:NullsDistinct", false);

            migrationBuilder.CreateIndex(
                name: "IX_FinSubAccounts_TenantId_AccountId_OwnerType_OwnerId_Currenc~",
                table: "FinSubAccounts",
                columns: new[] { "TenantId", "AccountId", "OwnerType", "OwnerId", "Currency", "Bucket" },
                unique: true)
                .Annotation("Npgsql:NullsDistinct", false);

            migrationBuilder.CreateIndex(
                name: "IX_FinPostingRules_TenantId_BizType_Phase_Version",
                table: "FinPostingRules",
                columns: new[] { "TenantId", "BizType", "Phase", "Version" },
                unique: true)
                .Annotation("Npgsql:NullsDistinct", false);

            migrationBuilder.CreateIndex(
                name: "IX_FinPeriods_TenantId_Year_Month",
                table: "FinPeriods",
                columns: new[] { "TenantId", "Year", "Month" },
                unique: true)
                .Annotation("Npgsql:NullsDistinct", false);

            migrationBuilder.CreateIndex(
                name: "IX_FinBalanceProjections_TenantId_SubAccountId",
                table: "FinBalanceProjections",
                columns: new[] { "TenantId", "SubAccountId" },
                unique: true)
                .Annotation("Npgsql:NullsDistinct", false);

            migrationBuilder.CreateIndex(
                name: "IX_FinAccounts_TenantId_Code",
                table: "FinAccounts",
                columns: new[] { "TenantId", "Code" },
                unique: true)
                .Annotation("Npgsql:NullsDistinct", false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_FinVouchers_ReversalOf",
                table: "FinVouchers");

            migrationBuilder.DropIndex(
                name: "IX_FinVouchers_TenantId_BizType_BizRefId_Phase_BatchKey",
                table: "FinVouchers");

            migrationBuilder.DropIndex(
                name: "IX_FinSubAccounts_TenantId_AccountId_OwnerType_OwnerId_Currenc~",
                table: "FinSubAccounts");

            migrationBuilder.DropIndex(
                name: "IX_FinPostingRules_TenantId_BizType_Phase_Version",
                table: "FinPostingRules");

            migrationBuilder.DropIndex(
                name: "IX_FinPeriods_TenantId_Year_Month",
                table: "FinPeriods");

            migrationBuilder.DropIndex(
                name: "IX_FinBalanceProjections_TenantId_SubAccountId",
                table: "FinBalanceProjections");

            migrationBuilder.DropIndex(
                name: "IX_FinAccounts_TenantId_Code",
                table: "FinAccounts");

            migrationBuilder.DropColumn(
                name: "Fingerprint",
                table: "FinVouchers");

            migrationBuilder.DropColumn(
                name: "UserId",
                table: "FinVouchers");

            migrationBuilder.DropColumn(
                name: "AuditedAt",
                table: "FinManualAdjusts");

            migrationBuilder.DropColumn(
                name: "CreditOwnerId",
                table: "FinManualAdjusts");

            migrationBuilder.DropColumn(
                name: "CreditOwnerType",
                table: "FinManualAdjusts");

            migrationBuilder.DropColumn(
                name: "DebitOwnerId",
                table: "FinManualAdjusts");

            migrationBuilder.DropColumn(
                name: "DebitOwnerType",
                table: "FinManualAdjusts");

            migrationBuilder.DropColumn(
                name: "DeleterId",
                table: "FinManualAdjusts");

            migrationBuilder.DropColumn(
                name: "DeletionTime",
                table: "FinManualAdjusts");

            migrationBuilder.DropColumn(
                name: "IsDeleted",
                table: "FinManualAdjusts");

            migrationBuilder.DropColumn(
                name: "RejectReason",
                table: "FinManualAdjusts");

            migrationBuilder.DropColumn(
                name: "ReversalVoucherId",
                table: "FinManualAdjusts");

            migrationBuilder.DropColumn(
                name: "SubmittedAt",
                table: "FinManualAdjusts");

            migrationBuilder.CreateIndex(
                name: "IX_FinVouchers_TenantId_BizType_BizRefId_Phase_BatchKey",
                table: "FinVouchers",
                columns: new[] { "TenantId", "BizType", "BizRefId", "Phase", "BatchKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_FinSubAccounts_TenantId_AccountId_OwnerType_OwnerId_Currenc~",
                table: "FinSubAccounts",
                columns: new[] { "TenantId", "AccountId", "OwnerType", "OwnerId", "Currency", "Bucket" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_FinPostingRules_TenantId_BizType_Phase_Version",
                table: "FinPostingRules",
                columns: new[] { "TenantId", "BizType", "Phase", "Version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_FinPeriods_TenantId_Year_Month",
                table: "FinPeriods",
                columns: new[] { "TenantId", "Year", "Month" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_FinBalanceProjections_TenantId_SubAccountId",
                table: "FinBalanceProjections",
                columns: new[] { "TenantId", "SubAccountId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_FinAccounts_TenantId_Code",
                table: "FinAccounts",
                columns: new[] { "TenantId", "Code" },
                unique: true);
        }
    }
}
