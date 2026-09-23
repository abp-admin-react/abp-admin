-- 框架库 PostgreSQL 基线，对应原 EF 迁移 20260922002644_Initial。
-- 已记入 __EFMigrationsHistory 的库会跳过。后续改表追加新的 .sql，不要改本文件。
-- 记账缺失的存量库（更早迁移代次等）重放也安全——全部建表/建索引语句带 IF NOT EXISTS，已存在的对象原样保留。

CREATE TABLE IF NOT EXISTS "AbpAuditLogExcelFiles" (
    "Id" uuid NOT NULL,
    "TenantId" uuid,
    "FileName" character varying(256),
    "CreationTime" timestamp without time zone NOT NULL,
    "CreatorId" uuid,
    CONSTRAINT "PK_AbpAuditLogExcelFiles" PRIMARY KEY ("Id")
);

CREATE TABLE IF NOT EXISTS "AbpAuditLogs" (
    "Id" uuid NOT NULL,
    "ApplicationName" character varying(96),
    "UserId" uuid,
    "UserName" character varying(256),
    "TenantId" uuid,
    "TenantName" character varying(64),
    "ImpersonatorUserId" uuid,
    "ImpersonatorUserName" character varying(256),
    "ImpersonatorTenantId" uuid,
    "ImpersonatorTenantName" character varying(64),
    "ExecutionTime" timestamp without time zone NOT NULL,
    "ExecutionDuration" integer NOT NULL,
    "ClientIpAddress" character varying(64),
    "ClientName" character varying(128),
    "ClientId" character varying(64),
    "CorrelationId" character varying(64),
    "BrowserInfo" character varying(512),
    "HttpMethod" character varying(16),
    "Url" character varying(256),
    "Exceptions" text,
    "Comments" character varying(256),
    "HttpStatusCode" integer,
    "HandledAt" timestamp without time zone,
    "HandledByName" character varying(256),
    "HandledByUserId" uuid,
    "HandledNote" character varying(512),
    "ExtraProperties" text NOT NULL,
    "ConcurrencyStamp" character varying(40) NOT NULL,
    CONSTRAINT "PK_AbpAuditLogs" PRIMARY KEY ("Id")
);

CREATE TABLE IF NOT EXISTS "AbpBackgroundJobs" (
    "Id" uuid NOT NULL,
    "ApplicationName" character varying(96),
    "JobName" character varying(128) NOT NULL,
    "JobArgs" character varying(1048576) NOT NULL,
    "TryCount" smallint NOT NULL DEFAULT 0,
    "CreationTime" timestamp without time zone NOT NULL,
    "NextTryTime" timestamp without time zone NOT NULL,
    "LastTryTime" timestamp without time zone,
    "IsAbandoned" boolean NOT NULL DEFAULT FALSE,
    "CompletionTime" timestamp without time zone,
    "Priority" smallint NOT NULL DEFAULT 15,
    "ExtraProperties" text NOT NULL,
    "ConcurrencyStamp" character varying(40) NOT NULL,
    CONSTRAINT "PK_AbpBackgroundJobs" PRIMARY KEY ("Id")
);

CREATE TABLE IF NOT EXISTS "AbpBlobContainers" (
    "Id" uuid NOT NULL,
    "TenantId" uuid,
    "Name" character varying(128) NOT NULL,
    "ExtraProperties" text NOT NULL,
    "ConcurrencyStamp" character varying(40) NOT NULL,
    CONSTRAINT "PK_AbpBlobContainers" PRIMARY KEY ("Id")
);

CREATE TABLE IF NOT EXISTS "AbpClaimTypes" (
    "Id" uuid NOT NULL,
    "Name" character varying(256) NOT NULL,
    "Required" boolean NOT NULL,
    "IsStatic" boolean NOT NULL,
    "Regex" character varying(512),
    "RegexDescription" character varying(128),
    "Description" character varying(256),
    "ValueType" integer NOT NULL,
    "CreationTime" timestamp without time zone NOT NULL,
    "ExtraProperties" text NOT NULL,
    "ConcurrencyStamp" character varying(40) NOT NULL,
    CONSTRAINT "PK_AbpClaimTypes" PRIMARY KEY ("Id")
);

CREATE TABLE IF NOT EXISTS "AbpFeatureGroups" (
    "Id" uuid NOT NULL,
    "Name" character varying(128) NOT NULL,
    "DisplayName" character varying(256) NOT NULL,
    "ExtraProperties" text,
    CONSTRAINT "PK_AbpFeatureGroups" PRIMARY KEY ("Id")
);

CREATE TABLE IF NOT EXISTS "AbpFeatures" (
    "Id" uuid NOT NULL,
    "GroupName" character varying(128) NOT NULL,
    "Name" character varying(128) NOT NULL,
    "ParentName" character varying(128),
    "DisplayName" character varying(256) NOT NULL,
    "Description" character varying(256),
    "DefaultValue" character varying(256),
    "IsVisibleToClients" boolean NOT NULL,
    "IsAvailableToHost" boolean NOT NULL,
    "AllowedProviders" character varying(256),
    "ValueType" character varying(2048),
    "ExtraProperties" text,
    CONSTRAINT "PK_AbpFeatures" PRIMARY KEY ("Id")
);

CREATE TABLE IF NOT EXISTS "AbpFeatureValues" (
    "Id" uuid NOT NULL,
    "Name" character varying(128) NOT NULL,
    "Value" character varying(128) NOT NULL,
    "ProviderName" character varying(64),
    "ProviderKey" character varying(64),
    CONSTRAINT "PK_AbpFeatureValues" PRIMARY KEY ("Id")
);

CREATE TABLE IF NOT EXISTS "AbpLinkUsers" (
    "Id" uuid NOT NULL,
    "SourceUserId" uuid NOT NULL,
    "SourceTenantId" uuid,
    "TargetUserId" uuid NOT NULL,
    "TargetTenantId" uuid,
    CONSTRAINT "PK_AbpLinkUsers" PRIMARY KEY ("Id")
);

CREATE TABLE IF NOT EXISTS "AbpOrganizationUnits" (
    "Id" uuid NOT NULL,
    "TenantId" uuid,
    "ParentId" uuid,
    "Code" character varying(95) NOT NULL,
    "DisplayName" character varying(128) NOT NULL,
    "EntityVersion" integer NOT NULL,
    "ExtraProperties" text NOT NULL,
    "ConcurrencyStamp" character varying(40) NOT NULL,
    "CreationTime" timestamp without time zone NOT NULL,
    "CreatorId" uuid,
    "LastModificationTime" timestamp without time zone,
    "LastModifierId" uuid,
    "IsDeleted" boolean NOT NULL DEFAULT FALSE,
    "DeleterId" uuid,
    "DeletionTime" timestamp without time zone,
    CONSTRAINT "PK_AbpOrganizationUnits" PRIMARY KEY ("Id"),
    CONSTRAINT "FK_AbpOrganizationUnits_AbpOrganizationUnits_ParentId" FOREIGN KEY ("ParentId") REFERENCES "AbpOrganizationUnits" ("Id")
);

CREATE TABLE IF NOT EXISTS "AbpPermissionGrants" (
    "Id" uuid NOT NULL,
    "TenantId" uuid,
    "Name" character varying(128) NOT NULL,
    "ProviderName" character varying(64) NOT NULL,
    "ProviderKey" character varying(64) NOT NULL,
    CONSTRAINT "PK_AbpPermissionGrants" PRIMARY KEY ("Id")
);

CREATE TABLE IF NOT EXISTS "AbpPermissionGroups" (
    "Id" uuid NOT NULL,
    "Name" character varying(128) NOT NULL,
    "DisplayName" character varying(256) NOT NULL,
    "ExtraProperties" text,
    CONSTRAINT "PK_AbpPermissionGroups" PRIMARY KEY ("Id")
);

CREATE TABLE IF NOT EXISTS "AbpPermissions" (
    "Id" uuid NOT NULL,
    "GroupName" character varying(128),
    "Name" character varying(128) NOT NULL,
    "ResourceName" character varying(256),
    "ManagementPermissionName" character varying(128),
    "ParentName" character varying(128),
    "DisplayName" character varying(256) NOT NULL,
    "IsEnabled" boolean NOT NULL,
    "MultiTenancySide" smallint NOT NULL,
    "Providers" character varying(128),
    "StateCheckers" character varying(256),
    "ExtraProperties" text,
    CONSTRAINT "PK_AbpPermissions" PRIMARY KEY ("Id")
);

