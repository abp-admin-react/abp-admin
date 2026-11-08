using System;
using System.Collections.Generic;
using System.Linq;
using AbpAdmin.ClientErrors;
using AbpAdmin.Imaging;
using Volo.Abp.DependencyInjection;

namespace AbpAdmin.Domain.ClientErrors;

/// <summary>
/// 公开错误定义注册表。框架基础定义在此注册；模块通过其它 IPublicErrorDefinitionProvider 追加。
/// </summary>
[ExposeServices(typeof(IPublicErrorDefinitionProvider))]
public class PublicErrorDefinitionRegistry : IPublicErrorDefinitionProvider, ISingletonDependency
{
    private readonly List<PublicErrorDefinition> _definitions = new();

    public PublicErrorDefinitionRegistry()
    {
        RegisterBaseDefinitions();
    }

    public IReadOnlyList<PublicErrorDefinition> GetDefinitions() => _definitions.AsReadOnly();

    public void Register(PublicErrorDefinition definition)
    {
        if (string.IsNullOrEmpty(definition.InternalCode))
        {
            throw new ArgumentException("InternalCode is required.", nameof(definition));
        }

        if (string.IsNullOrEmpty(definition.PublicCode))
        {
            throw new ArgumentException("PublicCode is required.", nameof(definition));
        }

        if (_definitions.Any(d => d.InternalCode == definition.InternalCode))
        {
            throw new InvalidOperationException(
                $"Duplicate internal code: {definition.InternalCode}");
        }

        var existingPublic = _definitions.FirstOrDefault(d => d.PublicCode == definition.PublicCode);
        if (existingPublic != null
            && !PublicErrorCatalogStartupValidator.AreConsistent(existingPublic, definition))
        {
            throw new InvalidOperationException(
                $"Inconsistent public code '{definition.PublicCode}'.");
        }

        _definitions.Add(definition);
    }

