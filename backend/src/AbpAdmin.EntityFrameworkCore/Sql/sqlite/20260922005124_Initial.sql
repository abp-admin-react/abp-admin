-- 框架库 SQLite 基线，对应原 EF 迁移 20260922005124_Initial。
-- 已记入 __EFMigrationsHistory 的库会跳过。后续改表追加新的 .sql，不要改本文件。
-- 记账缺失的存量库（更早迁移代次等）重放也安全——全部建表/建索引语句带 IF NOT EXISTS，已存在的对象原样保留。

CREATE TABLE IF NOT EXISTS "AbpAuditLogExcelFiles" (
    "Id" TEXT NOT NULL CONSTRAINT "PK_AbpAuditLogExcelFiles" PRIMARY KEY,
    "TenantId" TEXT NULL,
    "FileName" TEXT NULL,
    "CreationTime" TEXT NOT NULL,
    "CreatorId" TEXT NULL
);

CREATE TABLE IF NOT EXISTS "AbpAuditLogs" (
    "Id" TEXT NOT NULL CONSTRAINT "PK_AbpAuditLogs" PRIMARY KEY,
    "ApplicationName" TEXT NULL,
    "UserId" TEXT NULL,
    "UserName" TEXT NULL,
    "TenantId" TEXT NULL,
    "TenantName" TEXT NULL,
    "ImpersonatorUserId" TEXT NULL,
    "ImpersonatorUserName" TEXT NULL,
    "ImpersonatorTenantId" TEXT NULL,
    "ImpersonatorTenantName" TEXT NULL,
    "ExecutionTime" TEXT NOT NULL,
    "ExecutionDuration" INTEGER NOT NULL,
    "ClientIpAddress" TEXT NULL,
    "ClientName" TEXT NULL,
    "ClientId" TEXT NULL,
    "CorrelationId" TEXT NULL,
    "BrowserInfo" TEXT NULL,
    "HttpMethod" TEXT NULL,
    "Url" TEXT NULL,
    "Exceptions" TEXT NULL,
    "Comments" TEXT NULL,
    "HttpStatusCode" INTEGER NULL,
    "HandledAt" TEXT NULL,
    "HandledByName" TEXT NULL,
    "HandledByUserId" TEXT NULL,
    "HandledNote" TEXT NULL,
    "ExtraProperties" TEXT NOT NULL,
    "ConcurrencyStamp" TEXT NOT NULL
);

CREATE TABLE IF NOT EXISTS "AbpBackgroundJobs" (
    "Id" TEXT NOT NULL CONSTRAINT "PK_AbpBackgroundJobs" PRIMARY KEY,
    "ApplicationName" TEXT NULL,
    "JobName" TEXT NOT NULL,
    "JobArgs" TEXT NOT NULL,
    "TryCount" INTEGER NOT NULL DEFAULT 0,
    "CreationTime" TEXT NOT NULL,
    "NextTryTime" TEXT NOT NULL,
    "LastTryTime" TEXT NULL,
    "IsAbandoned" INTEGER NOT NULL DEFAULT 0,
    "CompletionTime" TEXT NULL,
    "Priority" INTEGER NOT NULL DEFAULT 15,
    "ExtraProperties" TEXT NOT NULL,
    "ConcurrencyStamp" TEXT NOT NULL
);

CREATE TABLE IF NOT EXISTS "AbpBlobContainers" (
    "Id" TEXT NOT NULL CONSTRAINT "PK_AbpBlobContainers" PRIMARY KEY,
    "TenantId" TEXT NULL,
    "Name" TEXT NOT NULL,
    "ExtraProperties" TEXT NOT NULL,
    "ConcurrencyStamp" TEXT NOT NULL
);

CREATE TABLE IF NOT EXISTS "AbpClaimTypes" (
    "Id" TEXT NOT NULL CONSTRAINT "PK_AbpClaimTypes" PRIMARY KEY,
    "Name" TEXT NOT NULL,
    "Required" INTEGER NOT NULL,
    "IsStatic" INTEGER NOT NULL,
    "Regex" TEXT NULL,
    "RegexDescription" TEXT NULL,
    "Description" TEXT NULL,
    "ValueType" INTEGER NOT NULL,
    "CreationTime" TEXT NOT NULL,
    "ExtraProperties" TEXT NOT NULL,
    "ConcurrencyStamp" TEXT NOT NULL
);

CREATE TABLE IF NOT EXISTS "AbpFeatureGroups" (
    "Id" TEXT NOT NULL CONSTRAINT "PK_AbpFeatureGroups" PRIMARY KEY,
    "Name" TEXT NOT NULL,
    "DisplayName" TEXT NOT NULL,
    "ExtraProperties" TEXT NULL
);

CREATE TABLE IF NOT EXISTS "AbpFeatures" (
    "Id" TEXT NOT NULL CONSTRAINT "PK_AbpFeatures" PRIMARY KEY,
    "GroupName" TEXT NOT NULL,
    "Name" TEXT NOT NULL,
    "ParentName" TEXT NULL,
    "DisplayName" TEXT NOT NULL,
    "Description" TEXT NULL,
    "DefaultValue" TEXT NULL,
    "IsVisibleToClients" INTEGER NOT NULL,
    "IsAvailableToHost" INTEGER NOT NULL,
    "AllowedProviders" TEXT NULL,
    "ValueType" TEXT NULL,
    "ExtraProperties" TEXT NULL
);

CREATE TABLE IF NOT EXISTS "AbpFeatureValues" (
    "Id" TEXT NOT NULL CONSTRAINT "PK_AbpFeatureValues" PRIMARY KEY,
    "Name" TEXT NOT NULL,
    "Value" TEXT NOT NULL,
    "ProviderName" TEXT NULL,
    "ProviderKey" TEXT NULL
);

CREATE TABLE IF NOT EXISTS "AbpLinkUsers" (
    "Id" TEXT NOT NULL CONSTRAINT "PK_AbpLinkUsers" PRIMARY KEY,
    "SourceUserId" TEXT NOT NULL,
    "SourceTenantId" TEXT NULL,
    "TargetUserId" TEXT NOT NULL,
    "TargetTenantId" TEXT NULL
);

CREATE TABLE IF NOT EXISTS "AbpOrganizationUnits" (
    "Id" TEXT NOT NULL CONSTRAINT "PK_AbpOrganizationUnits" PRIMARY KEY,
    "TenantId" TEXT NULL,
    "ParentId" TEXT NULL,
    "Code" TEXT NOT NULL,
    "DisplayName" TEXT NOT NULL,
    "EntityVersion" INTEGER NOT NULL,
    "ExtraProperties" TEXT NOT NULL,
    "ConcurrencyStamp" TEXT NOT NULL,
    "CreationTime" TEXT NOT NULL,
    "CreatorId" TEXT NULL,
    "LastModificationTime" TEXT NULL,
    "LastModifierId" TEXT NULL,
    "IsDeleted" INTEGER NOT NULL DEFAULT 0,
    "DeleterId" TEXT NULL,
    "DeletionTime" TEXT NULL,
    CONSTRAINT "FK_AbpOrganizationUnits_AbpOrganizationUnits_ParentId" FOREIGN KEY ("ParentId") REFERENCES "AbpOrganizationUnits" ("Id")
);

CREATE TABLE IF NOT EXISTS "AbpPermissionGrants" (
    "Id" TEXT NOT NULL CONSTRAINT "PK_AbpPermissionGrants" PRIMARY KEY,
    "TenantId" TEXT NULL,
    "Name" TEXT NOT NULL,
    "ProviderName" TEXT NOT NULL,
    "ProviderKey" TEXT NOT NULL
);

CREATE TABLE IF NOT EXISTS "AbpPermissionGroups" (
    "Id" TEXT NOT NULL CONSTRAINT "PK_AbpPermissionGroups" PRIMARY KEY,
    "Name" TEXT NOT NULL,
    "DisplayName" TEXT NOT NULL,
    "ExtraProperties" TEXT NULL
);

CREATE TABLE IF NOT EXISTS "AbpPermissions" (
    "Id" TEXT NOT NULL CONSTRAINT "PK_AbpPermissions" PRIMARY KEY,
    "GroupName" TEXT NULL,
    "Name" TEXT NOT NULL,
    "ResourceName" TEXT NULL,
    "ManagementPermissionName" TEXT NULL,
    "ParentName" TEXT NULL,
    "DisplayName" TEXT NOT NULL,
    "IsEnabled" INTEGER NOT NULL,
    "MultiTenancySide" INTEGER NOT NULL,
    "Providers" TEXT NULL,
    "StateCheckers" TEXT NULL,
    "ExtraProperties" TEXT NULL
);

CREATE TABLE IF NOT EXISTS "AbpResourcePermissionGrants" (
    "Id" TEXT NOT NULL CONSTRAINT "PK_AbpResourcePermissionGrants" PRIMARY KEY,
    "TenantId" TEXT NULL,
    "Name" TEXT NOT NULL,
    "ProviderName" TEXT NOT NULL,
    "ProviderKey" TEXT NOT NULL,
    "ResourceName" TEXT NOT NULL,
    "ResourceKey" TEXT NOT NULL
);

CREATE TABLE IF NOT EXISTS "AbpRoles" (
    "Id" TEXT NOT NULL CONSTRAINT "PK_AbpRoles" PRIMARY KEY,
    "TenantId" TEXT NULL,
    "Name" TEXT NOT NULL,
    "NormalizedName" TEXT NOT NULL,
    "IsDefault" INTEGER NOT NULL,
    "IsStatic" INTEGER NOT NULL,
    "IsPublic" INTEGER NOT NULL,
    "EntityVersion" INTEGER NOT NULL,
    "CreationTime" TEXT NOT NULL,
    "ExtraProperties" TEXT NOT NULL,
    "ConcurrencyStamp" TEXT NOT NULL
);

CREATE TABLE IF NOT EXISTS "AbpSecurityLogs" (
    "Id" TEXT NOT NULL CONSTRAINT "PK_AbpSecurityLogs" PRIMARY KEY,
    "TenantId" TEXT NULL,
    "ApplicationName" TEXT NULL,
    "Identity" TEXT NULL,
    "Action" TEXT NULL,
    "UserId" TEXT NULL,
    "UserName" TEXT NULL,
    "TenantName" TEXT NULL,
    "ClientId" TEXT NULL,
    "CorrelationId" TEXT NULL,
    "ClientIpAddress" TEXT NULL,
    "BrowserInfo" TEXT NULL,
    "CreationTime" TEXT NOT NULL,
    "ExtraProperties" TEXT NOT NULL,
    "ConcurrencyStamp" TEXT NOT NULL
);