CREATE TABLE IF NOT EXISTS "AbpResourcePermissionGrants" (
    "Id" uuid NOT NULL,
    "TenantId" uuid,
    "Name" character varying(128) NOT NULL,
    "ProviderName" character varying(64) NOT NULL,
    "ProviderKey" character varying(64) NOT NULL,
    "ResourceName" character varying(256) NOT NULL,
    "ResourceKey" character varying(256) NOT NULL,
    CONSTRAINT "PK_AbpResourcePermissionGrants" PRIMARY KEY ("Id")
);

CREATE TABLE IF NOT EXISTS "AbpRoles" (
    "Id" uuid NOT NULL,
    "TenantId" uuid,
    "Name" character varying(256) NOT NULL,
    "NormalizedName" character varying(256) NOT NULL,
    "IsDefault" boolean NOT NULL,
    "IsStatic" boolean NOT NULL,
    "IsPublic" boolean NOT NULL,
    "EntityVersion" integer NOT NULL,
    "CreationTime" timestamp without time zone NOT NULL,
    "ExtraProperties" text NOT NULL,
    "ConcurrencyStamp" character varying(40) NOT NULL,
    CONSTRAINT "PK_AbpRoles" PRIMARY KEY ("Id")
);

CREATE TABLE IF NOT EXISTS "AbpSecurityLogs" (
    "Id" uuid NOT NULL,
    "TenantId" uuid,
    "ApplicationName" character varying(96),
    "Identity" character varying(96),
    "Action" character varying(96),
    "UserId" uuid,
    "UserName" character varying(256),
    "TenantName" character varying(64),
    "ClientId" character varying(64),
    "CorrelationId" character varying(64),
    "ClientIpAddress" character varying(64),
    "BrowserInfo" character varying(512),
    "CreationTime" timestamp without time zone NOT NULL,
    "ExtraProperties" text NOT NULL,
    "ConcurrencyStamp" character varying(40) NOT NULL,
    CONSTRAINT "PK_AbpSecurityLogs" PRIMARY KEY ("Id")
);

CREATE TABLE IF NOT EXISTS "AbpSessions" (
    "Id" uuid NOT NULL,
    "SessionId" character varying(128) NOT NULL,
    "Device" character varying(64) NOT NULL,
    "DeviceInfo" character varying(256),
    "TenantId" uuid,
    "UserId" uuid NOT NULL,
    "ClientId" character varying(64),
    "IpAddresses" character varying(2048),
    "SignedIn" timestamp without time zone NOT NULL,
    "LastAccessed" timestamp without time zone,
    "ExtraProperties" text,
    CONSTRAINT "PK_AbpSessions" PRIMARY KEY ("Id")
);

CREATE TABLE IF NOT EXISTS "AbpSettingDefinitions" (
    "Id" uuid NOT NULL,
    "Name" character varying(128) NOT NULL,
    "DisplayName" character varying(256) NOT NULL,
    "Description" character varying(512),
    "DefaultValue" character varying(2048),
    "IsVisibleToClients" boolean NOT NULL,
    "Providers" character varying(1024),
    "IsInherited" boolean NOT NULL,
    "IsEncrypted" boolean NOT NULL,
    "ExtraProperties" text,
    CONSTRAINT "PK_AbpSettingDefinitions" PRIMARY KEY ("Id")
);

CREATE TABLE IF NOT EXISTS "AbpSettings" (
    "Id" uuid NOT NULL,
    "Name" character varying(128) NOT NULL,
    "Value" character varying(2048) NOT NULL,
    "ProviderName" character varying(64),
    "ProviderKey" character varying(64),
    CONSTRAINT "PK_AbpSettings" PRIMARY KEY ("Id")
);

CREATE TABLE IF NOT EXISTS "AbpTenants" (
    "Id" uuid NOT NULL,
    "Name" character varying(64) NOT NULL,
    "NormalizedName" character varying(64) NOT NULL,
    "EntityVersion" integer NOT NULL,
    "ActivationEndDate" timestamp without time zone,
    "ActivationState" smallint NOT NULL DEFAULT 0,
    "EditionEndDateUtc" timestamp without time zone,
    "ExtraProperties" text NOT NULL,
    "ConcurrencyStamp" character varying(40) NOT NULL,
    "CreationTime" timestamp without time zone NOT NULL,
    "CreatorId" uuid,
    "LastModificationTime" timestamp without time zone,
    "LastModifierId" uuid,
    "IsDeleted" boolean NOT NULL DEFAULT FALSE,
    "DeleterId" uuid,
    "DeletionTime" timestamp without time zone,
    CONSTRAINT "PK_AbpTenants" PRIMARY KEY ("Id")
);

CREATE TABLE IF NOT EXISTS "AbpUserDelegations" (
    "Id" uuid NOT NULL,
    "TenantId" uuid,
    "SourceUserId" uuid NOT NULL,
    "TargetUserId" uuid NOT NULL,
    "StartTime" timestamp without time zone NOT NULL,
    "EndTime" timestamp without time zone NOT NULL,
    CONSTRAINT "PK_AbpUserDelegations" PRIMARY KEY ("Id")
);

CREATE TABLE IF NOT EXISTS "AbpUsers" (
    "Id" uuid NOT NULL,
    "TenantId" uuid,
    "UserName" character varying(256) NOT NULL,
    "NormalizedUserName" character varying(256) NOT NULL,
    "Name" character varying(64),
    "Surname" character varying(64),
    "Email" character varying(256) NOT NULL,
    "NormalizedEmail" character varying(256) NOT NULL,
    "EmailConfirmed" boolean NOT NULL DEFAULT FALSE,
    "PasswordHash" character varying(256),
    "SecurityStamp" character varying(256) NOT NULL,
    "IsExternal" boolean NOT NULL DEFAULT FALSE,
    "PhoneNumber" character varying(16),
    "PhoneNumberConfirmed" boolean NOT NULL DEFAULT FALSE,
    "IsActive" boolean NOT NULL,
    "TwoFactorEnabled" boolean NOT NULL DEFAULT FALSE,
    "LockoutEnd" timestamp with time zone,
    "LockoutEnabled" boolean NOT NULL DEFAULT FALSE,
    "AccessFailedCount" integer NOT NULL DEFAULT 0,
    "ShouldChangePasswordOnNextLogin" boolean NOT NULL,
    "EntityVersion" integer NOT NULL,
    "LastPasswordChangeTime" timestamp with time zone,
    "LastSignInTime" timestamp with time zone,
    "Leaved" boolean NOT NULL DEFAULT FALSE,
    "ExtraProperties" text NOT NULL,
    "ConcurrencyStamp" character varying(40) NOT NULL,
    "CreationTime" timestamp without time zone NOT NULL,
    "CreatorId" uuid,
    "LastModificationTime" timestamp without time zone,
    "LastModifierId" uuid,
    "IsDeleted" boolean NOT NULL DEFAULT FALSE,
    "DeleterId" uuid,
    "DeletionTime" timestamp without time zone,
    CONSTRAINT "PK_AbpUsers" PRIMARY KEY ("Id")
);

CREATE TABLE IF NOT EXISTS "AppDataDictionaryItemMetas" (
    "Id" uuid NOT NULL,
    "TenantId" uuid,
    "DictionaryCode" character varying(64) NOT NULL,
    "ItemCode" character varying(64) NOT NULL,
    "TagType" character varying(32),
    "Order" integer NOT NULL,
    "CreationTime" timestamp without time zone NOT NULL,
    "CreatorId" uuid,
    "LastModificationTime" timestamp without time zone,
    "LastModifierId" uuid,
    CONSTRAINT "PK_AppDataDictionaryItemMetas" PRIMARY KEY ("Id")
);

CREATE TABLE IF NOT EXISTS "AppDataScopeDemos" (
    "Id" uuid NOT NULL,
    "TenantId" uuid,
    "OrganizationUnitId" uuid,
    "Name" character varying(128) NOT NULL,
    "ExtraProperties" text NOT NULL,
    "ConcurrencyStamp" character varying(40) NOT NULL,
    "CreationTime" timestamp without time zone NOT NULL,
    "CreatorId" uuid,
    "LastModificationTime" timestamp without time zone,
    "LastModifierId" uuid,
    CONSTRAINT "PK_AppDataScopeDemos" PRIMARY KEY ("Id")
);

