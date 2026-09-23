using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Dynamic.Core;
using System.Threading.Tasks;
using AbpAdmin.IpRegions;
using AbpAdmin.Permissions;
using Microsoft.AspNetCore.Authorization;
using Volo.Abp.Application.Dtos;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Validation;

namespace AbpAdmin.OperationLogs;

[Authorize(AbpAdminPermissions.OperationLogs.Default)]
public class OperationLogAppService : AbpAdminAppService, IOperationLogAppService
{
    private static readonly string[] SortableFields =
    [
        nameof(OperationLog.ExecutionTime),
        nameof(OperationLog.Duration),
        nameof(OperationLog.Type),
        nameof(OperationLog.SubType),
        nameof(OperationLog.UserName),
        nameof(OperationLog.BizId),
        nameof(OperationLog.Success),
    ];

    private readonly IRepository<OperationLog, Guid> _repository;
    private readonly IIpLocationResolver _ipLocationResolver;

    public OperationLogAppService(
        IRepository<OperationLog, Guid> repository,
        IIpLocationResolver ipLocationResolver)
    {
        _repository = repository;
        _ipLocationResolver = ipLocationResolver;
    }

    public virtual async Task<PagedResultDto<OperationLogDto>> GetListAsync(GetOperationLogListInput input)
    {
        ValidateSorting(input.Sorting);

        var queryable = await _repository.GetQueryableAsync();

        if (!string.IsNullOrWhiteSpace(input.Filter))
        {
            var filter = input.Filter;
            queryable = queryable.Where(x =>
                x.SubType.Contains(filter) ||
                x.Action!.Contains(filter) ||
                x.BizId!.Contains(filter) ||
                x.UserName!.Contains(filter));
        }

        if (!string.IsNullOrWhiteSpace(input.Type))
        {
            var type = input.Type;
            queryable = queryable.Where(x => x.Type.Contains(type));
        }

        if (!string.IsNullOrWhiteSpace(input.SubType))
        {
            var subType = input.SubType;
            queryable = queryable.Where(x => x.SubType.Contains(subType));
        }

        if (input.Success.HasValue)
        {
            queryable = queryable.Where(x => x.Success == input.Success.Value);
        }

        if (input.UserId.HasValue)
        {
            queryable = queryable.Where(x => x.UserId == input.UserId.Value);
        }

        if (!string.IsNullOrWhiteSpace(input.CorrelationId))
        {
            var correlationId = input.CorrelationId;
            queryable = queryable.Where(x => x.CorrelationId == correlationId);
        }

        if (input.StartTime.HasValue)
        {
            var start = input.StartTime.Value;
            queryable = queryable.Where(x => x.ExecutionTime >= start);
        }

        if (input.EndTime.HasValue)
        {
            var end = input.EndTime.Value;
            queryable = queryable.Where(x => x.ExecutionTime <= end);
        }

        queryable = queryable.OrderBy(string.IsNullOrWhiteSpace(input.Sorting) ? "ExecutionTime desc" : input.Sorting);

        var totalCount = await AsyncExecuter.CountAsync(queryable);
        var logs = await AsyncExecuter.ToListAsync(
            queryable.Skip(input.SkipCount).Take(input.MaxResultCount));

        // IP 归属地按当页去重解析（内部走缓存），不在查询里 join
        var locationMap = await _ipLocationResolver.ResolveManyAsync(logs.Select(x => x.ClientIpAddress));

        return new PagedResultDto<OperationLogDto>(
            totalCount,
            logs.Select(log => new OperationLogDto
            {
                Id = log.Id,
                UserId = log.UserId,
                UserName = log.UserName,
                Type = log.Type,
                SubType = log.SubType,
                BizId = log.BizId,
                Action = log.Action,
                Extra = log.Extra,
                Success = log.Success,
                ErrorMessage = log.ErrorMessage,
                RequestMethod = log.RequestMethod,
                RequestUrl = log.RequestUrl,
                ClientIpAddress = log.ClientIpAddress,
                IpLocation = log.ClientIpAddress != null && locationMap.TryGetValue(log.ClientIpAddress, out var location)
                    ? location
                    : null,
                UserAgent = log.UserAgent,
                CorrelationId = log.CorrelationId,
                Duration = log.Duration,
                ExecutionTime = log.ExecutionTime,
            }).ToList());
    }

    /// <summary>
    /// Sorting 是客户端自由文本且直接进 Dynamic LINQ OrderBy：白名单外输入
    /// （注入载荷或拼错的字段）在解析层只会 500，这里前置转成校验错（AbpValidationException → 400）。
    /// </summary>
    private void ValidateSorting(string? sorting)
    {
        if (SortingWhitelist.IsValid(sorting, SortableFields))
        {
            return;
        }

        throw new AbpValidationException(
            L["AbpAdmin:InvalidSorting", sorting]);
    }
}