CREATE TABLE IF NOT EXISTS "AbpSessions" (
    "Id" TEXT NOT NULL CONSTRAINT "PK_AbpSessions" PRIMARY KEY,
    "SessionId" TEXT NOT NULL,
    "Device" TEXT NOT NULL,
    "DeviceInfo" TEXT NULL,
    "TenantId" TEXT NULL,
    "UserId" TEXT NOT NULL,
    "ClientId" TEXT NULL,
    "IpAddresses" TEXT NULL,
    "SignedIn" TEXT NOT NULL,
    "LastAccessed" TEXT NULL,
    "ExtraProperties" TEXT NULL
);

CREATE TABLE IF NOT EXISTS "AbpSettingDefinitions" (
    "Id" TEXT NOT NULL CONSTRAINT "PK_AbpSettingDefinitions" PRIMARY KEY,
    "Name" TEXT NOT NULL,
    "DisplayName" TEXT NOT NULL,
    "Description" TEXT NULL,
    "DefaultValue" TEXT NULL,
    "IsVisibleToClients" INTEGER NOT NULL,
    "Providers" TEXT NULL,
    "IsInherited" INTEGER NOT NULL,
    "IsEncrypted" INTEGER NOT NULL,
    "ExtraProperties" TEXT NULL
);

CREATE TABLE IF NOT EXISTS "AbpSettings" (
    "Id" TEXT NOT NULL CONSTRAINT "PK_AbpSettings" PRIMARY KEY,
    "Name" TEXT NOT NULL,
    "Value" TEXT NOT NULL,
    "ProviderName" TEXT NULL,
    "ProviderKey" TEXT NULL
);

CREATE TABLE IF NOT EXISTS "AbpTenants" (
    "Id" TEXT NOT NULL CONSTRAINT "PK_AbpTenants" PRIMARY KEY,
    "Name" TEXT NOT NULL,
    "NormalizedName" TEXT NOT NULL,
    "EntityVersion" INTEGER NOT NULL,
    "ActivationEndDate" TEXT NULL,
    "ActivationState" INTEGER NOT NULL DEFAULT 0,
    "EditionEndDateUtc" TEXT NULL,
    "ExtraProperties" TEXT NOT NULL,
    "ConcurrencyStamp" TEXT NOT NULL,
    "CreationTime" TEXT NOT NULL,
    "CreatorId" TEXT NULL,
    "LastModificationTime" TEXT NULL,
    "LastModifierId" TEXT NULL,
    "IsDeleted" INTEGER NOT NULL DEFAULT 0,
    "DeleterId" TEXT NULL,
    "DeletionTime" TEXT NULL
);

CREATE TABLE IF NOT EXISTS "AbpUserDelegations" (
    "Id" TEXT NOT NULL CONSTRAINT "PK_AbpUserDelegations" PRIMARY KEY,
    "TenantId" TEXT NULL,
    "SourceUserId" TEXT NOT NULL,
    "TargetUserId" TEXT NOT NULL,
    "StartTime" TEXT NOT NULL,
    "EndTime" TEXT NOT NULL
);

CREATE TABLE IF NOT EXISTS "AbpUsers" (
    "Id" TEXT NOT NULL CONSTRAINT "PK_AbpUsers" PRIMARY KEY,
    "TenantId" TEXT NULL,
    "UserName" TEXT NOT NULL,
    "NormalizedUserName" TEXT NOT NULL,
    "Name" TEXT NULL,
    "Surname" TEXT NULL,
    "Email" TEXT NOT NULL,
    "NormalizedEmail" TEXT NOT NULL,
    "EmailConfirmed" INTEGER NOT NULL DEFAULT 0,
    "PasswordHash" TEXT NULL,
    "SecurityStamp" TEXT NOT NULL,
    "IsExternal" INTEGER NOT NULL DEFAULT 0,
    "PhoneNumber" TEXT NULL,
    "PhoneNumberConfirmed" INTEGER NOT NULL DEFAULT 0,
    "IsActive" INTEGER NOT NULL,
    "TwoFactorEnabled" INTEGER NOT NULL DEFAULT 0,
    "LockoutEnd" TEXT NULL,
    "LockoutEnabled" INTEGER NOT NULL DEFAULT 0,
    "AccessFailedCount" INTEGER NOT NULL DEFAULT 0,
    "ShouldChangePasswordOnNextLogin" INTEGER NOT NULL,
    "EntityVersion" INTEGER NOT NULL,
    "LastPasswordChangeTime" TEXT NULL,
    "LastSignInTime" TEXT NULL,
    "Leaved" INTEGER NOT NULL DEFAULT 0,
    "ExtraProperties" TEXT NOT NULL,
    "ConcurrencyStamp" TEXT NOT NULL,
    "CreationTime" TEXT NOT NULL,
    "CreatorId" TEXT NULL,
    "LastModificationTime" TEXT NULL,
    "LastModifierId" TEXT NULL,
    "IsDeleted" INTEGER NOT NULL DEFAULT 0,
    "DeleterId" TEXT NULL,
    "DeletionTime" TEXT NULL
);

CREATE TABLE IF NOT EXISTS "AppDataDictionaryItemMetas" (
    "Id" TEXT NOT NULL CONSTRAINT "PK_AppDataDictionaryItemMetas" PRIMARY KEY,
    "TenantId" TEXT NULL,
    "DictionaryCode" TEXT NOT NULL,
    "ItemCode" TEXT NOT NULL,
    "TagType" TEXT NULL,
    "Order" INTEGER NOT NULL,
    "CreationTime" TEXT NOT NULL,
    "CreatorId" TEXT NULL,
    "LastModificationTime" TEXT NULL,
    "LastModifierId" TEXT NULL
);

CREATE TABLE IF NOT EXISTS "AppDataScopeDemos" (
    "Id" TEXT NOT NULL CONSTRAINT "PK_AppDataScopeDemos" PRIMARY KEY,
    "TenantId" TEXT NULL,
    "OrganizationUnitId" TEXT NULL,
    "Name" TEXT NOT NULL,
    "ExtraProperties" TEXT NOT NULL,
    "ConcurrencyStamp" TEXT NOT NULL,
    "CreationTime" TEXT NOT NULL,
    "CreatorId" TEXT NULL,
    "LastModificationTime" TEXT NULL,
    "LastModifierId" TEXT NULL
);

CREATE TABLE IF NOT EXISTS "AppEditions" (
    "Id" TEXT NOT NULL CONSTRAINT "PK_AppEditions" PRIMARY KEY,
    "DisplayName" TEXT NOT NULL,
    "ExtraProperties" TEXT NOT NULL,
    "ConcurrencyStamp" TEXT NOT NULL,
    "CreationTime" TEXT NOT NULL,
    "CreatorId" TEXT NULL,
    "LastModificationTime" TEXT NULL,
    "LastModifierId" TEXT NULL
);

CREATE TABLE IF NOT EXISTS "AppFileShareLinks" (
    "Id" TEXT NOT NULL CONSTRAINT "PK_AppFileShareLinks" PRIMARY KEY,
    "TenantId" TEXT NULL,
    "FileId" TEXT NOT NULL,
    "Token" TEXT NOT NULL,
    "ExpireTime" TEXT NOT NULL,
    "IsPublic" INTEGER NOT NULL,
    "DownloadCount" INTEGER NOT NULL,
    "MaxDownloads" INTEGER NULL,
    "ExtraProperties" TEXT NOT NULL,
    "ConcurrencyStamp" TEXT NOT NULL,
    "CreationTime" TEXT NOT NULL,
    "CreatorId" TEXT NULL
);

CREATE TABLE IF NOT EXISTS "AppFileThumbnails" (
    "Id" TEXT NOT NULL CONSTRAINT "PK_AppFileThumbnails" PRIMARY KEY,
    "TenantId" TEXT NULL,
    "FileId" TEXT NOT NULL,
    "BlobName" TEXT NULL,
    "Width" INTEGER NOT NULL,
    "Height" INTEGER NOT NULL,
    "State" INTEGER NOT NULL,
    "FailureReason" TEXT NULL,
    "CreationTime" TEXT NOT NULL,
    "CreatorId" TEXT NULL,
    "LastModificationTime" TEXT NULL,
    "LastModifierId" TEXT NULL
);

CREATE TABLE IF NOT EXISTS "AppGdprInfos" (
    "Id" TEXT NOT NULL CONSTRAINT "PK_AppGdprInfos" PRIMARY KEY,
    "TenantId" TEXT NULL,
    "RequestId" TEXT NOT NULL,
    "Data" TEXT NOT NULL,
    "Provider" TEXT NOT NULL,
    "ExtraProperties" TEXT NOT NULL,
    "ConcurrencyStamp" TEXT NOT NULL,
    "CreationTime" TEXT NOT NULL,
    "CreatorId" TEXT NULL,
    "LastModificationTime" TEXT NULL,
    "LastModifierId" TEXT NULL
);

CREATE TABLE IF NOT EXISTS "AppGdprRequests" (
    "Id" TEXT NOT NULL CONSTRAINT "PK_AppGdprRequests" PRIMARY KEY,
    "TenantId" TEXT NULL,
    "UserId" TEXT NOT NULL,
    "CreationTime" TEXT NOT NULL,
    "ReadyTime" TEXT NOT NULL
);

CREATE TABLE IF NOT EXISTS "AppIdentityUserPosts" (
    "UserId" TEXT NOT NULL,
    "PostId" TEXT NOT NULL,
    "TenantId" TEXT NULL,
    CONSTRAINT "PK_AppIdentityUserPosts" PRIMARY KEY ("UserId", "PostId")
);

CREATE TABLE IF NOT EXISTS "AppLanguages" (
    "Id" TEXT NOT NULL CONSTRAINT "PK_AppLanguages" PRIMARY KEY,
    "CultureName" TEXT NOT NULL,
    "UiCultureName" TEXT NOT NULL,
    "DisplayName" TEXT NOT NULL,
    "FlagIcon" TEXT NULL,
    "IsEnabled" INTEGER NOT NULL,
    "IsDefault" INTEGER NOT NULL,
    "ExtraProperties" TEXT NOT NULL,
    "ConcurrencyStamp" TEXT NOT NULL,
    "CreationTime" TEXT NOT NULL,
    "CreatorId" TEXT NULL,
    "LastModificationTime" TEXT NULL,
    "LastModifierId" TEXT NULL
);