CREATE TABLE IF NOT EXISTS "AppEditions" (
    "Id" uuid NOT NULL,
    "DisplayName" character varying(128) NOT NULL,
    "ExtraProperties" text NOT NULL,
    "ConcurrencyStamp" character varying(40) NOT NULL,
    "CreationTime" timestamp without time zone NOT NULL,
    "CreatorId" uuid,
    "LastModificationTime" timestamp without time zone,
    "LastModifierId" uuid,
    CONSTRAINT "PK_AppEditions" PRIMARY KEY ("Id")
);

CREATE TABLE IF NOT EXISTS "AppFileShareLinks" (
    "Id" uuid NOT NULL,
    "TenantId" uuid,
    "FileId" uuid NOT NULL,
    "Token" character varying(64) NOT NULL,
    "ExpireTime" timestamp without time zone NOT NULL,
    "IsPublic" boolean NOT NULL,
    "DownloadCount" integer NOT NULL,
    "MaxDownloads" integer,
    "ExtraProperties" text NOT NULL,
    "ConcurrencyStamp" character varying(40) NOT NULL,
    "CreationTime" timestamp without time zone NOT NULL,
    "CreatorId" uuid,
    CONSTRAINT "PK_AppFileShareLinks" PRIMARY KEY ("Id")
);

CREATE TABLE IF NOT EXISTS "AppFileThumbnails" (
    "Id" uuid NOT NULL,
    "TenantId" uuid,
    "FileId" uuid NOT NULL,
    "BlobName" character varying(128),
    "Width" integer NOT NULL,
    "Height" integer NOT NULL,
    "State" smallint NOT NULL,
    "FailureReason" character varying(512),
    "CreationTime" timestamp without time zone NOT NULL,
    "CreatorId" uuid,
    "LastModificationTime" timestamp without time zone,
    "LastModifierId" uuid,
    CONSTRAINT "PK_AppFileThumbnails" PRIMARY KEY ("Id")
);

CREATE TABLE IF NOT EXISTS "AppGdprInfos" (
    "Id" uuid NOT NULL,
    "TenantId" uuid,
    "RequestId" uuid NOT NULL,
    "Data" text NOT NULL,
    "Provider" character varying(128) NOT NULL,
    "ExtraProperties" text NOT NULL,
    "ConcurrencyStamp" character varying(40) NOT NULL,
    "CreationTime" timestamp without time zone NOT NULL,
    "CreatorId" uuid,
    "LastModificationTime" timestamp without time zone,
    "LastModifierId" uuid,
    CONSTRAINT "PK_AppGdprInfos" PRIMARY KEY ("Id")
);

CREATE TABLE IF NOT EXISTS "AppGdprRequests" (
    "Id" uuid NOT NULL,
    "TenantId" uuid,
    "UserId" uuid NOT NULL,
    "CreationTime" timestamp without time zone NOT NULL,
    "ReadyTime" timestamp without time zone NOT NULL,
    CONSTRAINT "PK_AppGdprRequests" PRIMARY KEY ("Id")
);

CREATE TABLE IF NOT EXISTS "AppIdentityUserPosts" (
    "UserId" uuid NOT NULL,
    "PostId" uuid NOT NULL,
    "TenantId" uuid,
    CONSTRAINT "PK_AppIdentityUserPosts" PRIMARY KEY ("UserId", "PostId")
);

CREATE TABLE IF NOT EXISTS "AppLanguages" (
    "Id" uuid NOT NULL,
    "CultureName" character varying(20) NOT NULL,
    "UiCultureName" character varying(20) NOT NULL,
    "DisplayName" character varying(64) NOT NULL,
    "FlagIcon" character varying(30),
    "IsEnabled" boolean NOT NULL,
    "IsDefault" boolean NOT NULL,
    "ExtraProperties" text NOT NULL,
    "ConcurrencyStamp" character varying(40) NOT NULL,
    "CreationTime" timestamp without time zone NOT NULL,
    "CreatorId" uuid,
    "LastModificationTime" timestamp without time zone,
    "LastModifierId" uuid,
    CONSTRAINT "PK_AppLanguages" PRIMARY KEY ("Id")
);

CREATE TABLE IF NOT EXISTS "AppLanguageTexts" (
    "Id" uuid NOT NULL,
    "TenantId" uuid,
    "ResourceName" character varying(128) NOT NULL,
    "CultureName" character varying(20) NOT NULL,
    "Name" character varying(256) NOT NULL,
    "Value" character varying(2048) NOT NULL,
    "ExtraProperties" text NOT NULL,
    "ConcurrencyStamp" character varying(40) NOT NULL,
    "CreationTime" timestamp without time zone NOT NULL,
    "CreatorId" uuid,
    "LastModificationTime" timestamp without time zone,
    "LastModifierId" uuid,
    CONSTRAINT "PK_AppLanguageTexts" PRIMARY KEY ("Id")
);

CREATE TABLE IF NOT EXISTS "AppMenus" (
    "Id" uuid NOT NULL,
    "TenantId" uuid,
    "ParentId" uuid,
    "Type" integer NOT NULL,
    "Title" character varying(64) NOT NULL,
    "Name" character varying(64),
    "Path" character varying(128),
    "Icon" character varying(64),
    "OrderNo" integer NOT NULL,
    "IsHide" boolean NOT NULL,
    "IsEnabled" boolean NOT NULL,
    "PermissionName" character varying(256),
    "Remark" character varying(256),
    "ExtraProperties" text NOT NULL,
    "ConcurrencyStamp" character varying(40) NOT NULL,
    "CreationTime" timestamp without time zone NOT NULL,
    "CreatorId" uuid,
    "LastModificationTime" timestamp without time zone,
    "LastModifierId" uuid,
    "IsDeleted" boolean NOT NULL DEFAULT FALSE,
    "DeleterId" uuid,
    "DeletionTime" timestamp without time zone,
    CONSTRAINT "PK_AppMenus" PRIMARY KEY ("Id")
);

CREATE TABLE IF NOT EXISTS "AppNotificationBroadcasts" (
    "Id" uuid NOT NULL,
    "TenantId" uuid,
    "TargetType" character varying(32) NOT NULL,
    "TargetId" uuid,
    "NotificationMethods" character varying(128) NOT NULL,
    "Title" character varying(256) NOT NULL,
    "Body" text NOT NULL,
    "SmsText" text,
    "SmsPropertiesJson" text,
    "TotalCount" integer NOT NULL,
    "SentCount" integer NOT NULL,
    "FailedCount" integer NOT NULL,
    "LastProcessedUserId" uuid,
    "State" character varying(16) NOT NULL,
    "CompletionTime" timestamp without time zone,
    "ExtraProperties" text NOT NULL,
    "ConcurrencyStamp" character varying(40) NOT NULL,
    "CreationTime" timestamp without time zone NOT NULL,
    "CreatorId" uuid,
    "LastModificationTime" timestamp without time zone,
    "LastModifierId" uuid,
    CONSTRAINT "PK_AppNotificationBroadcasts" PRIMARY KEY ("Id")
);

CREATE TABLE IF NOT EXISTS "AppOperationLogs" (
    "Id" uuid NOT NULL,
    "TenantId" uuid,
    "UserId" uuid,
    "UserName" text,
    "Type" character varying(64) NOT NULL,
    "SubType" character varying(128) NOT NULL,
    "BizId" text,
    "Action" character varying(2048),
    "Extra" character varying(2048),
    "Success" boolean NOT NULL,
    "ErrorMessage" text,
    "RequestMethod" text,
    "RequestUrl" text,
    "ClientIpAddress" text,
    "UserAgent" text,
    "CorrelationId" character varying(64),
    "Duration" integer NOT NULL,
    "ExecutionTime" timestamp without time zone NOT NULL,
    CONSTRAINT "PK_AppOperationLogs" PRIMARY KEY ("Id")
);

CREATE TABLE IF NOT EXISTS "AppPosts" (
    "Id" uuid NOT NULL,
    "TenantId" uuid,
    "Name" character varying(64) NOT NULL,
    "Code" character varying(64) NOT NULL,
    "SortOrder" integer NOT NULL,
    "Status" integer NOT NULL,
    "Remark" character varying(512),
    "ExtraProperties" text NOT NULL,
    "ConcurrencyStamp" character varying(40) NOT NULL,
    "CreationTime" timestamp without time zone NOT NULL,
    "CreatorId" uuid,
    "LastModificationTime" timestamp without time zone,
    "LastModifierId" uuid,
    "IsDeleted" boolean NOT NULL DEFAULT FALSE,
    "DeleterId" uuid,
    "DeletionTime" timestamp without time zone,
    CONSTRAINT "PK_AppPosts" PRIMARY KEY ("Id")
);

