using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AbpAdmin.Ledger.Migrations
{
    /// <inheritdoc />
    public partial class Initial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "FinAccounts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: true),
                    Code = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Name = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Type = table.Column<int>(type: "integer", nullable: false),
                    ParentId = table.Column<Guid>(type: "uuid", nullable: true),
                    NormalSide = table.Column<int>(type: "integer", nullable: false),
                    AllowNegative = table.Column<bool>(type: "boolean", nullable: false),
                    IsSystem = table.Column<bool>(type: "boolean", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    ExtraProperties = table.Column<string>(type: "text", nullable: false),
                    ConcurrencyStamp = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    CreationTime = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    CreatorId = table.Column<Guid>(type: "uuid", nullable: true),
                    LastModificationTime = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    LastModifierId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FinAccounts", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "FinBalanceProjections",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: true),
                    SubAccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    Balance = table.Column<decimal>(type: "numeric(28,18)", precision: 28, scale: 18, nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FinBalanceProjections", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "FinEntries",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: true),
                    VoucherId = table.Column<Guid>(type: "uuid", nullable: false),
                    SubAccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    Debit = table.Column<decimal>(type: "numeric(28,18)", precision: 28, scale: 18, nullable: false),
                    Credit = table.Column<decimal>(type: "numeric(28,18)", precision: 28, scale: 18, nullable: false),
                    BalanceAfter = table.Column<decimal>(type: "numeric(28,18)", precision: 28, scale: 18, nullable: true),
                    Dimensions = table.Column<string>(type: "jsonb", nullable: true),
                    Seq = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FinEntries", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "FinManualAdjusts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: true),
                    ApplicantId = table.Column<Guid>(type: "uuid", nullable: false),
                    AuditorId = table.Column<Guid>(type: "uuid", nullable: true),
                    Amount = table.Column<decimal>(type: "numeric(28,18)", precision: 28, scale: 18, nullable: false),
                    Currency = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    DebitAccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreditAccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    Reason = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    VoucherId = table.Column<Guid>(type: "uuid", nullable: true),
                    ExtraProperties = table.Column<string>(type: "text", nullable: false),
                    ConcurrencyStamp = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    CreationTime = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    CreatorId = table.Column<Guid>(type: "uuid", nullable: true),
                    LastModificationTime = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    LastModifierId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FinManualAdjusts", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "FinPeriods",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: true),
                    Year = table.Column<int>(type: "integer", nullable: false),
                    Month = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    ExtraProperties = table.Column<string>(type: "text", nullable: false),
                    ConcurrencyStamp = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    CreationTime = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    CreatorId = table.Column<Guid>(type: "uuid", nullable: true),
                    LastModificationTime = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    LastModifierId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FinPeriods", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "FinPostingRules",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: true),
                    BizType = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Phase = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    TemplateJson = table.Column<string>(type: "jsonb", nullable: false),
                    Description = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    ExtraProperties = table.Column<string>(type: "text", nullable: false),
                    ConcurrencyStamp = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    CreationTime = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    CreatorId = table.Column<Guid>(type: "uuid", nullable: true),
                    LastModificationTime = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    LastModifierId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FinPostingRules", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "FinReconcileRuns",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: true),
                    RunType = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ScopeJson = table.Column<string>(type: "jsonb", nullable: true),
                    DiffJson = table.Column<string>(type: "jsonb", nullable: true),
                    HasDiff = table.Column<bool>(type: "boolean", nullable: false),
                    IncludeDemo = table.Column<bool>(type: "boolean", nullable: false),
                    ExtraProperties = table.Column<string>(type: "text", nullable: false),
                    ConcurrencyStamp = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    CreationTime = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    CreatorId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FinReconcileRuns", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "FinSubAccounts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: true),
                    AccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    OwnerType = table.Column<int>(type: "integer", nullable: false),
                    OwnerId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Currency = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    Bucket = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ExtraProperties = table.Column<string>(type: "text", nullable: false),
                    ConcurrencyStamp = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    CreationTime = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    CreatorId = table.Column<Guid>(type: "uuid", nullable: true),
                    LastModificationTime = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    LastModifierId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FinSubAccounts", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "FinVouchers",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: true),
                    BizType = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    BizRefId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Phase = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    BatchKey = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    RuleVersion = table.Column<int>(type: "integer", nullable: false),
                    PeriodId = table.Column<Guid>(type: "uuid", nullable: true),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    ReversalOf = table.Column<Guid>(type: "uuid", nullable: true),
                    AgentId = table.Column<Guid>(type: "uuid", nullable: true),
                    IsDemo = table.Column<bool>(type: "boolean", nullable: false),
                    Summary = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    Currency = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(28,18)", precision: 28, scale: 18, nullable: false),
                    ExtraProperties = table.Column<string>(type: "text", nullable: false),
                    ConcurrencyStamp = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    CreationTime = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    CreatorId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FinVouchers", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_FinAccounts_TenantId_Code",
                table: "FinAccounts",
                columns: new[] { "TenantId", "Code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_FinBalanceProjections_TenantId_SubAccountId",
                table: "FinBalanceProjections",
                columns: new[] { "TenantId", "SubAccountId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_FinEntries_SubAccountId",
                table: "FinEntries",
                column: "SubAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_FinEntries_VoucherId_Seq",
                table: "FinEntries",
                columns: new[] { "VoucherId", "Seq" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_FinManualAdjusts_TenantId_Status",
                table: "FinManualAdjusts",
                columns: new[] { "TenantId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_FinPeriods_TenantId_Year_Month",
                table: "FinPeriods",
                columns: new[] { "TenantId", "Year", "Month" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_FinPostingRules_TenantId_BizType_Phase_Version",
                table: "FinPostingRules",
                columns: new[] { "TenantId", "BizType", "Phase", "Version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_FinReconcileRuns_TenantId_CreationTime",
                table: "FinReconcileRuns",
                columns: new[] { "TenantId", "CreationTime" });

            migrationBuilder.CreateIndex(
                name: "IX_FinSubAccounts_TenantId_AccountId_OwnerType_OwnerId_Currenc~",
                table: "FinSubAccounts",
                columns: new[] { "TenantId", "AccountId", "OwnerType", "OwnerId", "Currency", "Bucket" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_FinVouchers_TenantId_AgentId",
                table: "FinVouchers",
                columns: new[] { "TenantId", "AgentId" });

            migrationBuilder.CreateIndex(
                name: "IX_FinVouchers_TenantId_BizType_BizRefId_Phase_BatchKey",
                table: "FinVouchers",
                columns: new[] { "TenantId", "BizType", "BizRefId", "Phase", "BatchKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_FinVouchers_TenantId_IsDemo",
                table: "FinVouchers",
                columns: new[] { "TenantId", "IsDemo" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "FinAccounts");

            migrationBuilder.DropTable(
                name: "FinBalanceProjections");

            migrationBuilder.DropTable(
                name: "FinEntries");

            migrationBuilder.DropTable(
                name: "FinManualAdjusts");

            migrationBuilder.DropTable(
                name: "FinPeriods");

            migrationBuilder.DropTable(
                name: "FinPostingRules");

            migrationBuilder.DropTable(
                name: "FinReconcileRuns");

            migrationBuilder.DropTable(
                name: "FinSubAccounts");

            migrationBuilder.DropTable(
                name: "FinVouchers");
        }
    }
}