CREATE TABLE IF NOT EXISTS "AppLanguageTexts" (
    "Id" TEXT NOT NULL CONSTRAINT "PK_AppLanguageTexts" PRIMARY KEY,
    "TenantId" TEXT NULL,
    "ResourceName" TEXT NOT NULL,
    "CultureName" TEXT NOT NULL,
    "Name" TEXT NOT NULL,
    "Value" TEXT NOT NULL,
    "ExtraProperties" TEXT NOT NULL,
    "ConcurrencyStamp" TEXT NOT NULL,
    "CreationTime" TEXT NOT NULL,
    "CreatorId" TEXT NULL,
    "LastModificationTime" TEXT NULL,
    "LastModifierId" TEXT NULL
);

CREATE TABLE IF NOT EXISTS "AppMenus" (
    "Id" TEXT NOT NULL CONSTRAINT "PK_AppMenus" PRIMARY KEY,
    "TenantId" TEXT NULL,
    "ParentId" TEXT NULL,
    "Type" INTEGER NOT NULL,
    "Title" TEXT NOT NULL,
    "Name" TEXT NULL,
    "Path" TEXT NULL,
    "Icon" TEXT NULL,
    "OrderNo" INTEGER NOT NULL,
    "IsHide" INTEGER NOT NULL,
    "IsEnabled" INTEGER NOT NULL,
    "PermissionName" TEXT NULL,
    "Remark" TEXT NULL,
    "ExtraProperties" TEXT NOT NULL,
    "ConcurrencyStamp" TEXT NOT NULL,
    "CreationTime" TEXT NOT NULL,
    "CreatorId" TEXT NULL,
    "LastModificationTime" TEXT NULL,
    "LastModifierId" TEXT NULL,
    "IsDeleted" INTEGER NOT NULL DEFAULT 0,
    "DeleterId" TEXT NULL,
    "DeletionTime" TEXT NULL
);

CREATE TABLE IF NOT EXISTS "AppNotificationBroadcasts" (
    "Id" TEXT NOT NULL CONSTRAINT "PK_AppNotificationBroadcasts" PRIMARY KEY,
    "TenantId" TEXT NULL,
    "TargetType" TEXT NOT NULL,
    "TargetId" TEXT NULL,
    "NotificationMethods" TEXT NOT NULL,
    "Title" TEXT NOT NULL,
    "Body" TEXT NOT NULL,
    "SmsText" TEXT NULL,
    "SmsPropertiesJson" TEXT NULL,
    "TotalCount" INTEGER NOT NULL,
    "SentCount" INTEGER NOT NULL,
    "FailedCount" INTEGER NOT NULL,
    "LastProcessedUserId" TEXT NULL,
    "State" TEXT NOT NULL,
    "CompletionTime" TEXT NULL,
    "ExtraProperties" TEXT NOT NULL,
    "ConcurrencyStamp" TEXT NOT NULL,
    "CreationTime" TEXT NOT NULL,
    "CreatorId" TEXT NULL,
    "LastModificationTime" TEXT NULL,
    "LastModifierId" TEXT NULL
);

CREATE TABLE IF NOT EXISTS "AppOperationLogs" (
    "Id" TEXT NOT NULL CONSTRAINT "PK_AppOperationLogs" PRIMARY KEY,
    "TenantId" TEXT NULL,
    "UserId" TEXT NULL,
    "UserName" TEXT NULL,
    "Type" TEXT NOT NULL,
    "SubType" TEXT NOT NULL,
    "BizId" TEXT NULL,
    "Action" TEXT NULL,
    "Extra" TEXT NULL,
    "Success" INTEGER NOT NULL,
    "ErrorMessage" TEXT NULL,
    "RequestMethod" TEXT NULL,
    "RequestUrl" TEXT NULL,
    "ClientIpAddress" TEXT NULL,
    "UserAgent" TEXT NULL,
    "CorrelationId" TEXT NULL,
    "Duration" INTEGER NOT NULL,
    "ExecutionTime" TEXT NOT NULL
);

CREATE TABLE IF NOT EXISTS "AppPosts" (
    "Id" TEXT NOT NULL CONSTRAINT "PK_AppPosts" PRIMARY KEY,
    "TenantId" TEXT NULL,
    "Name" TEXT NOT NULL,
    "Code" TEXT NOT NULL,
    "SortOrder" INTEGER NOT NULL,
    "Status" INTEGER NOT NULL,
    "Remark" TEXT NULL,
    "ExtraProperties" TEXT NOT NULL,
    "ConcurrencyStamp" TEXT NOT NULL,
    "CreationTime" TEXT NOT NULL,
    "CreatorId" TEXT NULL,
    "LastModificationTime" TEXT NULL,
    "LastModifierId" TEXT NULL,
    "IsDeleted" INTEGER NOT NULL DEFAULT 0,
    "DeleterId" TEXT NULL,
    "DeletionTime" TEXT NULL
);

CREATE TABLE IF NOT EXISTS "AppRoleDataScopes" (
    "Id" TEXT NOT NULL CONSTRAINT "PK_AppRoleDataScopes" PRIMARY KEY,
    "TenantId" TEXT NULL,
    "RoleName" TEXT NOT NULL,
    "ScopeType" INTEGER NOT NULL,
    "ExtraProperties" TEXT NOT NULL,
    "ConcurrencyStamp" TEXT NOT NULL,
    "CreationTime" TEXT NOT NULL,
    "CreatorId" TEXT NULL,
    "LastModificationTime" TEXT NULL,
    "LastModifierId" TEXT NULL
);

CREATE TABLE IF NOT EXISTS "AppScheduledJobExecutions" (
    "Id" TEXT NOT NULL CONSTRAINT "PK_AppScheduledJobExecutions" PRIMARY KEY,
    "TenantId" TEXT NULL,
    "ScheduledJobId" TEXT NOT NULL,
    "StartTime" TEXT NOT NULL,
    "EndTime" TEXT NULL,
    "Success" INTEGER NOT NULL,
    "Message" TEXT NULL,
    "DurationMs" INTEGER NOT NULL,
    "CreationTime" TEXT NOT NULL
);

CREATE TABLE IF NOT EXISTS "AppScheduledJobs" (
    "Id" TEXT NOT NULL CONSTRAINT "PK_AppScheduledJobs" PRIMARY KEY,
    "TenantId" TEXT NULL,
    "Name" TEXT NOT NULL,
    "JobType" TEXT NOT NULL,
    "CronExpression" TEXT NOT NULL,
    "IsEnabled" INTEGER NOT NULL,
    "Description" TEXT NULL,
    "Payload" TEXT NULL,
    "LastRunTime" TEXT NULL,
    "LastRunSuccess" INTEGER NULL,
    "LastRunMessage" TEXT NULL,
    "NextRunTime" TEXT NULL,
    "ExtraProperties" TEXT NOT NULL,
    "ConcurrencyStamp" TEXT NOT NULL,
    "CreationTime" TEXT NOT NULL,
    "CreatorId" TEXT NULL,
    "LastModificationTime" TEXT NULL,
    "LastModifierId" TEXT NULL,
    "IsDeleted" INTEGER NOT NULL DEFAULT 0,
    "DeleterId" TEXT NULL,
    "DeletionTime" TEXT NULL
);

CREATE TABLE IF NOT EXISTS "AppTenantPackages" (
    "Id" TEXT NOT NULL CONSTRAINT "PK_AppTenantPackages" PRIMARY KEY,
    "Name" TEXT NOT NULL,
    "Remark" TEXT NULL,
    "ExtraProperties" TEXT NOT NULL,
    "ConcurrencyStamp" TEXT NOT NULL,
    "CreationTime" TEXT NOT NULL,
    "CreatorId" TEXT NULL,
    "LastModificationTime" TEXT NULL,
    "LastModifierId" TEXT NULL,
    "IsDeleted" INTEGER NOT NULL DEFAULT 0,
    "DeleterId" TEXT NULL,
    "DeletionTime" TEXT NULL
);

CREATE TABLE IF NOT EXISTS "AppTextTemplateContents" (
    "Id" TEXT NOT NULL CONSTRAINT "PK_AppTextTemplateContents" PRIMARY KEY,
    "TenantId" TEXT NULL,
    "Name" TEXT NOT NULL,
    "CultureName" TEXT NULL,
    "Content" TEXT NOT NULL,
    "ExtraProperties" TEXT NOT NULL,
    "ConcurrencyStamp" TEXT NOT NULL,
    "CreationTime" TEXT NOT NULL,
    "CreatorId" TEXT NULL,
    "LastModificationTime" TEXT NULL,
    "LastModifierId" TEXT NULL
);

CREATE TABLE IF NOT EXISTS "AppUserExcelFiles" (
    "Id" TEXT NOT NULL CONSTRAINT "PK_AppUserExcelFiles" PRIMARY KEY,
    "TenantId" TEXT NULL,
    "FileName" TEXT NOT NULL,
    "CreationTime" TEXT NOT NULL,
    "CreatorId" TEXT NULL
);

CREATE TABLE IF NOT EXISTS "EasyAbpAbpDataDictionaryDataDictionaries" (
    "Id" TEXT NOT NULL CONSTRAINT "PK_EasyAbpAbpDataDictionaryDataDictionaries" PRIMARY KEY,
    "TenantId" TEXT NULL,
    "Code" TEXT NOT NULL,
    "DisplayText" TEXT NOT NULL,
    "Description" TEXT NULL,
    "IsStatic" INTEGER NOT NULL,
    "ExtraProperties" TEXT NOT NULL,
    "ConcurrencyStamp" TEXT NOT NULL,
    "CreationTime" TEXT NOT NULL,
    "CreatorId" TEXT NULL,
    "LastModificationTime" TEXT NULL,
    "LastModifierId" TEXT NULL,
    "IsDeleted" INTEGER NOT NULL DEFAULT 0,
    "DeleterId" TEXT NULL,
    "DeletionTime" TEXT NULL
);