CREATE TABLE IF NOT EXISTS "AppRoleDataScopes" (
    "Id" uuid NOT NULL,
    "TenantId" uuid,
    "RoleName" character varying(256) NOT NULL,
    "ScopeType" integer NOT NULL,
    "ExtraProperties" text NOT NULL,
    "ConcurrencyStamp" character varying(40) NOT NULL,
    "CreationTime" timestamp without time zone NOT NULL,
    "CreatorId" uuid,
    "LastModificationTime" timestamp without time zone,
    "LastModifierId" uuid,
    CONSTRAINT "PK_AppRoleDataScopes" PRIMARY KEY ("Id")
);

CREATE TABLE IF NOT EXISTS "AppScheduledJobExecutions" (
    "Id" uuid NOT NULL,
    "TenantId" uuid,
    "ScheduledJobId" uuid NOT NULL,
    "StartTime" timestamp without time zone NOT NULL,
    "EndTime" timestamp without time zone,
    "Success" boolean NOT NULL,
    "Message" character varying(4000),
    "DurationMs" bigint NOT NULL,
    "CreationTime" timestamp without time zone NOT NULL,
    CONSTRAINT "PK_AppScheduledJobExecutions" PRIMARY KEY ("Id")
);

CREATE TABLE IF NOT EXISTS "AppScheduledJobs" (
    "Id" uuid NOT NULL,
    "TenantId" uuid,
    "Name" character varying(128) NOT NULL,
    "JobType" character varying(128) NOT NULL,
    "CronExpression" character varying(64) NOT NULL,
    "IsEnabled" boolean NOT NULL,
    "Description" character varying(512),
    "Payload" text,
    "LastRunTime" timestamp without time zone,
    "LastRunSuccess" boolean,
    "LastRunMessage" character varying(4000),
    "NextRunTime" timestamp without time zone,
    "ExtraProperties" text NOT NULL,
    "ConcurrencyStamp" character varying(40) NOT NULL,
    "CreationTime" timestamp without time zone NOT NULL,
    "CreatorId" uuid,
    "LastModificationTime" timestamp without time zone,
    "LastModifierId" uuid,
    "IsDeleted" boolean NOT NULL DEFAULT FALSE,
    "DeleterId" uuid,
    "DeletionTime" timestamp without time zone,
    CONSTRAINT "PK_AppScheduledJobs" PRIMARY KEY ("Id")
);

CREATE TABLE IF NOT EXISTS "AppTenantPackages" (
    "Id" uuid NOT NULL,
    "Name" character varying(64) NOT NULL,
    "Remark" character varying(256),
    "ExtraProperties" text NOT NULL,
    "ConcurrencyStamp" character varying(40) NOT NULL,
    "CreationTime" timestamp without time zone NOT NULL,
    "CreatorId" uuid,
    "LastModificationTime" timestamp without time zone,
    "LastModifierId" uuid,
    "IsDeleted" boolean NOT NULL DEFAULT FALSE,
    "DeleterId" uuid,
    "DeletionTime" timestamp without time zone,
    CONSTRAINT "PK_AppTenantPackages" PRIMARY KEY ("Id")
);

CREATE TABLE IF NOT EXISTS "AppTextTemplateContents" (
    "Id" uuid NOT NULL,
    "TenantId" uuid,
    "Name" character varying(128) NOT NULL,
    "CultureName" character varying(16),
    "Content" text NOT NULL,
    "ExtraProperties" text NOT NULL,
    "ConcurrencyStamp" character varying(40) NOT NULL,
    "CreationTime" timestamp without time zone NOT NULL,
    "CreatorId" uuid,
    "LastModificationTime" timestamp without time zone,
    "LastModifierId" uuid,
    CONSTRAINT "PK_AppTextTemplateContents" PRIMARY KEY ("Id")
);

CREATE TABLE IF NOT EXISTS "AppUserExcelFiles" (
    "Id" uuid NOT NULL,
    "TenantId" uuid,
    "FileName" character varying(256) NOT NULL,
    "CreationTime" timestamp without time zone NOT NULL,
    "CreatorId" uuid,
    CONSTRAINT "PK_AppUserExcelFiles" PRIMARY KEY ("Id")
);

CREATE TABLE IF NOT EXISTS "EasyAbpAbpDataDictionaryDataDictionaries" (
    "Id" uuid NOT NULL,
    "TenantId" uuid,
    "Code" character varying(64) NOT NULL,
    "DisplayText" character varying(64) NOT NULL,
    "Description" character varying(1024),
    "IsStatic" boolean NOT NULL,
    "ExtraProperties" text NOT NULL,
    "ConcurrencyStamp" character varying(40) NOT NULL,
    "CreationTime" timestamp without time zone NOT NULL,
    "CreatorId" uuid,
    "LastModificationTime" timestamp without time zone,
    "LastModifierId" uuid,
    "IsDeleted" boolean NOT NULL DEFAULT FALSE,
    "DeleterId" uuid,
    "DeletionTime" timestamp without time zone,
    CONSTRAINT "PK_EasyAbpAbpDataDictionaryDataDictionaries" PRIMARY KEY ("Id")
);

CREATE TABLE IF NOT EXISTS "EasyAbpFileManagementFiles" (
    "Id" uuid NOT NULL,
    "TenantId" uuid,
    "ParentId" uuid,
    "FileContainerName" character varying(64),
    "FileName" character varying(255),
    "MimeType" character varying(128),
    "FileType" integer NOT NULL,
    "SubFilesQuantity" integer NOT NULL,
    "HasSubdirectories" boolean NOT NULL,
    "ByteSize" bigint NOT NULL,
    "Hash" character varying(32),
    "BlobName" character varying(64),
    "OwnerUserId" uuid,
    "Flag" character varying(64),
    "SoftDeletionToken" text DEFAULT '',
    "ExtraProperties" text NOT NULL,
    "ConcurrencyStamp" character varying(40) NOT NULL,
    "CreationTime" timestamp without time zone NOT NULL,
    "CreatorId" uuid,
    "LastModificationTime" timestamp without time zone,
    "LastModifierId" uuid,
    "IsDeleted" boolean NOT NULL DEFAULT FALSE,
    "DeleterId" uuid,
    "DeletionTime" timestamp without time zone,
    CONSTRAINT "PK_EasyAbpFileManagementFiles" PRIMARY KEY ("Id")
);

CREATE TABLE IF NOT EXISTS "EasyAbpFileManagementUsers" (
    "Id" uuid NOT NULL,
    "TenantId" uuid,
    "UserName" character varying(256) NOT NULL,
    "Email" character varying(256) NOT NULL,
    "Name" character varying(64),
    "Surname" character varying(64),
    "IsActive" boolean NOT NULL,
    "EmailConfirmed" boolean NOT NULL DEFAULT FALSE,
    "PhoneNumber" character varying(16),
    "PhoneNumberConfirmed" boolean NOT NULL DEFAULT FALSE,
    "ExtraProperties" text NOT NULL,
    "ConcurrencyStamp" character varying(40) NOT NULL,
    CONSTRAINT "PK_EasyAbpFileManagementUsers" PRIMARY KEY ("Id")
);

CREATE TABLE IF NOT EXISTS "EasyAbpNotificationServiceNotificationInfos" (
    "Id" uuid NOT NULL,
    "TenantId" uuid,
    "ExtraProperties" text NOT NULL,
    "ConcurrencyStamp" character varying(40) NOT NULL,
    "CreationTime" timestamp without time zone NOT NULL,
    "CreatorId" uuid,
    "LastModificationTime" timestamp without time zone,
    "LastModifierId" uuid,
    "IsDeleted" boolean NOT NULL DEFAULT FALSE,
    "DeleterId" uuid,
    "DeletionTime" timestamp without time zone,
    CONSTRAINT "PK_EasyAbpNotificationServiceNotificationInfos" PRIMARY KEY ("Id")
);

