using System;
using System.Collections.Generic;
using System.Threading;
using Volo.Abp.DependencyInjection;

namespace AbpAdmin.DataScopes;

/// <summary>
/// <see cref="ICurrentDataScopeState"/> 的默认实现。
/// 必须是单例 + 静态 <see cref="AsyncLocal{T}"/>——不能是 scoped。
/// scoped 方案会让每个 <see cref="IHasDataScope"/> 实体对每个用户返回 0 条（必现），
/// 因为 DbContext 与中间件可能解析到不同的 scoped 实例。
/// <para>形状照抄 <c>AsyncLocalCurrentTenantAccessor</c> + <c>CurrentTenant.Change</c>。</para>
/// </summary>
public class CurrentDataScopeState : ICurrentDataScopeState, ISingletonDependency
{
    private static readonly AsyncLocal<DataScopeSnapshot?> Current = new();

    public bool IsAll => Current.Value?.IsAll ?? false;

    public bool SelfOnly => Current.Value?.SelfOnly ?? false;

    public Guid? UserId => Current.Value?.UserId;

    public IReadOnlyCollection<Guid> OrganizationUnitIds =>
        Current.Value?.OrganizationUnitIds ?? Array.Empty<Guid>();

    public IDisposable Change(DataScopeSnapshot? snapshot)
    {
        var parent = Current.Value;
        Current.Value = snapshot;
        return new DisposeAction(() => Current.Value = parent);
    }

    private sealed class DisposeAction : IDisposable
    {
        private readonly Action _action;
        private bool _disposed;

        public DisposeAction(Action action) => _action = action;

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _action();
        }
    }
}