CREATE TABLE IF NOT EXISTS "EasyAbpFileManagementFiles" (
    "Id" TEXT NOT NULL CONSTRAINT "PK_EasyAbpFileManagementFiles" PRIMARY KEY,
    "TenantId" TEXT NULL,
    "ParentId" TEXT NULL,
    "FileContainerName" TEXT NULL,
    "FileName" TEXT NULL,
    "MimeType" TEXT NULL,
    "FileType" INTEGER NOT NULL,
    "SubFilesQuantity" INTEGER NOT NULL,
    "HasSubdirectories" INTEGER NOT NULL,
    "ByteSize" INTEGER NOT NULL,
    "Hash" TEXT NULL,
    "BlobName" TEXT NULL,
    "OwnerUserId" TEXT NULL,
    "Flag" TEXT NULL,
    "SoftDeletionToken" TEXT NULL DEFAULT '',
    "ExtraProperties" TEXT NOT NULL,
    "ConcurrencyStamp" TEXT NOT NULL,
    "CreationTime" TEXT NOT NULL,
    "CreatorId" TEXT NULL,
    "LastModificationTime" TEXT NULL,
    "LastModifierId" TEXT NULL,
    "IsDeleted" INTEGER NOT NULL DEFAULT 0,
    "DeleterId" TEXT NULL,
    "DeletionTime" TEXT NULL
);

CREATE TABLE IF NOT EXISTS "EasyAbpFileManagementUsers" (
    "Id" TEXT NOT NULL CONSTRAINT "PK_EasyAbpFileManagementUsers" PRIMARY KEY,
    "TenantId" TEXT NULL,
    "UserName" TEXT NOT NULL,
    "Email" TEXT NOT NULL,
    "Name" TEXT NULL,
    "Surname" TEXT NULL,
    "IsActive" INTEGER NOT NULL,
    "EmailConfirmed" INTEGER NOT NULL DEFAULT 0,
    "PhoneNumber" TEXT NULL,
    "PhoneNumberConfirmed" INTEGER NOT NULL DEFAULT 0,
    "ExtraProperties" TEXT NOT NULL,
    "ConcurrencyStamp" TEXT NOT NULL
);

CREATE TABLE IF NOT EXISTS "EasyAbpNotificationServiceNotificationInfos" (
    "Id" TEXT NOT NULL CONSTRAINT "PK_EasyAbpNotificationServiceNotificationInfos" PRIMARY KEY,
    "TenantId" TEXT NULL,
    "ExtraProperties" TEXT NOT NULL,
    "ConcurrencyStamp" TEXT NOT NULL,
    "CreationTime" TEXT NOT NULL,
    "CreatorId" TEXT NULL,
    "LastModificationTime" TEXT NULL,
    "LastModifierId" TEXT NULL,
    "IsDeleted" INTEGER NOT NULL DEFAULT 0,
    "DeleterId" TEXT NULL,
    "DeletionTime" TEXT NULL
);

CREATE TABLE IF NOT EXISTS "EasyAbpNotificationServiceNotifications" (
    "Id" TEXT NOT NULL CONSTRAINT "PK_EasyAbpNotificationServiceNotifications" PRIMARY KEY,
    "TenantId" TEXT NULL,
    "UserId" TEXT NOT NULL,
    "UserName" TEXT NULL,
    "NotificationInfoId" TEXT NOT NULL,
    "NotificationMethod" TEXT NULL,
    "Success" INTEGER NULL,
    "CompletionTime" TEXT NULL,
    "FailureReason" TEXT NULL,
    "RetryForNotificationId" TEXT NULL,
    "ExtraProperties" TEXT NOT NULL,
    "ConcurrencyStamp" TEXT NOT NULL,
    "CreationTime" TEXT NOT NULL,
    "CreatorId" TEXT NULL
);

CREATE TABLE IF NOT EXISTS "EasyAbpPaymentServicePayments" (
    "Id" TEXT NOT NULL CONSTRAINT "PK_EasyAbpPaymentServicePayments" PRIMARY KEY,
    "TenantId" TEXT NULL,
    "UserId" TEXT NOT NULL,
    "PaymentMethod" TEXT NULL,
    "PayeeAccount" TEXT NULL,
    "ExternalTradingCode" TEXT NULL,
    "Currency" TEXT NULL,
    "OriginalPaymentAmount" decimal(20,8) NOT NULL,
    "PaymentDiscount" decimal(20,8) NOT NULL,
    "ActualPaymentAmount" decimal(20,8) NOT NULL,
    "RefundAmount" decimal(20,8) NOT NULL,
    "PendingRefundAmount" decimal(20,8) NOT NULL,
    "CompletionTime" TEXT NULL,
    "CanceledTime" TEXT NULL,
    "ExtraProperties" TEXT NOT NULL,
    "ConcurrencyStamp" TEXT NOT NULL,
    "CreationTime" TEXT NOT NULL,
    "CreatorId" TEXT NULL,
    "LastModificationTime" TEXT NULL,
    "LastModifierId" TEXT NULL,
    "IsDeleted" INTEGER NOT NULL DEFAULT 0,
    "DeleterId" TEXT NULL,
    "DeletionTime" TEXT NULL
);

CREATE TABLE IF NOT EXISTS "EasyAbpPaymentServicePrepaymentAccounts" (
    "Id" TEXT NOT NULL CONSTRAINT "PK_EasyAbpPaymentServicePrepaymentAccounts" PRIMARY KEY,
    "TenantId" TEXT NULL,
    "AccountGroupName" TEXT NULL,
    "UserId" TEXT NOT NULL,
    "Balance" decimal(20,8) NOT NULL,
    "LockedBalance" decimal(20,8) NOT NULL,
    "PendingTopUpPaymentId" TEXT NULL,
    "PendingWithdrawalRecordId" TEXT NULL,
    "PendingWithdrawalAmount" decimal(20,8) NOT NULL,
    "ExtraProperties" TEXT NOT NULL,
    "ConcurrencyStamp" TEXT NOT NULL,
    "CreationTime" TEXT NOT NULL,
    "CreatorId" TEXT NULL,
    "LastModificationTime" TEXT NULL,
    "LastModifierId" TEXT NULL,
    "IsDeleted" INTEGER NOT NULL DEFAULT 0,
    "DeleterId" TEXT NULL,
    "DeletionTime" TEXT NULL
);

CREATE TABLE IF NOT EXISTS "EasyAbpPaymentServicePrepaymentTransactions" (
    "Id" TEXT NOT NULL CONSTRAINT "PK_EasyAbpPaymentServicePrepaymentTransactions" PRIMARY KEY,
    "TenantId" TEXT NULL,
    "AccountId" TEXT NOT NULL,
    "AccountUserId" TEXT NOT NULL,
    "PaymentId" TEXT NULL,
    "TransactionType" INTEGER NOT NULL,
    "ActionName" TEXT NULL,
    "PaymentMethod" TEXT NULL,
    "ExternalTradingCode" TEXT NULL,
    "Currency" TEXT NULL,
    "ChangedBalance" decimal(20,8) NOT NULL,
    "OriginalBalance" decimal(20,8) NOT NULL,
    "ExtraProperties" TEXT NOT NULL,
    "ConcurrencyStamp" TEXT NOT NULL,
    "CreationTime" TEXT NOT NULL,
    "CreatorId" TEXT NULL
);

CREATE TABLE IF NOT EXISTS "EasyAbpPaymentServicePrepaymentWithdrawalRecords" (
    "Id" TEXT NOT NULL CONSTRAINT "PK_EasyAbpPaymentServicePrepaymentWithdrawalRecords" PRIMARY KEY,
    "TenantId" TEXT NULL,
    "AccountId" TEXT NOT NULL,
    "WithdrawalMethod" TEXT NULL,
    "Amount" decimal(20,8) NOT NULL,
    "CompletionTime" TEXT NULL,
    "CancellationTime" TEXT NULL,
    "ResultErrorCode" TEXT NULL,
    "ResultErrorMessage" TEXT NULL,
    "ExtraProperties" TEXT NOT NULL,
    "ConcurrencyStamp" TEXT NOT NULL,
    "CreationTime" TEXT NOT NULL,
    "CreatorId" TEXT NULL,
    "LastModificationTime" TEXT NULL,
    "LastModifierId" TEXT NULL,
    "IsDeleted" INTEGER NOT NULL DEFAULT 0,
    "DeleterId" TEXT NULL,
    "DeletionTime" TEXT NULL
);

CREATE TABLE IF NOT EXISTS "EasyAbpPaymentServicePrepaymentWithdrawalRequests" (
    "Id" TEXT NOT NULL CONSTRAINT "PK_EasyAbpPaymentServicePrepaymentWithdrawalRequests" PRIMARY KEY,
    "TenantId" TEXT NULL,
    "AccountId" TEXT NOT NULL,
    "AccountUserId" TEXT NOT NULL,
    "Amount" decimal(20,8) NOT NULL,
    "ReviewTime" TEXT NULL,
    "ReviewerUserId" TEXT NULL,
    "IsApproved" INTEGER NULL,
    "ExtraProperties" TEXT NOT NULL,
    "ConcurrencyStamp" TEXT NOT NULL,
    "CreationTime" TEXT NOT NULL,
    "CreatorId" TEXT NULL,
    "LastModificationTime" TEXT NULL,
    "LastModifierId" TEXT NULL,
    "IsDeleted" INTEGER NOT NULL DEFAULT 0,
    "DeleterId" TEXT NULL,
    "DeletionTime" TEXT NULL
);

CREATE TABLE IF NOT EXISTS "EasyAbpPaymentServiceRefunds" (
    "Id" TEXT NOT NULL CONSTRAINT "PK_EasyAbpPaymentServiceRefunds" PRIMARY KEY,
    "TenantId" TEXT NULL,
    "PaymentId" TEXT NOT NULL,
    "RefundPaymentMethod" TEXT NULL,
    "ExternalTradingCode" TEXT NULL,
    "Currency" TEXT NULL,
    "RefundAmount" decimal(20,8) NOT NULL,
    "DisplayReason" TEXT NULL,
    "CustomerRemark" TEXT NULL,
    "StaffRemark" TEXT NULL,
    "CompletedTime" TEXT NULL,
    "CanceledTime" TEXT NULL,
    "ExtraProperties" TEXT NOT NULL,
    "ConcurrencyStamp" TEXT NOT NULL,
    "CreationTime" TEXT NOT NULL,
    "CreatorId" TEXT NULL,
    "LastModificationTime" TEXT NULL,
    "LastModifierId" TEXT NULL,
    "IsDeleted" INTEGER NOT NULL DEFAULT 0,
    "DeleterId" TEXT NULL,
    "DeletionTime" TEXT NULL
);