CREATE TABLE IF NOT EXISTS "EasyAbpNotificationServiceNotifications" (
    "Id" uuid NOT NULL,
    "TenantId" uuid,
    "UserId" uuid NOT NULL,
    "UserName" text,
    "NotificationInfoId" uuid NOT NULL,
    "NotificationMethod" text,
    "Success" boolean,
    "CompletionTime" timestamp without time zone,
    "FailureReason" text,
    "RetryForNotificationId" uuid,
    "ExtraProperties" text NOT NULL,
    "ConcurrencyStamp" character varying(40) NOT NULL,
    "CreationTime" timestamp without time zone NOT NULL,
    "CreatorId" uuid,
    CONSTRAINT "PK_EasyAbpNotificationServiceNotifications" PRIMARY KEY ("Id")
);

CREATE TABLE IF NOT EXISTS "EasyAbpPaymentServicePayments" (
    "Id" uuid NOT NULL,
    "TenantId" uuid,
    "UserId" uuid NOT NULL,
    "PaymentMethod" text,
    "PayeeAccount" text,
    "ExternalTradingCode" text,
    "Currency" text,
    "OriginalPaymentAmount" numeric(20,8) NOT NULL,
    "PaymentDiscount" numeric(20,8) NOT NULL,
    "ActualPaymentAmount" numeric(20,8) NOT NULL,
    "RefundAmount" numeric(20,8) NOT NULL,
    "PendingRefundAmount" numeric(20,8) NOT NULL,
    "CompletionTime" timestamp without time zone,
    "CanceledTime" timestamp without time zone,
    "ExtraProperties" text NOT NULL,
    "ConcurrencyStamp" character varying(40) NOT NULL,
    "CreationTime" timestamp without time zone NOT NULL,
    "CreatorId" uuid,
    "LastModificationTime" timestamp without time zone,
    "LastModifierId" uuid,
    "IsDeleted" boolean NOT NULL DEFAULT FALSE,
    "DeleterId" uuid,
    "DeletionTime" timestamp without time zone,
    CONSTRAINT "PK_EasyAbpPaymentServicePayments" PRIMARY KEY ("Id")
);

CREATE TABLE IF NOT EXISTS "EasyAbpPaymentServicePrepaymentAccounts" (
    "Id" uuid NOT NULL,
    "TenantId" uuid,
    "AccountGroupName" text,
    "UserId" uuid NOT NULL,
    "Balance" numeric(20,8) NOT NULL,
    "LockedBalance" numeric(20,8) NOT NULL,
    "PendingTopUpPaymentId" uuid,
    "PendingWithdrawalRecordId" uuid,
    "PendingWithdrawalAmount" numeric(20,8) NOT NULL,
    "ExtraProperties" text NOT NULL,
    "ConcurrencyStamp" character varying(40) NOT NULL,
    "CreationTime" timestamp without time zone NOT NULL,
    "CreatorId" uuid,
    "LastModificationTime" timestamp without time zone,
    "LastModifierId" uuid,
    "IsDeleted" boolean NOT NULL DEFAULT FALSE,
    "DeleterId" uuid,
    "DeletionTime" timestamp without time zone,
    CONSTRAINT "PK_EasyAbpPaymentServicePrepaymentAccounts" PRIMARY KEY ("Id")
);

CREATE TABLE IF NOT EXISTS "EasyAbpPaymentServicePrepaymentTransactions" (
    "Id" uuid NOT NULL,
    "TenantId" uuid,
    "AccountId" uuid NOT NULL,
    "AccountUserId" uuid NOT NULL,
    "PaymentId" uuid,
    "TransactionType" integer NOT NULL,
    "ActionName" text,
    "PaymentMethod" text,
    "ExternalTradingCode" text,
    "Currency" text,
    "ChangedBalance" numeric(20,8) NOT NULL,
    "OriginalBalance" numeric(20,8) NOT NULL,
    "ExtraProperties" text NOT NULL,
    "ConcurrencyStamp" character varying(40) NOT NULL,
    "CreationTime" timestamp without time zone NOT NULL,
    "CreatorId" uuid,
    CONSTRAINT "PK_EasyAbpPaymentServicePrepaymentTransactions" PRIMARY KEY ("Id")
);

CREATE TABLE IF NOT EXISTS "EasyAbpPaymentServicePrepaymentWithdrawalRecords" (
    "Id" uuid NOT NULL,
    "TenantId" uuid,
    "AccountId" uuid NOT NULL,
    "WithdrawalMethod" text,
    "Amount" numeric(20,8) NOT NULL,
    "CompletionTime" timestamp without time zone,
    "CancellationTime" timestamp without time zone,
    "ResultErrorCode" text,
    "ResultErrorMessage" text,
    "ExtraProperties" text NOT NULL,
    "ConcurrencyStamp" character varying(40) NOT NULL,
    "CreationTime" timestamp without time zone NOT NULL,
    "CreatorId" uuid,
    "LastModificationTime" timestamp without time zone,
    "LastModifierId" uuid,
    "IsDeleted" boolean NOT NULL DEFAULT FALSE,
    "DeleterId" uuid,
    "DeletionTime" timestamp without time zone,
    CONSTRAINT "PK_EasyAbpPaymentServicePrepaymentWithdrawalRecords" PRIMARY KEY ("Id")
);

CREATE TABLE IF NOT EXISTS "EasyAbpPaymentServicePrepaymentWithdrawalRequests" (
    "Id" uuid NOT NULL,
    "TenantId" uuid,
    "AccountId" uuid NOT NULL,
    "AccountUserId" uuid NOT NULL,
    "Amount" numeric(20,8) NOT NULL,
    "ReviewTime" timestamp without time zone,
    "ReviewerUserId" uuid,
    "IsApproved" boolean,
    "ExtraProperties" text NOT NULL,
    "ConcurrencyStamp" character varying(40) NOT NULL,
    "CreationTime" timestamp without time zone NOT NULL,
    "CreatorId" uuid,
    "LastModificationTime" timestamp without time zone,
    "LastModifierId" uuid,
    "IsDeleted" boolean NOT NULL DEFAULT FALSE,
    "DeleterId" uuid,
    "DeletionTime" timestamp without time zone,
    CONSTRAINT "PK_EasyAbpPaymentServicePrepaymentWithdrawalRequests" PRIMARY KEY ("Id")
);

CREATE TABLE IF NOT EXISTS "EasyAbpPaymentServiceRefunds" (
    "Id" uuid NOT NULL,
    "TenantId" uuid,
    "PaymentId" uuid NOT NULL,
    "RefundPaymentMethod" text,
    "ExternalTradingCode" text,
    "Currency" text,
    "RefundAmount" numeric(20,8) NOT NULL,
    "DisplayReason" text,
    "CustomerRemark" text,
    "StaffRemark" text,
    "CompletedTime" timestamp without time zone,
    "CanceledTime" timestamp without time zone,
    "ExtraProperties" text NOT NULL,
    "ConcurrencyStamp" character varying(40) NOT NULL,
    "CreationTime" timestamp without time zone NOT NULL,
    "CreatorId" uuid,
    "LastModificationTime" timestamp without time zone,
    "LastModifierId" uuid,
    "IsDeleted" boolean NOT NULL DEFAULT FALSE,
    "DeleterId" uuid,
    "DeletionTime" timestamp without time zone,
    CONSTRAINT "PK_EasyAbpPaymentServiceRefunds" PRIMARY KEY ("Id")
);

CREATE TABLE IF NOT EXISTS "EasyAbpPaymentServiceWeChatPayPaymentRecords" (
    "Id" uuid NOT NULL,
    "TenantId" uuid,
    "PaymentId" uuid NOT NULL,
    "AppId" text,
    "MchId" text,
    "OutTradeNo" text,
    "TransactionId" text,
    "TradeType" text,
    "BankType" text,
    "Attach" text,
    "ReturnCode" text,
    "ReturnMsg" text,
    "DeviceInfo" text,
    "ResultCode" text,
    "ErrCode" text,
    "ErrCodeDes" text,
    "Openid" text,
    "IsSubscribe" text,
    "TotalFee" integer NOT NULL,
    "SettlementTotalFee" integer,
    "FeeType" text,
    "CashFee" integer NOT NULL,
    "CashFeeType" text,
    "CouponFee" integer,
    "CouponCount" integer,
    "CouponTypes" text,
    "CouponIds" text,
    "CouponFees" text,
    "TimeEnd" text,
    "TradeState" text,
    "TradeStateDesc" text,
    "SuccessTime" text,
    "Payer" text,
    "Amount" text,
    "SceneInfo" text,
    "PromotionDetail" text,
    "ExtraProperties" text NOT NULL,
    "ConcurrencyStamp" character varying(40) NOT NULL,
    "CreationTime" timestamp without time zone NOT NULL,
    "CreatorId" uuid,
    CONSTRAINT "PK_EasyAbpPaymentServiceWeChatPayPaymentRecords" PRIMARY KEY ("Id")
);

