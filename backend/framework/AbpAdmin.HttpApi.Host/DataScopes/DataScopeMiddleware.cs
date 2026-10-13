using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Uow;
using Volo.Abp.Users;

namespace AbpAdmin.DataScopes;

/// <summary>
/// 数据范围中间件。在请求开始时解析当前用户的数据范围快照，
/// 灌入 <see cref="ICurrentDataScopeState"/> 的 AsyncLocal，供下游全局筛选器读取。
///
/// 管线位置：在 <c>UseDynamicClaims()</c> 之后（角色可能来自动态 claims），
/// 在 <c>UseConfiguredEndpoints()</c> 之前。
///
/// 注意：中间件里 <c>UnitOfWorkManager.Current</c> 是 null（<c>UseUnitOfWork</c> 走的是
/// <c>Reserve()</c>，<c>AmbientUnitOfWork.GetCurrentByChecking()</c> 会跳过 IsReserved 的 UoW），
/// 所以必须自己开一个 UoW 来查库。
/// </summary>
public class DataScopeMiddleware : IMiddleware, ITransientDependency
{
    private readonly ICurrentUser _currentUser;
    private readonly ICurrentDataScopeProvider _dataScopeProvider;
    private readonly ICurrentDataScopeState _dataScopeState;
    private readonly IUnitOfWorkManager _unitOfWorkManager;

    public DataScopeMiddleware(
        ICurrentUser currentUser,
        ICurrentDataScopeProvider dataScopeProvider,
        ICurrentDataScopeState dataScopeState,
        IUnitOfWorkManager unitOfWorkManager)
    {
        _currentUser = currentUser;
        _dataScopeProvider = dataScopeProvider;
        _dataScopeState = dataScopeState;
        _unitOfWorkManager = unitOfWorkManager;
    }

    public async Task InvokeAsync(HttpContext context, RequestDelegate next)
    {
        DataScopeSnapshot? snapshot = null;

        if (_currentUser.IsAuthenticated)
        {
            using var uow = _unitOfWorkManager.Begin(requiresNew: true, isTransactional: false);
            snapshot = await _dataScopeProvider.GetAsync();
            await uow.CompleteAsync();
        }

        using (_dataScopeState.Change(snapshot))
        {
            await next(context);
        }
    }
}
