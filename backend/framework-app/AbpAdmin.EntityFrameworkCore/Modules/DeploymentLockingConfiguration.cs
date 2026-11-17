using System;
using System.Linq;
using Medallion.Threading;
using Medallion.Threading.Redis;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using StackExchange.Redis;
using Volo.Abp;
using Volo.Abp.DistributedLocking;

namespace AbpAdmin.Modules;

/// <summary>Host and DbMigrator must coordinate through the same configured lock provider.</summary>
public static class DeploymentLockingConfiguration
{
    public static SharedRedisConnection? Configure(IServiceCollection services, IConfiguration configuration)
    {
        var raw = configuration["Redis:IsEnabled"];
        var enabled = true;
        if (!string.IsNullOrEmpty(raw) && !bool.TryParse(raw, out enabled))
            throw new AbpInitializationException("Redis:IsEnabled must be true or false.");
        var connectionString = configuration["Redis:Configuration"];
        if (!enabled || string.IsNullOrWhiteSpace(connectionString))
        {
            services.RemoveAll<MedallionAbpDistributedLock>();
            services.Replace(ServiceDescriptor.Singleton<IAbpDistributedLock, LocalAbpDistributedLock>());
            return null;
        }
        var existing = services.FirstOrDefault(d => d.ServiceType == typeof(SharedRedisConnectionRegistration))?.ImplementationInstance;
        if (existing is SharedRedisConnectionRegistration registration) return registration.Connection;
        var connection = new SharedRedisConnection(connectionString);
        // The non-disposable registration makes repeated configuration idempotent. The connection
        // itself is factory-registered so the container owns disposal (unlike AddSingleton(instance)).
        services.AddSingleton(new SharedRedisConnectionRegistration(connection));
        services.AddSingleton<SharedRedisConnection>(_ => connection);
        services.AddSingleton<IDistributedLockProvider>(sp => new RedisDistributedSynchronizationProvider(sp.GetRequiredService<SharedRedisConnection>().GetDatabase()));
        services.AddTransient<IAbpDistributedLock, MedallionAbpDistributedLock>();
        return connection;
    }
}

internal sealed record SharedRedisConnectionRegistration(SharedRedisConnection Connection);

/// <summary>Lazy Redis connection shared by locking and Host DataProtection; creating the service graph never dials Redis.</summary>
public sealed class SharedRedisConnection(string configuration) : IDisposable
{
    private static readonly TimeSpan FailureCooldown = TimeSpan.FromSeconds(30);
    private readonly object _gate = new();
    private ConnectionMultiplexer? _multiplexer;
    private DateTime _lastFailureUtc = DateTime.MinValue;
    private Exception? _lastFailure;
    private bool _disposed;

    public IDatabase GetDatabase()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_multiplexer != null) return _multiplexer.GetDatabase();
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_multiplexer != null) return _multiplexer.GetDatabase();
            if (DateTime.UtcNow - _lastFailureUtc < FailureCooldown)
                throw new InvalidOperationException("Redis connection failed within the last 30 seconds (failure cooldown, retry deferred).", _lastFailure);
            var options = ConfigurationOptions.Parse(configuration);
            options.AbortOnConnectFail = true;
            try { _multiplexer = ConnectionMultiplexer.Connect(options); }
            catch (Exception ex)
            {
                _lastFailureUtc = DateTime.UtcNow;
                _lastFailure = ex;
                throw;
            }
            return _multiplexer.GetDatabase();
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            _disposed = true;
            _multiplexer?.Dispose();
            _multiplexer = null;
        }
    }
}