CREATE TABLE IF NOT EXISTS "EasyAbpPaymentServiceWeChatPayPaymentRecords" (
    "Id" TEXT NOT NULL CONSTRAINT "PK_EasyAbpPaymentServiceWeChatPayPaymentRecords" PRIMARY KEY,
    "TenantId" TEXT NULL,
    "PaymentId" TEXT NOT NULL,
    "AppId" TEXT NULL,
    "MchId" TEXT NULL,
    "OutTradeNo" TEXT NULL,
    "TransactionId" TEXT NULL,
    "TradeType" TEXT NULL,
    "BankType" TEXT NULL,
    "Attach" TEXT NULL,
    "ReturnCode" TEXT NULL,
    "ReturnMsg" TEXT NULL,
    "DeviceInfo" TEXT NULL,
    "ResultCode" TEXT NULL,
    "ErrCode" TEXT NULL,
    "ErrCodeDes" TEXT NULL,
    "Openid" TEXT NULL,
    "IsSubscribe" TEXT NULL,
    "TotalFee" INTEGER NOT NULL,
    "SettlementTotalFee" INTEGER NULL,
    "FeeType" TEXT NULL,
    "CashFee" INTEGER NOT NULL,
    "CashFeeType" TEXT NULL,
    "CouponFee" INTEGER NULL,
    "CouponCount" INTEGER NULL,
    "CouponTypes" TEXT NULL,
    "CouponIds" TEXT NULL,
    "CouponFees" TEXT NULL,
    "TimeEnd" TEXT NULL,
    "TradeState" TEXT NULL,
    "TradeStateDesc" TEXT NULL,
    "SuccessTime" TEXT NULL,
    "Payer" TEXT NULL,
    "Amount" TEXT NULL,
    "SceneInfo" TEXT NULL,
    "PromotionDetail" TEXT NULL,
    "ExtraProperties" TEXT NOT NULL,
    "ConcurrencyStamp" TEXT NOT NULL,
    "CreationTime" TEXT NOT NULL,
    "CreatorId" TEXT NULL
);

CREATE TABLE IF NOT EXISTS "EasyAbpPaymentServiceWeChatPayRefundRecords" (
    "Id" TEXT NOT NULL CONSTRAINT "PK_EasyAbpPaymentServiceWeChatPayRefundRecords" PRIMARY KEY,
    "TenantId" TEXT NULL,
    "PaymentId" TEXT NOT NULL,
    "OutRefundNo" TEXT NULL,
    "TransactionId" TEXT NULL,
    "OutTradeNo" TEXT NULL,
    "SuccessTime" TEXT NULL,
    "ReturnCode" TEXT NULL,
    "ReturnMsg" TEXT NULL,
    "AppId" TEXT NULL,
    "MchId" TEXT NULL,
    "RefundId" TEXT NULL,
    "TotalFee" INTEGER NOT NULL,
    "SettlementTotalFee" INTEGER NULL,
    "RefundFee" INTEGER NOT NULL,
    "SettlementRefundFee" INTEGER NULL,
    "FeeType" TEXT NULL,
    "CashFee" INTEGER NOT NULL,
    "CashFeeType" TEXT NULL,
    "CashRefundFee" INTEGER NULL,
    "CouponRefundFee" INTEGER NULL,
    "CouponRefundCount" INTEGER NULL,
    "CouponTypes" TEXT NULL,
    "CouponIds" TEXT NULL,
    "CouponRefundFees" TEXT NULL,
    "RefundStatus" TEXT NULL,
    "RefundRecvAccout" TEXT NULL,
    "RefundAccount" TEXT NULL,
    "RefundRequestSource" TEXT NULL,
    "Channel" TEXT NULL,
    "UserReceivedAccount" TEXT NULL,
    "CreateTime" TEXT NULL,
    "Status" TEXT NULL,
    "FundsAccount" TEXT NULL,
    "Amount" TEXT NULL,
    "PromotionDetail" TEXT NULL,
    "ExtraProperties" TEXT NOT NULL,
    "ConcurrencyStamp" TEXT NOT NULL,
    "CreationTime" TEXT NOT NULL,
    "CreatorId" TEXT NULL
);

CREATE TABLE IF NOT EXISTS "OpenIddictApplications" (
    "Id" TEXT NOT NULL CONSTRAINT "PK_OpenIddictApplications" PRIMARY KEY,
    "ApplicationType" TEXT NULL,
    "ClientId" TEXT NULL,
    "ClientSecret" TEXT NULL,
    "ClientType" TEXT NULL,
    "ConsentType" TEXT NULL,
    "DisplayName" TEXT NULL,
    "DisplayNames" TEXT NULL,
    "JsonWebKeySet" TEXT NULL,
    "Permissions" TEXT NULL,
    "PostLogoutRedirectUris" TEXT NULL,
    "Properties" TEXT NULL,
    "RedirectUris" TEXT NULL,
    "Requirements" TEXT NULL,
    "Settings" TEXT NULL,
    "FrontChannelLogoutUri" TEXT NULL,
    "ClientUri" TEXT NULL,
    "LogoUri" TEXT NULL,
    "ExtraProperties" TEXT NOT NULL,
    "ConcurrencyStamp" TEXT NOT NULL,
    "CreationTime" TEXT NOT NULL,
    "CreatorId" TEXT NULL,
    "LastModificationTime" TEXT NULL,
    "LastModifierId" TEXT NULL,
    "IsDeleted" INTEGER NOT NULL DEFAULT 0,
    "DeleterId" TEXT NULL,
    "DeletionTime" TEXT NULL
);

CREATE TABLE IF NOT EXISTS "OpenIddictScopes" (
    "Id" TEXT NOT NULL CONSTRAINT "PK_OpenIddictScopes" PRIMARY KEY,
    "Description" TEXT NULL,
    "Descriptions" TEXT NULL,
    "DisplayName" TEXT NULL,
    "DisplayNames" TEXT NULL,
    "Name" TEXT NULL,
    "Properties" TEXT NULL,
    "Resources" TEXT NULL,
    "ExtraProperties" TEXT NOT NULL,
    "ConcurrencyStamp" TEXT NOT NULL,
    "CreationTime" TEXT NOT NULL,
    "CreatorId" TEXT NULL,
    "LastModificationTime" TEXT NULL,
    "LastModifierId" TEXT NULL,
    "IsDeleted" INTEGER NOT NULL DEFAULT 0,
    "DeleterId" TEXT NULL,
    "DeletionTime" TEXT NULL
);

CREATE TABLE IF NOT EXISTS "AbpAuditLogActions" (
    "Id" TEXT NOT NULL CONSTRAINT "PK_AbpAuditLogActions" PRIMARY KEY,
    "TenantId" TEXT NULL,
    "AuditLogId" TEXT NOT NULL,
    "ServiceName" TEXT NULL,
    "MethodName" TEXT NULL,
    "Parameters" TEXT NULL,
    "ExecutionTime" TEXT NOT NULL,
    "ExecutionDuration" INTEGER NOT NULL,
    "ExtraProperties" TEXT NULL,
    CONSTRAINT "FK_AbpAuditLogActions_AbpAuditLogs_AuditLogId" FOREIGN KEY ("AuditLogId") REFERENCES "AbpAuditLogs" ("Id") ON DELETE CASCADE
);

CREATE TABLE IF NOT EXISTS "AbpEntityChanges" (
    "Id" TEXT NOT NULL CONSTRAINT "PK_AbpEntityChanges" PRIMARY KEY,
    "AuditLogId" TEXT NOT NULL,
    "TenantId" TEXT NULL,
    "ChangeTime" TEXT NOT NULL,
    "ChangeType" INTEGER NOT NULL,
    "EntityTenantId" TEXT NULL,
    "EntityId" TEXT NULL,
    "EntityTypeFullName" TEXT NOT NULL,
    "ExtraProperties" TEXT NULL,
    CONSTRAINT "FK_AbpEntityChanges_AbpAuditLogs_AuditLogId" FOREIGN KEY ("AuditLogId") REFERENCES "AbpAuditLogs" ("Id") ON DELETE CASCADE
);

CREATE TABLE IF NOT EXISTS "AbpBlobs" (
    "Id" TEXT NOT NULL CONSTRAINT "PK_AbpBlobs" PRIMARY KEY,
    "ContainerId" TEXT NOT NULL,
    "TenantId" TEXT NULL,
    "Name" TEXT NOT NULL,
    "Content" BLOB NULL,
    "ExtraProperties" TEXT NOT NULL,
    "ConcurrencyStamp" TEXT NOT NULL,
    CONSTRAINT "FK_AbpBlobs_AbpBlobContainers_ContainerId" FOREIGN KEY ("ContainerId") REFERENCES "AbpBlobContainers" ("Id") ON DELETE CASCADE
);

CREATE TABLE IF NOT EXISTS "AbpOrganizationUnitRoles" (
    "RoleId" TEXT NOT NULL,
    "OrganizationUnitId" TEXT NOT NULL,
    "TenantId" TEXT NULL,
    "CreationTime" TEXT NOT NULL,
    "CreatorId" TEXT NULL,
    CONSTRAINT "PK_AbpOrganizationUnitRoles" PRIMARY KEY ("OrganizationUnitId", "RoleId"),
    CONSTRAINT "FK_AbpOrganizationUnitRoles_AbpOrganizationUnits_OrganizationUnitId" FOREIGN KEY ("OrganizationUnitId") REFERENCES "AbpOrganizationUnits" ("Id") ON DELETE CASCADE,
    CONSTRAINT "FK_AbpOrganizationUnitRoles_AbpRoles_RoleId" FOREIGN KEY ("RoleId") REFERENCES "AbpRoles" ("Id") ON DELETE CASCADE
);

CREATE TABLE IF NOT EXISTS "AbpRoleClaims" (
    "Id" TEXT NOT NULL CONSTRAINT "PK_AbpRoleClaims" PRIMARY KEY,
    "RoleId" TEXT NOT NULL,
    "TenantId" TEXT NULL,
    "ClaimType" TEXT NOT NULL,
    "ClaimValue" TEXT NULL,
    CONSTRAINT "FK_AbpRoleClaims_AbpRoles_RoleId" FOREIGN KEY ("RoleId") REFERENCES "AbpRoles" ("Id") ON DELETE CASCADE
);

CREATE TABLE IF NOT EXISTS "AbpTenantConnectionStrings" (
    "TenantId" TEXT NOT NULL,
    "Name" TEXT NOT NULL,
    "Value" TEXT NOT NULL,
    CONSTRAINT "PK_AbpTenantConnectionStrings" PRIMARY KEY ("TenantId", "Name"),
    CONSTRAINT "FK_AbpTenantConnectionStrings_AbpTenants_TenantId" FOREIGN KEY ("TenantId") REFERENCES "AbpTenants" ("Id") ON DELETE CASCADE
);