CREATE TABLE IF NOT EXISTS "EasyAbpPaymentServiceWeChatPayRefundRecords" (
    "Id" uuid NOT NULL,
    "TenantId" uuid,
    "PaymentId" uuid NOT NULL,
    "OutRefundNo" text,
    "TransactionId" text,
    "OutTradeNo" text,
    "SuccessTime" text,
    "ReturnCode" text,
    "ReturnMsg" text,
    "AppId" text,
    "MchId" text,
    "RefundId" text,
    "TotalFee" integer NOT NULL,
    "SettlementTotalFee" integer,
    "RefundFee" integer NOT NULL,
    "SettlementRefundFee" integer,
    "FeeType" text,
    "CashFee" integer NOT NULL,
    "CashFeeType" text,
    "CashRefundFee" integer,
    "CouponRefundFee" integer,
    "CouponRefundCount" integer,
    "CouponTypes" text,
    "CouponIds" text,
    "CouponRefundFees" text,
    "RefundStatus" text,
    "RefundRecvAccout" text,
    "RefundAccount" text,
    "RefundRequestSource" text,
    "Channel" text,
    "UserReceivedAccount" text,
    "CreateTime" text,
    "Status" text,
    "FundsAccount" text,
    "Amount" text,
    "PromotionDetail" text,
    "ExtraProperties" text NOT NULL,
    "ConcurrencyStamp" character varying(40) NOT NULL,
    "CreationTime" timestamp without time zone NOT NULL,
    "CreatorId" uuid,
    CONSTRAINT "PK_EasyAbpPaymentServiceWeChatPayRefundRecords" PRIMARY KEY ("Id")
);

CREATE TABLE IF NOT EXISTS "OpenIddictApplications" (
    "Id" uuid NOT NULL,
    "ApplicationType" character varying(50),
    "ClientId" character varying(100),
    "ClientSecret" text,
    "ClientType" character varying(50),
    "ConsentType" character varying(50),
    "DisplayName" text,
    "DisplayNames" text,
    "JsonWebKeySet" text,
    "Permissions" text,
    "PostLogoutRedirectUris" text,
    "Properties" text,
    "RedirectUris" text,
    "Requirements" text,
    "Settings" text,
    "FrontChannelLogoutUri" text,
    "ClientUri" text,
    "LogoUri" text,
    "ExtraProperties" text NOT NULL,
    "ConcurrencyStamp" character varying(40) NOT NULL,
    "CreationTime" timestamp without time zone NOT NULL,
    "CreatorId" uuid,
    "LastModificationTime" timestamp without time zone,
    "LastModifierId" uuid,
    "IsDeleted" boolean NOT NULL DEFAULT FALSE,
    "DeleterId" uuid,
    "DeletionTime" timestamp without time zone,
    CONSTRAINT "PK_OpenIddictApplications" PRIMARY KEY ("Id")
);

CREATE TABLE IF NOT EXISTS "OpenIddictScopes" (
    "Id" uuid NOT NULL,
    "Description" text,
    "Descriptions" text,
    "DisplayName" text,
    "DisplayNames" text,
    "Name" character varying(200),
    "Properties" text,
    "Resources" text,
    "ExtraProperties" text NOT NULL,
    "ConcurrencyStamp" character varying(40) NOT NULL,
    "CreationTime" timestamp without time zone NOT NULL,
    "CreatorId" uuid,
    "LastModificationTime" timestamp without time zone,
    "LastModifierId" uuid,
    "IsDeleted" boolean NOT NULL DEFAULT FALSE,
    "DeleterId" uuid,
    "DeletionTime" timestamp without time zone,
    CONSTRAINT "PK_OpenIddictScopes" PRIMARY KEY ("Id")
);

CREATE TABLE IF NOT EXISTS "AbpAuditLogActions" (
    "Id" uuid NOT NULL,
    "TenantId" uuid,
    "AuditLogId" uuid NOT NULL,
    "ServiceName" character varying(256),
    "MethodName" character varying(128),
    "Parameters" character varying(2000),
    "ExecutionTime" timestamp without time zone NOT NULL,
    "ExecutionDuration" integer NOT NULL,
    "ExtraProperties" text,
    CONSTRAINT "PK_AbpAuditLogActions" PRIMARY KEY ("Id"),
    CONSTRAINT "FK_AbpAuditLogActions_AbpAuditLogs_AuditLogId" FOREIGN KEY ("AuditLogId") REFERENCES "AbpAuditLogs" ("Id") ON DELETE CASCADE
);

CREATE TABLE IF NOT EXISTS "AbpEntityChanges" (
    "Id" uuid NOT NULL,
    "AuditLogId" uuid NOT NULL,
    "TenantId" uuid,
    "ChangeTime" timestamp without time zone NOT NULL,
    "ChangeType" smallint NOT NULL,
    "EntityTenantId" uuid,
    "EntityId" character varying(128),
    "EntityTypeFullName" character varying(512) NOT NULL,
    "ExtraProperties" text,
    CONSTRAINT "PK_AbpEntityChanges" PRIMARY KEY ("Id"),
    CONSTRAINT "FK_AbpEntityChanges_AbpAuditLogs_AuditLogId" FOREIGN KEY ("AuditLogId") REFERENCES "AbpAuditLogs" ("Id") ON DELETE CASCADE
);

CREATE TABLE IF NOT EXISTS "AbpBlobs" (
    "Id" uuid NOT NULL,
    "ContainerId" uuid NOT NULL,
    "TenantId" uuid,
    "Name" character varying(256) NOT NULL,
    "Content" bytea,
    "ExtraProperties" text NOT NULL,
    "ConcurrencyStamp" character varying(40) NOT NULL,
    CONSTRAINT "PK_AbpBlobs" PRIMARY KEY ("Id"),
    CONSTRAINT "FK_AbpBlobs_AbpBlobContainers_ContainerId" FOREIGN KEY ("ContainerId") REFERENCES "AbpBlobContainers" ("Id") ON DELETE CASCADE
);

CREATE TABLE IF NOT EXISTS "AbpOrganizationUnitRoles" (
    "RoleId" uuid NOT NULL,
    "OrganizationUnitId" uuid NOT NULL,
    "TenantId" uuid,
    "CreationTime" timestamp without time zone NOT NULL,
    "CreatorId" uuid,
    CONSTRAINT "PK_AbpOrganizationUnitRoles" PRIMARY KEY ("OrganizationUnitId", "RoleId"),
    CONSTRAINT "FK_AbpOrganizationUnitRoles_AbpOrganizationUnits_OrganizationU~" FOREIGN KEY ("OrganizationUnitId") REFERENCES "AbpOrganizationUnits" ("Id") ON DELETE CASCADE,
    CONSTRAINT "FK_AbpOrganizationUnitRoles_AbpRoles_RoleId" FOREIGN KEY ("RoleId") REFERENCES "AbpRoles" ("Id") ON DELETE CASCADE
);

CREATE TABLE IF NOT EXISTS "AbpRoleClaims" (
    "Id" uuid NOT NULL,
    "RoleId" uuid NOT NULL,
    "TenantId" uuid,
    "ClaimType" character varying(256) NOT NULL,
    "ClaimValue" character varying(1024),
    CONSTRAINT "PK_AbpRoleClaims" PRIMARY KEY ("Id"),
    CONSTRAINT "FK_AbpRoleClaims_AbpRoles_RoleId" FOREIGN KEY ("RoleId") REFERENCES "AbpRoles" ("Id") ON DELETE CASCADE
);

CREATE TABLE IF NOT EXISTS "AbpTenantConnectionStrings" (
    "TenantId" uuid NOT NULL,
    "Name" character varying(64) NOT NULL,
    "Value" character varying(1024) NOT NULL,
    CONSTRAINT "PK_AbpTenantConnectionStrings" PRIMARY KEY ("TenantId", "Name"),
    CONSTRAINT "FK_AbpTenantConnectionStrings_AbpTenants_TenantId" FOREIGN KEY ("TenantId") REFERENCES "AbpTenants" ("Id") ON DELETE CASCADE
);