    private void RegisterBaseDefinitions()
    {
        var unexpectedParameters = new PublicErrorParameterDefinition[]
        {
            new()
            {
                Name = ClientErrorFieldNames.RetryAfterSeconds,
                Source = PublicErrorParameterSource.ExceptionData,
                SourceKey = nameof(AbpAdminImageProcessingBusyException.RetryAfterSeconds),
                ValueType = PublicErrorParameterValueType.Int32,
                MinValue = 0,
                MaxValue = 86400,
                CanReturnToClient = true,
            },
        };

        Register(new PublicErrorDefinition
        {
            InternalCode = ClientErrorCodes.AuthenticationRequired,
            PublicCode = ClientErrorCodes.AuthenticationRequired,
            ResourceType = typeof(ClientErrorResource),
            MessageKey = "AuthenticationRequired",
            Kind = PublicErrorKind.Authentication,
        });
        Register(new PublicErrorDefinition
        {
            InternalCode = ClientErrorCodes.Forbidden,
            PublicCode = ClientErrorCodes.Forbidden,
            ResourceType = typeof(ClientErrorResource),
            MessageKey = "Forbidden",
            Kind = PublicErrorKind.Forbidden,
        });
        Register(new PublicErrorDefinition
        {
            InternalCode = ClientErrorCodes.ValidationFailed,
            PublicCode = ClientErrorCodes.ValidationFailed,
            ResourceType = typeof(ClientErrorResource),
            MessageKey = "ValidationFailed",
            Kind = PublicErrorKind.Validation,
        });
        Register(new PublicErrorDefinition
        {
            InternalCode = ClientErrorCodes.NotFound,
            PublicCode = ClientErrorCodes.NotFound,
            ResourceType = typeof(ClientErrorResource),
            MessageKey = "NotFound",
            Kind = PublicErrorKind.NotFound,
        });
        var rateLimitedParameters = new PublicErrorParameterDefinition[]
        {
            new()
            {
                Name = ClientErrorFieldNames.RetryAfterSeconds,
                Source = PublicErrorParameterSource.RetryAfterSeconds,
                ValueType = PublicErrorParameterValueType.Int32,
                MinValue = 0,
                MaxValue = 86400,
                CanReturnToClient = true,
            },
        };

        Register(new PublicErrorDefinition
        {
            InternalCode = ClientErrorCodes.RateLimited,
            PublicCode = ClientErrorCodes.RateLimited,
            ResourceType = typeof(ClientErrorResource),
            MessageKey = "RateLimited",
            Kind = PublicErrorKind.RateLimited,
            AllowedParameters = rateLimitedParameters,
        });
        Register(new PublicErrorDefinition
        {
            InternalCode = ClientErrorCodes.Conflict,
            PublicCode = ClientErrorCodes.Conflict,
            ResourceType = typeof(ClientErrorResource),
            MessageKey = "Conflict",
            Kind = PublicErrorKind.Conflict,
        });
        Register(new PublicErrorDefinition
        {
            InternalCode = ClientErrorCodes.OperationFailed,
            PublicCode = ClientErrorCodes.OperationFailed,
            ResourceType = typeof(ClientErrorResource),
            MessageKey = "OperationFailed",
            Kind = PublicErrorKind.Business,
        });
        Register(new PublicErrorDefinition
        {
            InternalCode = ClientErrorCodes.Unexpected,
            PublicCode = ClientErrorCodes.Unexpected,
            ResourceType = typeof(ClientErrorResource),
            MessageKey = "Unexpected",
            Kind = PublicErrorKind.Server,
            AllowedParameters = unexpectedParameters,
            PublicHttpStatuses = [500, 503],
        });
        Register(new PublicErrorDefinition
        {
            InternalCode = AbpAdminDomainErrorCodes.Imaging.ImageProcessingBusy,
            PublicCode = ClientErrorCodes.Unexpected,
            ResourceType = typeof(ClientErrorResource),
            MessageKey = "Unexpected",
            Kind = PublicErrorKind.Server,
            AllowedParameters = unexpectedParameters,
            PublicHttpStatuses = [500, 503],
        });
        Register(new PublicErrorDefinition
        {
            InternalCode = AbpAdminDomainErrorCodes.RateLimiting.OperationRateLimitExceeded,
            PublicCode = ClientErrorCodes.RateLimited,
            ResourceType = typeof(ClientErrorResource),
            MessageKey = "RateLimited",
            Kind = PublicErrorKind.RateLimited,
            AllowedParameters = rateLimitedParameters,
        });
        Register(new PublicErrorDefinition
        {
            InternalCode = AbpAdminDomainErrorCodes.RateLimiting.OperationBanned,
            PublicCode = ClientErrorCodes.RateLimited,
            ResourceType = typeof(ClientErrorResource),
            MessageKey = "RateLimited",
            Kind = PublicErrorKind.RateLimited,
            AllowedParameters = rateLimitedParameters,
        });
        Register(new PublicErrorDefinition
        {
            InternalCode = AbpAdminDomainErrorCodes.Payments.PrepaymentAccountIdRequired,
            PublicCode = AbpAdminDomainErrorCodes.Payments.PrepaymentAccountIdRequired,
            ResourceType = typeof(ClientErrorResource),
            MessageKey = "PrepaymentAccountIdRequired",
            Kind = PublicErrorKind.Validation,
        });
        Register(new PublicErrorDefinition
        {
            InternalCode = AbpAdminDomainErrorCodes.Payments.PrepaymentAccountNotFound,
            PublicCode = AbpAdminDomainErrorCodes.Payments.PrepaymentAccountNotFound,
            ResourceType = typeof(ClientErrorResource),
            MessageKey = "PrepaymentAccountNotFound",
            Kind = PublicErrorKind.NotFound,
            AllowedParameters =
            [
                new PublicErrorParameterDefinition
                {
                    Name = "AccountId",
                    Source = PublicErrorParameterSource.ExceptionData,
                    SourceKey = "AccountId",
                    ValueType = PublicErrorParameterValueType.String,
                    MaxLength = 64,
                    CanReturnToClient = true,
                },
            ],
        });
        Register(new PublicErrorDefinition
        {
            InternalCode = AbpAdminDomainErrorCodes.Files.ContentMismatch,
            PublicCode = AbpAdminDomainErrorCodes.Files.ContentMismatch,
            ResourceType = typeof(ClientErrorResource),
            MessageKey = "FileContentMismatch",
            Kind = PublicErrorKind.Validation,
            AllowedParameters =
            [
                new PublicErrorParameterDefinition
                {
                    Name = ManagementCompatibilityDataWhitelist.FileUploadRejection.FileName,
                    Source = PublicErrorParameterSource.ExceptionData,
                    SourceKey = ManagementCompatibilityDataWhitelist.FileUploadRejection.FileName,
                    ValueType = PublicErrorParameterValueType.String,
                    MaxLength = 256,
                    CanReturnToClient = true,
                },
                new PublicErrorParameterDefinition
                {
                    Name = ManagementCompatibilityDataWhitelist.FileUploadRejection.Reason,
                    Source = PublicErrorParameterSource.ExceptionData,
                    SourceKey = ManagementCompatibilityDataWhitelist.FileUploadRejection.Reason,
                    ValueType = PublicErrorParameterValueType.String,
                    MaxLength = 64,
                    CanReturnToClient = true,
                },
            ],
        });
        Register(new PublicErrorDefinition
        {
            InternalCode = "AbpAdmin:Export:SyncThresholdExceeded",
            PublicCode = "AbpAdmin:Export:SyncThresholdExceeded",
            ResourceType = typeof(ClientErrorResource),
            MessageKey = "SyncExportThresholdExceeded",
            Kind = PublicErrorKind.Business,
        });

        // 反提权守卫码登记（安全审计 run-3 契约漂移收口）：IdentityClaimAuthorityGuard 抛出的
        // CannotCarryRole 与 OrganizationUnitRoleConferralGuard/RoleDataScope 写面抛出的
        // RoleDataScopeEscalation 此前未登记进公开目录——管线 fail-closed 把它们展平成
        // 400 Client:OperationFailed（强制语义完好，但管理端与 sectest 断言失去专属码，
        // 且 HTTP 状态从 403 漂到 400）。Forbidden 语义（拒绝越权授予）与公共码保留：
        // 内部码即公开码（与 FileContentMismatch 同模式），HTTP 403。
        Register(new PublicErrorDefinition
        {
            InternalCode = AbpAdminIdentityClaimsErrorCodes.CannotCarryRole,
            PublicCode = AbpAdminIdentityClaimsErrorCodes.CannotCarryRole,
            ResourceType = typeof(ClientErrorResource),
            MessageKey = "CannotCarryRole",
            Kind = PublicErrorKind.Forbidden,
        });
        Register(new PublicErrorDefinition
        {
            InternalCode = AbpAdminDomainErrorCodes.DataScopes.RoleDataScopeEscalation,
            PublicCode = AbpAdminDomainErrorCodes.DataScopes.RoleDataScopeEscalation,
            ResourceType = typeof(ClientErrorResource),
            MessageKey = "RoleDataScopeEscalation",
            Kind = PublicErrorKind.Forbidden,
        });
    }

    IEnumerable<PublicErrorDefinition> IPublicErrorDefinitionProvider.GetDefinitions()
    {
        return GetDefinitions();
    }
}