CREATE TABLE IF NOT EXISTS "AbpUserClaims" (
    "Id" TEXT NOT NULL CONSTRAINT "PK_AbpUserClaims" PRIMARY KEY,
    "UserId" TEXT NOT NULL,
    "TenantId" TEXT NULL,
    "ClaimType" TEXT NOT NULL,
    "ClaimValue" TEXT NULL,
    CONSTRAINT "FK_AbpUserClaims_AbpUsers_UserId" FOREIGN KEY ("UserId") REFERENCES "AbpUsers" ("Id") ON DELETE CASCADE
);

CREATE TABLE IF NOT EXISTS "AbpUserLogins" (
    "UserId" TEXT NOT NULL,
    "LoginProvider" TEXT NOT NULL,
    "TenantId" TEXT NULL,
    "ProviderKey" TEXT NOT NULL,
    "ProviderDisplayName" TEXT NULL,
    CONSTRAINT "PK_AbpUserLogins" PRIMARY KEY ("UserId", "LoginProvider"),
    CONSTRAINT "FK_AbpUserLogins_AbpUsers_UserId" FOREIGN KEY ("UserId") REFERENCES "AbpUsers" ("Id") ON DELETE CASCADE
);

CREATE TABLE IF NOT EXISTS "AbpUserOrganizationUnits" (
    "UserId" TEXT NOT NULL,
    "OrganizationUnitId" TEXT NOT NULL,
    "TenantId" TEXT NULL,
    "CreationTime" TEXT NOT NULL,
    "CreatorId" TEXT NULL,
    CONSTRAINT "PK_AbpUserOrganizationUnits" PRIMARY KEY ("OrganizationUnitId", "UserId"),
    CONSTRAINT "FK_AbpUserOrganizationUnits_AbpOrganizationUnits_OrganizationUnitId" FOREIGN KEY ("OrganizationUnitId") REFERENCES "AbpOrganizationUnits" ("Id") ON DELETE CASCADE,
    CONSTRAINT "FK_AbpUserOrganizationUnits_AbpUsers_UserId" FOREIGN KEY ("UserId") REFERENCES "AbpUsers" ("Id") ON DELETE CASCADE
);

CREATE TABLE IF NOT EXISTS "AbpUserPasskeys" (
    "CredentialId" BLOB NOT NULL CONSTRAINT "PK_AbpUserPasskeys" PRIMARY KEY,
    "TenantId" TEXT NULL,
    "UserId" TEXT NOT NULL,
    "Data" TEXT NULL,
    CONSTRAINT "FK_AbpUserPasskeys_AbpUsers_UserId" FOREIGN KEY ("UserId") REFERENCES "AbpUsers" ("Id") ON DELETE CASCADE
);

CREATE TABLE IF NOT EXISTS "AbpUserPasswordHistories" (
    "UserId" TEXT NOT NULL,
    "Password" TEXT NOT NULL,
    "TenantId" TEXT NULL,
    "CreatedAt" TEXT NOT NULL,
    CONSTRAINT "PK_AbpUserPasswordHistories" PRIMARY KEY ("UserId", "Password"),
    CONSTRAINT "FK_AbpUserPasswordHistories_AbpUsers_UserId" FOREIGN KEY ("UserId") REFERENCES "AbpUsers" ("Id") ON DELETE CASCADE
);

CREATE TABLE IF NOT EXISTS "AbpUserRoles" (
    "UserId" TEXT NOT NULL,
    "RoleId" TEXT NOT NULL,
    "TenantId" TEXT NULL,
    CONSTRAINT "PK_AbpUserRoles" PRIMARY KEY ("UserId", "RoleId"),
    CONSTRAINT "FK_AbpUserRoles_AbpRoles_RoleId" FOREIGN KEY ("RoleId") REFERENCES "AbpRoles" ("Id") ON DELETE CASCADE,
    CONSTRAINT "FK_AbpUserRoles_AbpUsers_UserId" FOREIGN KEY ("UserId") REFERENCES "AbpUsers" ("Id") ON DELETE CASCADE
);

CREATE TABLE IF NOT EXISTS "AbpUserTokens" (
    "UserId" TEXT NOT NULL,
    "LoginProvider" TEXT NOT NULL,
    "Name" TEXT NOT NULL,
    "TenantId" TEXT NULL,
    "Value" TEXT NULL,
    CONSTRAINT "PK_AbpUserTokens" PRIMARY KEY ("UserId", "LoginProvider", "Name"),
    CONSTRAINT "FK_AbpUserTokens_AbpUsers_UserId" FOREIGN KEY ("UserId") REFERENCES "AbpUsers" ("Id") ON DELETE CASCADE
);

CREATE TABLE IF NOT EXISTS "AppMenuGrants" (
    "Id" TEXT NOT NULL CONSTRAINT "PK_AppMenuGrants" PRIMARY KEY,
    "MenuId" TEXT NOT NULL,
    "TenantId" TEXT NULL,
    "ProviderName" TEXT NOT NULL,
    "ProviderKey" TEXT NOT NULL,
    CONSTRAINT "FK_AppMenuGrants_AppMenus_MenuId" FOREIGN KEY ("MenuId") REFERENCES "AppMenus" ("Id") ON DELETE CASCADE
);

CREATE TABLE IF NOT EXISTS "AppRoleDataScopeOrganizationUnits" (
    "RoleDataScopeId" TEXT NOT NULL,
    "OrganizationUnitId" TEXT NOT NULL,
    CONSTRAINT "PK_AppRoleDataScopeOrganizationUnits" PRIMARY KEY ("RoleDataScopeId", "OrganizationUnitId"),
    CONSTRAINT "FK_AppRoleDataScopeOrganizationUnits_AppRoleDataScopes_RoleDataScopeId" FOREIGN KEY ("RoleDataScopeId") REFERENCES "AppRoleDataScopes" ("Id") ON DELETE CASCADE
);

CREATE TABLE IF NOT EXISTS "AppTenantPackageMenus" (
    "Id" TEXT NOT NULL CONSTRAINT "PK_AppTenantPackageMenus" PRIMARY KEY,
    "PackageId" TEXT NOT NULL,
    "TemplateMenuId" TEXT NOT NULL,
    CONSTRAINT "FK_AppTenantPackageMenus_AppTenantPackages_PackageId" FOREIGN KEY ("PackageId") REFERENCES "AppTenantPackages" ("Id") ON DELETE CASCADE
);

CREATE TABLE IF NOT EXISTS "EasyAbpAbpDataDictionaryDataDictionaryItems" (
    "DataDictionaryId" TEXT NOT NULL,
    "Code" TEXT NOT NULL,
    "TenantId" TEXT NULL,
    "DisplayText" TEXT NOT NULL,
    "Description" TEXT NULL,
    "IsStatic" INTEGER NOT NULL,
    "CreationTime" TEXT NOT NULL,
    "CreatorId" TEXT NULL,
    "LastModificationTime" TEXT NULL,
    "LastModifierId" TEXT NULL,
    CONSTRAINT "PK_EasyAbpAbpDataDictionaryDataDictionaryItems" PRIMARY KEY ("Code", "DataDictionaryId"),
    CONSTRAINT "FK_EasyAbpAbpDataDictionaryDataDictionaryItems_EasyAbpAbpDataDictionaryDataDictionaries_DataDictionaryId" FOREIGN KEY ("DataDictionaryId") REFERENCES "EasyAbpAbpDataDictionaryDataDictionaries" ("Id") ON DELETE CASCADE
);

CREATE TABLE IF NOT EXISTS "EasyAbpPaymentServicePaymentItems" (
    "Id" TEXT NOT NULL CONSTRAINT "PK_EasyAbpPaymentServicePaymentItems" PRIMARY KEY,
    "ItemType" TEXT NULL,
    "ItemKey" TEXT NULL,
    "OriginalPaymentAmount" decimal(20,8) NOT NULL,
    "PaymentDiscount" decimal(20,8) NOT NULL,
    "ActualPaymentAmount" decimal(20,8) NOT NULL,
    "RefundAmount" decimal(20,8) NOT NULL,
    "PendingRefundAmount" decimal(20,8) NOT NULL,
    "ExtraProperties" TEXT NULL,
    "PaymentId" TEXT NULL,
    "CreationTime" TEXT NOT NULL,
    "CreatorId" TEXT NULL,
    "LastModificationTime" TEXT NULL,
    "LastModifierId" TEXT NULL,
    "IsDeleted" INTEGER NOT NULL DEFAULT 0,
    "DeleterId" TEXT NULL,
    "DeletionTime" TEXT NULL,
    CONSTRAINT "FK_EasyAbpPaymentServicePaymentItems_EasyAbpPaymentServicePayments_PaymentId" FOREIGN KEY ("PaymentId") REFERENCES "EasyAbpPaymentServicePayments" ("Id")
);

CREATE TABLE IF NOT EXISTS "EasyAbpPaymentServiceRefundItems" (
    "Id" TEXT NOT NULL CONSTRAINT "PK_EasyAbpPaymentServiceRefundItems" PRIMARY KEY,
    "PaymentItemId" TEXT NOT NULL,
    "RefundAmount" decimal(20,8) NOT NULL,
    "CustomerRemark" TEXT NULL,
    "StaffRemark" TEXT NULL,
    "ExtraProperties" TEXT NULL,
    "RefundId" TEXT NULL,
    "CreationTime" TEXT NOT NULL,
    "CreatorId" TEXT NULL,
    "LastModificationTime" TEXT NULL,
    "LastModifierId" TEXT NULL,
    "IsDeleted" INTEGER NOT NULL DEFAULT 0,
    "DeleterId" TEXT NULL,
    "DeletionTime" TEXT NULL,
    CONSTRAINT "FK_EasyAbpPaymentServiceRefundItems_EasyAbpPaymentServiceRefunds_RefundId" FOREIGN KEY ("RefundId") REFERENCES "EasyAbpPaymentServiceRefunds" ("Id")
);

CREATE TABLE IF NOT EXISTS "OpenIddictAuthorizations" (
    "Id" TEXT NOT NULL CONSTRAINT "PK_OpenIddictAuthorizations" PRIMARY KEY,
    "ApplicationId" TEXT NULL,
    "CreationDate" TEXT NULL,
    "Properties" TEXT NULL,
    "Scopes" TEXT NULL,
    "Status" TEXT NULL,
    "Subject" TEXT NULL,
    "Type" TEXT NULL,
    "ExtraProperties" TEXT NOT NULL,
    "ConcurrencyStamp" TEXT NOT NULL,
    CONSTRAINT "FK_OpenIddictAuthorizations_OpenIddictApplications_ApplicationId" FOREIGN KEY ("ApplicationId") REFERENCES "OpenIddictApplications" ("Id")
);