CREATE TABLE IF NOT EXISTS "AbpUserClaims" (
    "Id" uuid NOT NULL,
    "UserId" uuid NOT NULL,
    "TenantId" uuid,
    "ClaimType" character varying(256) NOT NULL,
    "ClaimValue" character varying(1024),
    CONSTRAINT "PK_AbpUserClaims" PRIMARY KEY ("Id"),
    CONSTRAINT "FK_AbpUserClaims_AbpUsers_UserId" FOREIGN KEY ("UserId") REFERENCES "AbpUsers" ("Id") ON DELETE CASCADE
);

CREATE TABLE IF NOT EXISTS "AbpUserLogins" (
    "UserId" uuid NOT NULL,
    "LoginProvider" character varying(64) NOT NULL,
    "TenantId" uuid,
    "ProviderKey" character varying(196) NOT NULL,
    "ProviderDisplayName" character varying(128),
    CONSTRAINT "PK_AbpUserLogins" PRIMARY KEY ("UserId", "LoginProvider"),
    CONSTRAINT "FK_AbpUserLogins_AbpUsers_UserId" FOREIGN KEY ("UserId") REFERENCES "AbpUsers" ("Id") ON DELETE CASCADE
);

CREATE TABLE IF NOT EXISTS "AbpUserOrganizationUnits" (
    "UserId" uuid NOT NULL,
    "OrganizationUnitId" uuid NOT NULL,
    "TenantId" uuid,
    "CreationTime" timestamp without time zone NOT NULL,
    "CreatorId" uuid,
    CONSTRAINT "PK_AbpUserOrganizationUnits" PRIMARY KEY ("OrganizationUnitId", "UserId"),
    CONSTRAINT "FK_AbpUserOrganizationUnits_AbpOrganizationUnits_OrganizationU~" FOREIGN KEY ("OrganizationUnitId") REFERENCES "AbpOrganizationUnits" ("Id") ON DELETE CASCADE,
    CONSTRAINT "FK_AbpUserOrganizationUnits_AbpUsers_UserId" FOREIGN KEY ("UserId") REFERENCES "AbpUsers" ("Id") ON DELETE CASCADE
);

CREATE TABLE IF NOT EXISTS "AbpUserPasskeys" (
    "CredentialId" bytea NOT NULL,
    "TenantId" uuid,
    "UserId" uuid NOT NULL,
    "Data" jsonb,
    CONSTRAINT "PK_AbpUserPasskeys" PRIMARY KEY ("CredentialId"),
    CONSTRAINT "FK_AbpUserPasskeys_AbpUsers_UserId" FOREIGN KEY ("UserId") REFERENCES "AbpUsers" ("Id") ON DELETE CASCADE
);

CREATE TABLE IF NOT EXISTS "AbpUserPasswordHistories" (
    "UserId" uuid NOT NULL,
    "Password" character varying(256) NOT NULL,
    "TenantId" uuid,
    "CreatedAt" timestamp with time zone NOT NULL,
    CONSTRAINT "PK_AbpUserPasswordHistories" PRIMARY KEY ("UserId", "Password"),
    CONSTRAINT "FK_AbpUserPasswordHistories_AbpUsers_UserId" FOREIGN KEY ("UserId") REFERENCES "AbpUsers" ("Id") ON DELETE CASCADE
);

CREATE TABLE IF NOT EXISTS "AbpUserRoles" (
    "UserId" uuid NOT NULL,
    "RoleId" uuid NOT NULL,
    "TenantId" uuid,
    CONSTRAINT "PK_AbpUserRoles" PRIMARY KEY ("UserId", "RoleId"),
    CONSTRAINT "FK_AbpUserRoles_AbpRoles_RoleId" FOREIGN KEY ("RoleId") REFERENCES "AbpRoles" ("Id") ON DELETE CASCADE,
    CONSTRAINT "FK_AbpUserRoles_AbpUsers_UserId" FOREIGN KEY ("UserId") REFERENCES "AbpUsers" ("Id") ON DELETE CASCADE
);

CREATE TABLE IF NOT EXISTS "AbpUserTokens" (
    "UserId" uuid NOT NULL,
    "LoginProvider" character varying(64) NOT NULL,
    "Name" character varying(128) NOT NULL,
    "TenantId" uuid,
    "Value" text,
    CONSTRAINT "PK_AbpUserTokens" PRIMARY KEY ("UserId", "LoginProvider", "Name"),
    CONSTRAINT "FK_AbpUserTokens_AbpUsers_UserId" FOREIGN KEY ("UserId") REFERENCES "AbpUsers" ("Id") ON DELETE CASCADE
);

CREATE TABLE IF NOT EXISTS "AppMenuGrants" (
    "Id" uuid NOT NULL,
    "MenuId" uuid NOT NULL,
    "TenantId" uuid,
    "ProviderName" character varying(64) NOT NULL,
    "ProviderKey" character varying(64) NOT NULL,
    CONSTRAINT "PK_AppMenuGrants" PRIMARY KEY ("Id"),
    CONSTRAINT "FK_AppMenuGrants_AppMenus_MenuId" FOREIGN KEY ("MenuId") REFERENCES "AppMenus" ("Id") ON DELETE CASCADE
);

CREATE TABLE IF NOT EXISTS "AppRoleDataScopeOrganizationUnits" (
    "RoleDataScopeId" uuid NOT NULL,
    "OrganizationUnitId" uuid NOT NULL,
    CONSTRAINT "PK_AppRoleDataScopeOrganizationUnits" PRIMARY KEY ("RoleDataScopeId", "OrganizationUnitId"),
    CONSTRAINT "FK_AppRoleDataScopeOrganizationUnits_AppRoleDataScopes_RoleDat~" FOREIGN KEY ("RoleDataScopeId") REFERENCES "AppRoleDataScopes" ("Id") ON DELETE CASCADE
);

CREATE TABLE IF NOT EXISTS "AppTenantPackageMenus" (
    "Id" uuid NOT NULL,
    "PackageId" uuid NOT NULL,
    "TemplateMenuId" uuid NOT NULL,
    CONSTRAINT "PK_AppTenantPackageMenus" PRIMARY KEY ("Id"),
    CONSTRAINT "FK_AppTenantPackageMenus_AppTenantPackages_PackageId" FOREIGN KEY ("PackageId") REFERENCES "AppTenantPackages" ("Id") ON DELETE CASCADE
);

CREATE TABLE IF NOT EXISTS "EasyAbpAbpDataDictionaryDataDictionaryItems" (
    "DataDictionaryId" uuid NOT NULL,
    "Code" character varying(64) NOT NULL,
    "TenantId" uuid,
    "DisplayText" character varying(64) NOT NULL,
    "Description" character varying(1024),
    "IsStatic" boolean NOT NULL,
    "CreationTime" timestamp without time zone NOT NULL,
    "CreatorId" uuid,
    "LastModificationTime" timestamp without time zone,
    "LastModifierId" uuid,
    CONSTRAINT "PK_EasyAbpAbpDataDictionaryDataDictionaryItems" PRIMARY KEY ("Code", "DataDictionaryId"),
    CONSTRAINT "FK_EasyAbpAbpDataDictionaryDataDictionaryItems_EasyAbpAbpDataD~" FOREIGN KEY ("DataDictionaryId") REFERENCES "EasyAbpAbpDataDictionaryDataDictionaries" ("Id") ON DELETE CASCADE
);

CREATE TABLE IF NOT EXISTS "EasyAbpPaymentServicePaymentItems" (
    "Id" uuid NOT NULL,
    "ItemType" text,
    "ItemKey" text,
    "OriginalPaymentAmount" numeric(20,8) NOT NULL,
    "PaymentDiscount" numeric(20,8) NOT NULL,
    "ActualPaymentAmount" numeric(20,8) NOT NULL,
    "RefundAmount" numeric(20,8) NOT NULL,
    "PendingRefundAmount" numeric(20,8) NOT NULL,
    "ExtraProperties" text,
    "PaymentId" uuid,
    "CreationTime" timestamp without time zone NOT NULL,
    "CreatorId" uuid,
    "LastModificationTime" timestamp without time zone,
    "LastModifierId" uuid,
    "IsDeleted" boolean NOT NULL DEFAULT FALSE,
    "DeleterId" uuid,
    "DeletionTime" timestamp without time zone,
    CONSTRAINT "PK_EasyAbpPaymentServicePaymentItems" PRIMARY KEY ("Id"),
    CONSTRAINT "FK_EasyAbpPaymentServicePaymentItems_EasyAbpPaymentServicePaym~" FOREIGN KEY ("PaymentId") REFERENCES "EasyAbpPaymentServicePayments" ("Id")
);

