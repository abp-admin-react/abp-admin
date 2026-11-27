using System;
using System.Threading.Tasks;

namespace AbpAdmin.Identity;

/// <summary>Resolves ABP's name-based grant key under a role-row lock held by the caller's transaction.</summary>
public interface IRolePermissionTarget
{
    Task<string> LockAsync(Guid id, string expectedRoleName, bool forUpdate);
}