CREATE TABLE IF NOT EXISTS "AbpEntityPropertyChanges" (
    "Id" TEXT NOT NULL CONSTRAINT "PK_AbpEntityPropertyChanges" PRIMARY KEY,
    "TenantId" TEXT NULL,
    "EntityChangeId" TEXT NOT NULL,
    "NewValue" TEXT NULL,
    "OriginalValue" TEXT NULL,
    "PropertyName" TEXT NOT NULL,
    "PropertyTypeFullName" TEXT NOT NULL,
    CONSTRAINT "FK_AbpEntityPropertyChanges_AbpEntityChanges_EntityChangeId" FOREIGN KEY ("EntityChangeId") REFERENCES "AbpEntityChanges" ("Id") ON DELETE CASCADE
);

CREATE TABLE IF NOT EXISTS "OpenIddictTokens" (
    "Id" TEXT NOT NULL CONSTRAINT "PK_OpenIddictTokens" PRIMARY KEY,
    "ApplicationId" TEXT NULL,
    "AuthorizationId" TEXT NULL,
    "CreationDate" TEXT NULL,
    "ExpirationDate" TEXT NULL,
    "Payload" TEXT NULL,
    "Properties" TEXT NULL,
    "RedemptionDate" TEXT NULL,
    "ReferenceId" TEXT NULL,
    "Status" TEXT NULL,
    "Subject" TEXT NULL,
    "Type" TEXT NULL,
    "ExtraProperties" TEXT NOT NULL,
    "ConcurrencyStamp" TEXT NOT NULL,
    CONSTRAINT "FK_OpenIddictTokens_OpenIddictApplications_ApplicationId" FOREIGN KEY ("ApplicationId") REFERENCES "OpenIddictApplications" ("Id"),
    CONSTRAINT "FK_OpenIddictTokens_OpenIddictAuthorizations_AuthorizationId" FOREIGN KEY ("AuthorizationId") REFERENCES "OpenIddictAuthorizations" ("Id")
);

CREATE INDEX IF NOT EXISTS "IX_AbpAuditLogActions_AuditLogId" ON "AbpAuditLogActions" ("AuditLogId");

CREATE INDEX IF NOT EXISTS "IX_AbpAuditLogActions_TenantId_ServiceName_MethodName_ExecutionTime" ON "AbpAuditLogActions" ("TenantId", "ServiceName", "MethodName", "ExecutionTime");

CREATE INDEX IF NOT EXISTS "IX_AbpAuditLogs_TenantId_ExecutionTime" ON "AbpAuditLogs" ("TenantId", "ExecutionTime");

CREATE INDEX IF NOT EXISTS "IX_AbpAuditLogs_TenantId_UserId_ExecutionTime" ON "AbpAuditLogs" ("TenantId", "UserId", "ExecutionTime");

CREATE INDEX IF NOT EXISTS "IX_AbpBackgroundJobs_ApplicationName_CompletionTime_IsAbandoned_NextTryTime" ON "AbpBackgroundJobs" ("ApplicationName", "CompletionTime", "IsAbandoned", "NextTryTime");

CREATE INDEX IF NOT EXISTS "IX_AbpBlobContainers_TenantId_Name" ON "AbpBlobContainers" ("TenantId", "Name");

CREATE INDEX IF NOT EXISTS "IX_AbpBlobs_ContainerId" ON "AbpBlobs" ("ContainerId");

CREATE INDEX IF NOT EXISTS "IX_AbpBlobs_TenantId_ContainerId_Name" ON "AbpBlobs" ("TenantId", "ContainerId", "Name");

CREATE INDEX IF NOT EXISTS "IX_AbpEntityChanges_AuditLogId" ON "AbpEntityChanges" ("AuditLogId");

CREATE INDEX IF NOT EXISTS "IX_AbpEntityChanges_TenantId_EntityTypeFullName_EntityId" ON "AbpEntityChanges" ("TenantId", "EntityTypeFullName", "EntityId");

CREATE INDEX IF NOT EXISTS "IX_AbpEntityPropertyChanges_EntityChangeId" ON "AbpEntityPropertyChanges" ("EntityChangeId");

CREATE UNIQUE INDEX IF NOT EXISTS "IX_AbpFeatureGroups_Name" ON "AbpFeatureGroups" ("Name");

CREATE INDEX IF NOT EXISTS "IX_AbpFeatures_GroupName" ON "AbpFeatures" ("GroupName");

CREATE UNIQUE INDEX IF NOT EXISTS "IX_AbpFeatures_Name" ON "AbpFeatures" ("Name");

CREATE UNIQUE INDEX IF NOT EXISTS "IX_AbpFeatureValues_Name_ProviderName_ProviderKey" ON "AbpFeatureValues" ("Name", "ProviderName", "ProviderKey");

CREATE UNIQUE INDEX IF NOT EXISTS "IX_AbpLinkUsers_SourceUserId_SourceTenantId_TargetUserId_TargetTenantId" ON "AbpLinkUsers" ("SourceUserId", "SourceTenantId", "TargetUserId", "TargetTenantId");

CREATE INDEX IF NOT EXISTS "IX_AbpOrganizationUnitRoles_RoleId_OrganizationUnitId" ON "AbpOrganizationUnitRoles" ("RoleId", "OrganizationUnitId");

CREATE INDEX IF NOT EXISTS "IX_AbpOrganizationUnits_Code" ON "AbpOrganizationUnits" ("Code");

CREATE INDEX IF NOT EXISTS "IX_AbpOrganizationUnits_ParentId" ON "AbpOrganizationUnits" ("ParentId");

CREATE UNIQUE INDEX IF NOT EXISTS "IX_AbpPermissionGrants_TenantId_Name_ProviderName_ProviderKey" ON "AbpPermissionGrants" ("TenantId", "Name", "ProviderName", "ProviderKey");

CREATE UNIQUE INDEX IF NOT EXISTS "IX_AbpPermissionGroups_Name" ON "AbpPermissionGroups" ("Name");

CREATE INDEX IF NOT EXISTS "IX_AbpPermissions_GroupName" ON "AbpPermissions" ("GroupName");

CREATE UNIQUE INDEX IF NOT EXISTS "IX_AbpPermissions_ResourceName_Name" ON "AbpPermissions" ("ResourceName", "Name");

CREATE UNIQUE INDEX IF NOT EXISTS "IX_AbpResourcePermissionGrants_TenantId_Name_ResourceName_ResourceKey_ProviderName_ProviderKey" ON "AbpResourcePermissionGrants" ("TenantId", "Name", "ResourceName", "ResourceKey", "ProviderName", "ProviderKey");

CREATE INDEX IF NOT EXISTS "IX_AbpRoleClaims_RoleId" ON "AbpRoleClaims" ("RoleId");

CREATE INDEX IF NOT EXISTS "IX_AbpRoles_NormalizedName" ON "AbpRoles" ("NormalizedName");

CREATE INDEX IF NOT EXISTS "IX_AbpSecurityLogs_TenantId_Action" ON "AbpSecurityLogs" ("TenantId", "Action");

CREATE INDEX IF NOT EXISTS "IX_AbpSecurityLogs_TenantId_ApplicationName" ON "AbpSecurityLogs" ("TenantId", "ApplicationName");

CREATE INDEX IF NOT EXISTS "IX_AbpSecurityLogs_TenantId_Identity" ON "AbpSecurityLogs" ("TenantId", "Identity");

CREATE INDEX IF NOT EXISTS "IX_AbpSecurityLogs_TenantId_UserId" ON "AbpSecurityLogs" ("TenantId", "UserId");

CREATE INDEX IF NOT EXISTS "IX_AbpSessions_Device" ON "AbpSessions" ("Device");

CREATE INDEX IF NOT EXISTS "IX_AbpSessions_SessionId" ON "AbpSessions" ("SessionId");

CREATE INDEX IF NOT EXISTS "IX_AbpSessions_TenantId_UserId" ON "AbpSessions" ("TenantId", "UserId");

CREATE UNIQUE INDEX IF NOT EXISTS "IX_AbpSettingDefinitions_Name" ON "AbpSettingDefinitions" ("Name");

CREATE UNIQUE INDEX IF NOT EXISTS "IX_AbpSettings_Name_ProviderName_ProviderKey" ON "AbpSettings" ("Name", "ProviderName", "ProviderKey");

CREATE INDEX IF NOT EXISTS "IX_AbpTenants_Name" ON "AbpTenants" ("Name");

CREATE INDEX IF NOT EXISTS "IX_AbpTenants_NormalizedName" ON "AbpTenants" ("NormalizedName");

CREATE INDEX IF NOT EXISTS "IX_AbpUserClaims_UserId" ON "AbpUserClaims" ("UserId");

CREATE INDEX IF NOT EXISTS "IX_AbpUserLogins_LoginProvider_ProviderKey" ON "AbpUserLogins" ("LoginProvider", "ProviderKey");

CREATE INDEX IF NOT EXISTS "IX_AbpUserOrganizationUnits_UserId_OrganizationUnitId" ON "AbpUserOrganizationUnits" ("UserId", "OrganizationUnitId");

CREATE INDEX IF NOT EXISTS "IX_AbpUserPasskeys_UserId" ON "AbpUserPasskeys" ("UserId");

CREATE INDEX IF NOT EXISTS "IX_AbpUserRoles_RoleId_UserId" ON "AbpUserRoles" ("RoleId", "UserId");

CREATE INDEX IF NOT EXISTS "IX_AbpUsers_Email" ON "AbpUsers" ("Email");

CREATE INDEX IF NOT EXISTS "IX_AbpUsers_NormalizedEmail" ON "AbpUsers" ("NormalizedEmail");

CREATE INDEX IF NOT EXISTS "IX_AbpUsers_NormalizedUserName" ON "AbpUsers" ("NormalizedUserName");

CREATE INDEX IF NOT EXISTS "IX_AbpUsers_UserName" ON "AbpUsers" ("UserName");

CREATE UNIQUE INDEX IF NOT EXISTS "UX_AbpUsers_TenantId_NormalizedUserName" ON "AbpUsers" ("TenantId", "NormalizedUserName");

