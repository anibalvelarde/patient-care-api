using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Neurocorp.Api.Core.BusinessObjects.Auth;
using Neurocorp.Api.Core.Entities;
using Neurocorp.Api.Core.Interfaces.Repositories;
using Neurocorp.Api.Infrastructure.Data;

namespace Neurocorp.Api.Infrastructure.Repositories;

/// <summary>
/// EF Core data access for authentication and the claims-aware authorization model.
/// </summary>
public class AuthRepository : IAuthRepository
{
    private readonly ApplicationDbContext _dbContext;

    public AuthRepository(ApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<User?> GetByEmailAsync(string email)
    {
        // Email has a UNIQUE constraint; tracked so login bookkeeping can be saved.
        return await _dbContext.Users
            .FirstOrDefaultAsync(u => u.Email == email);
    }

    public async Task<User?> GetByIdAsync(int id)
    {
        return await _dbContext.Users.FindAsync(id);
    }

    public async Task<IReadOnlyList<ClaimDto>> GetEffectiveClaimsAsync(int userId)
    {
        var roleIds = await _dbContext.Set<UserRole>()
            .Where(ur => ur.UserId == userId)
            .Select(ur => ur.RoleId)
            .ToListAsync();

        var roleClaims = await _dbContext.RoleClaims
            .Where(rc => roleIds.Contains(rc.RoleId))
            .Select(rc => new ClaimDto(rc.ClaimType, rc.ClaimValue))
            .ToListAsync();

        var userClaims = await _dbContext.UserClaims
            .Where(uc => uc.UserId == userId)
            .Select(uc => new { uc.ClaimType, uc.ClaimValue, uc.IsDenied })
            .ToListAsync();

        var denied = userClaims
            .Where(uc => uc.IsDenied)
            .Select(uc => new ClaimDto(uc.ClaimType, uc.ClaimValue))
            .ToHashSet();

        var granted = userClaims
            .Where(uc => !uc.IsDenied)
            .Select(uc => new ClaimDto(uc.ClaimType, uc.ClaimValue));

        // (role claims UNION user grants) MINUS user denials, de-duplicated.
        return roleClaims
            .Concat(granted)
            .Distinct()
            .Where(c => !denied.Contains(c))
            .ToList();
    }

    public async Task<IReadOnlyList<string>> GetRoleNamesAsync(int userId)
    {
        var roleIds = await _dbContext.Set<UserRole>()
            .Where(ur => ur.UserId == userId)
            .Select(ur => ur.RoleId)
            .ToListAsync();

        return await _dbContext.RoleTypes
            .Where(r => roleIds.Contains(r.Id))
            .Select(r => r.Name)
            .ToListAsync();
    }

    public async Task SaveUserAsync(User user)
    {
        // user is typically already tracked (from GetByEmail/GetById); ensure it is attached.
        if (_dbContext.Entry(user).State == EntityState.Detached)
        {
            _dbContext.Users.Attach(user);
            _dbContext.Entry(user).State = EntityState.Modified;
        }
        await _dbContext.SaveChangesAsync();
    }
}