CREATE TABLE IF NOT EXISTS "EasyAbpPaymentServiceRefundItems" (
    "Id" uuid NOT NULL,
    "PaymentItemId" uuid NOT NULL,
    "RefundAmount" numeric(20,8) NOT NULL,
    "CustomerRemark" text,
    "StaffRemark" text,
    "ExtraProperties" text,
    "RefundId" uuid,
    "CreationTime" timestamp without time zone NOT NULL,
    "CreatorId" uuid,
    "LastModificationTime" timestamp without time zone,
    "LastModifierId" uuid,
    "IsDeleted" boolean NOT NULL DEFAULT FALSE,
    "DeleterId" uuid,
    "DeletionTime" timestamp without time zone,
    CONSTRAINT "PK_EasyAbpPaymentServiceRefundItems" PRIMARY KEY ("Id"),
    CONSTRAINT "FK_EasyAbpPaymentServiceRefundItems_EasyAbpPaymentServiceRefun~" FOREIGN KEY ("RefundId") REFERENCES "EasyAbpPaymentServiceRefunds" ("Id")
);

CREATE TABLE IF NOT EXISTS "OpenIddictAuthorizations" (
    "Id" uuid NOT NULL,
    "ApplicationId" uuid,
    "CreationDate" timestamp without time zone,
    "Properties" text,
    "Scopes" text,
    "Status" character varying(50),
    "Subject" character varying(400),
    "Type" character varying(50),
    "ExtraProperties" text NOT NULL,
    "ConcurrencyStamp" character varying(40) NOT NULL,
    CONSTRAINT "PK_OpenIddictAuthorizations" PRIMARY KEY ("Id"),
    CONSTRAINT "FK_OpenIddictAuthorizations_OpenIddictApplications_Application~" FOREIGN KEY ("ApplicationId") REFERENCES "OpenIddictApplications" ("Id")
);

CREATE TABLE IF NOT EXISTS "AbpEntityPropertyChanges" (
    "Id" uuid NOT NULL,
    "TenantId" uuid,
    "EntityChangeId" uuid NOT NULL,
    "NewValue" character varying(512),
    "OriginalValue" character varying(512),
    "PropertyName" character varying(128) NOT NULL,
    "PropertyTypeFullName" character varying(512) NOT NULL,
    CONSTRAINT "PK_AbpEntityPropertyChanges" PRIMARY KEY ("Id"),
    CONSTRAINT "FK_AbpEntityPropertyChanges_AbpEntityChanges_EntityChangeId" FOREIGN KEY ("EntityChangeId") REFERENCES "AbpEntityChanges" ("Id") ON DELETE CASCADE
);

CREATE TABLE IF NOT EXISTS "OpenIddictTokens" (
    "Id" uuid NOT NULL,
    "ApplicationId" uuid,
    "AuthorizationId" uuid,
    "CreationDate" timestamp without time zone,
    "ExpirationDate" timestamp without time zone,
    "Payload" text,
    "Properties" text,
    "RedemptionDate" timestamp without time zone,
    "ReferenceId" character varying(100),
    "Status" character varying(50),
    "Subject" character varying(400),
    "Type" character varying(150),
    "ExtraProperties" text NOT NULL,
    "ConcurrencyStamp" character varying(40) NOT NULL,
    CONSTRAINT "PK_OpenIddictTokens" PRIMARY KEY ("Id"),
    CONSTRAINT "FK_OpenIddictTokens_OpenIddictApplications_ApplicationId" FOREIGN KEY ("ApplicationId") REFERENCES "OpenIddictApplications" ("Id"),
    CONSTRAINT "FK_OpenIddictTokens_OpenIddictAuthorizations_AuthorizationId" FOREIGN KEY ("AuthorizationId") REFERENCES "OpenIddictAuthorizations" ("Id")
);

CREATE INDEX IF NOT EXISTS "IX_AbpAuditLogActions_AuditLogId" ON "AbpAuditLogActions" ("AuditLogId");

CREATE INDEX IF NOT EXISTS "IX_AbpAuditLogActions_TenantId_ServiceName_MethodName_Executio~" ON "AbpAuditLogActions" ("TenantId", "ServiceName", "MethodName", "ExecutionTime");

CREATE INDEX IF NOT EXISTS "IX_AbpAuditLogs_TenantId_ExecutionTime" ON "AbpAuditLogs" ("TenantId", "ExecutionTime");

CREATE INDEX IF NOT EXISTS "IX_AbpAuditLogs_TenantId_UserId_ExecutionTime" ON "AbpAuditLogs" ("TenantId", "UserId", "ExecutionTime");

CREATE INDEX IF NOT EXISTS "IX_AbpBackgroundJobs_ApplicationName_CompletionTime_IsAbandone~" ON "AbpBackgroundJobs" ("ApplicationName", "CompletionTime", "IsAbandoned", "NextTryTime");

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

CREATE UNIQUE INDEX IF NOT EXISTS "IX_AbpLinkUsers_SourceUserId_SourceTenantId_TargetUserId_Targe~" ON "AbpLinkUsers" ("SourceUserId", "SourceTenantId", "TargetUserId", "TargetTenantId");

CREATE INDEX IF NOT EXISTS "IX_AbpOrganizationUnitRoles_RoleId_OrganizationUnitId" ON "AbpOrganizationUnitRoles" ("RoleId", "OrganizationUnitId");

CREATE INDEX IF NOT EXISTS "IX_AbpOrganizationUnits_Code" ON "AbpOrganizationUnits" ("Code");

CREATE INDEX IF NOT EXISTS "IX_AbpOrganizationUnits_ParentId" ON "AbpOrganizationUnits" ("ParentId");

CREATE UNIQUE INDEX IF NOT EXISTS "IX_AbpPermissionGrants_TenantId_Name_ProviderName_ProviderKey" ON "AbpPermissionGrants" ("TenantId", "Name", "ProviderName", "ProviderKey");

CREATE UNIQUE INDEX IF NOT EXISTS "IX_AbpPermissionGroups_Name" ON "AbpPermissionGroups" ("Name");

CREATE INDEX IF NOT EXISTS "IX_AbpPermissions_GroupName" ON "AbpPermissions" ("GroupName");

CREATE UNIQUE INDEX IF NOT EXISTS "IX_AbpPermissions_ResourceName_Name" ON "AbpPermissions" ("ResourceName", "Name");

CREATE UNIQUE INDEX IF NOT EXISTS "IX_AbpResourcePermissionGrants_TenantId_Name_ResourceName_Reso~" ON "AbpResourcePermissionGrants" ("TenantId", "Name", "ResourceName", "ResourceKey", "ProviderName", "ProviderKey");

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

CREATE INDEX IF NOT EXISTS "IX_EasyAbpAbpDataDictionaryDataDictionaryItems_Code_DataDictio~" ON "EasyAbpAbpDataDictionaryDataDictionaryItems" ("Code", "DataDictionaryId");

CREATE INDEX IF NOT EXISTS "IX_EasyAbpAbpDataDictionaryDataDictionaryItems_DataDictionaryId" ON "EasyAbpAbpDataDictionaryDataDictionaryItems" ("DataDictionaryId");

CREATE INDEX IF NOT EXISTS "IX_EasyAbpFileManagementFiles_BlobName" ON "EasyAbpFileManagementFiles" ("BlobName");

CREATE UNIQUE INDEX IF NOT EXISTS "IX_EasyAbpFileManagementFiles_FileName_ParentId_OwnerUserId_Fi~" ON "EasyAbpFileManagementFiles" ("FileName", "ParentId", "OwnerUserId", "FileContainerName", "TenantId", "SoftDeletionToken");

CREATE INDEX IF NOT EXISTS "IX_EasyAbpFileManagementFiles_Hash" ON "EasyAbpFileManagementFiles" ("Hash");

CREATE INDEX IF NOT EXISTS "IX_EasyAbpFileManagementFiles_ParentId" ON "EasyAbpFileManagementFiles" ("ParentId");

CREATE INDEX IF NOT EXISTS "IX_EasyAbpFileManagementFiles_ParentId_OwnerUserId_FileContain~" ON "EasyAbpFileManagementFiles" ("ParentId", "OwnerUserId", "FileContainerName", "FileName");

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