CREATE UNIQUE INDEX IF NOT EXISTS "IX_AppDataDictionaryItemMetas_TenantId_DictionaryCode_ItemCode" ON "AppDataDictionaryItemMetas" ("TenantId", "DictionaryCode", "ItemCode");

CREATE INDEX IF NOT EXISTS "IX_AppDataScopeDemos_TenantId_OrganizationUnitId" ON "AppDataScopeDemos" ("TenantId", "OrganizationUnitId");

CREATE INDEX IF NOT EXISTS "IX_AppEditions_DisplayName" ON "AppEditions" ("DisplayName");

CREATE INDEX IF NOT EXISTS "IX_AppFileShareLinks_TenantId_FileId" ON "AppFileShareLinks" ("TenantId", "FileId");

CREATE UNIQUE INDEX IF NOT EXISTS "IX_AppFileShareLinks_Token" ON "AppFileShareLinks" ("Token");

CREATE INDEX IF NOT EXISTS "IX_AppFileThumbnails_State" ON "AppFileThumbnails" ("State");

CREATE UNIQUE INDEX IF NOT EXISTS "IX_AppFileThumbnails_TenantId_FileId" ON "AppFileThumbnails" ("TenantId", "FileId");

CREATE INDEX IF NOT EXISTS "IX_AppFileThumbnails_TenantId_State" ON "AppFileThumbnails" ("TenantId", "State");

CREATE INDEX IF NOT EXISTS "IX_AppGdprInfos_TenantId_RequestId" ON "AppGdprInfos" ("TenantId", "RequestId");

CREATE INDEX IF NOT EXISTS "IX_AppGdprRequests_TenantId_UserId_CreationTime" ON "AppGdprRequests" ("TenantId", "UserId", "CreationTime");

CREATE INDEX IF NOT EXISTS "IX_AppIdentityUserPosts_TenantId_PostId" ON "AppIdentityUserPosts" ("TenantId", "PostId");

CREATE UNIQUE INDEX IF NOT EXISTS "IX_AppLanguages_CultureName" ON "AppLanguages" ("CultureName");

CREATE UNIQUE INDEX IF NOT EXISTS "IX_AppLanguageTexts_TenantId_ResourceName_CultureName_Name" ON "AppLanguageTexts" ("TenantId", "ResourceName", "CultureName", "Name");

CREATE UNIQUE INDEX IF NOT EXISTS "IX_AppMenuGrants_MenuId_ProviderName_ProviderKey" ON "AppMenuGrants" ("MenuId", "ProviderName", "ProviderKey");

CREATE INDEX IF NOT EXISTS "IX_AppMenuGrants_TenantId_ProviderName_ProviderKey" ON "AppMenuGrants" ("TenantId", "ProviderName", "ProviderKey");

CREATE INDEX IF NOT EXISTS "IX_AppMenus_TenantId_ParentId" ON "AppMenus" ("TenantId", "ParentId");

CREATE UNIQUE INDEX IF NOT EXISTS "IX_AppMenus_TenantId_Path" ON "AppMenus" ("TenantId", "Path");

CREATE INDEX IF NOT EXISTS "IX_AppNotificationBroadcasts_TenantId_CreationTime" ON "AppNotificationBroadcasts" ("TenantId", "CreationTime");

CREATE INDEX IF NOT EXISTS "IX_AppOperationLogs_TenantId_CorrelationId" ON "AppOperationLogs" ("TenantId", "CorrelationId");

CREATE INDEX IF NOT EXISTS "IX_AppOperationLogs_TenantId_ExecutionTime" ON "AppOperationLogs" ("TenantId", "ExecutionTime");

CREATE INDEX IF NOT EXISTS "IX_AppOperationLogs_TenantId_UserId" ON "AppOperationLogs" ("TenantId", "UserId");

CREATE UNIQUE INDEX IF NOT EXISTS "IX_AppPosts_TenantId_Code" ON "AppPosts" ("TenantId", "Code");

CREATE UNIQUE INDEX IF NOT EXISTS "IX_AppPosts_TenantId_Name" ON "AppPosts" ("TenantId", "Name");

CREATE UNIQUE INDEX IF NOT EXISTS "IX_AppRoleDataScopes_TenantId_RoleName" ON "AppRoleDataScopes" ("TenantId", "RoleName");

CREATE INDEX IF NOT EXISTS "IX_AppScheduledJobExecutions_ScheduledJobId_StartTime" ON "AppScheduledJobExecutions" ("ScheduledJobId", "StartTime" DESC);

CREATE INDEX IF NOT EXISTS "IX_AppScheduledJobExecutions_TenantId_CreationTime" ON "AppScheduledJobExecutions" ("TenantId", "CreationTime");

CREATE INDEX IF NOT EXISTS "IX_AppScheduledJobs_TenantId_IsEnabled" ON "AppScheduledJobs" ("TenantId", "IsEnabled");

CREATE UNIQUE INDEX IF NOT EXISTS "IX_AppScheduledJobs_TenantId_Name" ON "AppScheduledJobs" ("TenantId", "Name");

CREATE UNIQUE INDEX IF NOT EXISTS "IX_AppTenantPackageMenus_PackageId_TemplateMenuId" ON "AppTenantPackageMenus" ("PackageId", "TemplateMenuId");

CREATE UNIQUE INDEX IF NOT EXISTS "IX_AppTenantPackages_Name" ON "AppTenantPackages" ("Name");

CREATE UNIQUE INDEX IF NOT EXISTS "IX_AppTextTemplateContents_TenantId_Name_CultureName" ON "AppTextTemplateContents" ("TenantId", "Name", "CultureName");

CREATE INDEX IF NOT EXISTS "IX_AppUserExcelFiles_TenantId_CreatorId" ON "AppUserExcelFiles" ("TenantId", "CreatorId");

CREATE INDEX IF NOT EXISTS "IX_EasyAbpAbpDataDictionaryDataDictionaries_Code" ON "EasyAbpAbpDataDictionaryDataDictionaries" ("Code");

CREATE INDEX IF NOT EXISTS "IX_EasyAbpAbpDataDictionaryDataDictionaryItems_Code_DataDictionaryId" ON "EasyAbpAbpDataDictionaryDataDictionaryItems" ("Code", "DataDictionaryId");

CREATE INDEX IF NOT EXISTS "IX_EasyAbpAbpDataDictionaryDataDictionaryItems_DataDictionaryId" ON "EasyAbpAbpDataDictionaryDataDictionaryItems" ("DataDictionaryId");

CREATE INDEX IF NOT EXISTS "IX_EasyAbpFileManagementFiles_BlobName" ON "EasyAbpFileManagementFiles" ("BlobName");

CREATE UNIQUE INDEX IF NOT EXISTS "IX_EasyAbpFileManagementFiles_FileName_ParentId_OwnerUserId_FileContainerName_TenantId_SoftDeletionToken" ON "EasyAbpFileManagementFiles" ("FileName", "ParentId", "OwnerUserId", "FileContainerName", "TenantId", "SoftDeletionToken");

CREATE INDEX IF NOT EXISTS "IX_EasyAbpFileManagementFiles_Hash" ON "EasyAbpFileManagementFiles" ("Hash");

CREATE INDEX IF NOT EXISTS "IX_EasyAbpFileManagementFiles_ParentId" ON "EasyAbpFileManagementFiles" ("ParentId");

CREATE INDEX IF NOT EXISTS "IX_EasyAbpFileManagementFiles_ParentId_OwnerUserId_FileContainerName_FileName" ON "EasyAbpFileManagementFiles" ("ParentId", "OwnerUserId", "FileContainerName", "FileName");

CREATE INDEX IF NOT EXISTS "IX_EasyAbpFileManagementFiles_TenantId_FileType" ON "EasyAbpFileManagementFiles" ("TenantId", "FileType");

CREATE INDEX IF NOT EXISTS "IX_EasyAbpPaymentServicePaymentItems_PaymentId" ON "EasyAbpPaymentServicePaymentItems" ("PaymentId");

CREATE INDEX IF NOT EXISTS "IX_EasyAbpPaymentServicePrepaymentAccounts_UserId" ON "EasyAbpPaymentServicePrepaymentAccounts" ("UserId");

CREATE INDEX IF NOT EXISTS "IX_EasyAbpPaymentServicePrepaymentTransactions_AccountId" ON "EasyAbpPaymentServicePrepaymentTransactions" ("AccountId");

CREATE INDEX IF NOT EXISTS "IX_EasyAbpPaymentServicePrepaymentTransactions_AccountUserId" ON "EasyAbpPaymentServicePrepaymentTransactions" ("AccountUserId");

CREATE INDEX IF NOT EXISTS "IX_EasyAbpPaymentServiceRefundItems_RefundId" ON "EasyAbpPaymentServiceRefundItems" ("RefundId");

CREATE INDEX IF NOT EXISTS "IX_EasyAbpPaymentServiceWeChatPayPaymentRecords_PaymentId" ON "EasyAbpPaymentServiceWeChatPayPaymentRecords" ("PaymentId");

CREATE INDEX IF NOT EXISTS "IX_EasyAbpPaymentServiceWeChatPayRefundRecords_OutRefundNo" ON "EasyAbpPaymentServiceWeChatPayRefundRecords" ("OutRefundNo");

CREATE INDEX IF NOT EXISTS "IX_EasyAbpPaymentServiceWeChatPayRefundRecords_PaymentId" ON "EasyAbpPaymentServiceWeChatPayRefundRecords" ("PaymentId");

CREATE INDEX IF NOT EXISTS "IX_OpenIddictApplications_ClientId" ON "OpenIddictApplications" ("ClientId");

CREATE INDEX IF NOT EXISTS "IX_OpenIddictAuthorizations_ApplicationId_Status_Subject_Type" ON "OpenIddictAuthorizations" ("ApplicationId", "Status", "Subject", "Type");

CREATE INDEX IF NOT EXISTS "IX_OpenIddictScopes_Name" ON "OpenIddictScopes" ("Name");

CREATE INDEX IF NOT EXISTS "IX_OpenIddictTokens_ApplicationId_Status_Subject_Type" ON "OpenIddictTokens" ("ApplicationId", "Status", "Subject", "Type");

CREATE INDEX IF NOT EXISTS "IX_OpenIddictTokens_AuthorizationId" ON "OpenIddictTokens" ("AuthorizationId");

CREATE INDEX IF NOT EXISTS "IX_OpenIddictTokens_ReferenceId" ON "OpenIddictTokens" ("ReferenceId");
