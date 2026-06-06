namespace Neurocorp.Api.Core.Interfaces.Services;

/// <summary>
/// Exposes the identity of the user making the current request. Implemented in the Web
/// layer over <c>IHttpContextAccessor</c> and consumed by <c>ApplicationDbContext</c> so
/// audit columns (<c>LastUpdatedByUserId</c>) are stamped with the real authenticated user.
/// </summary>
public interface ICurrentUserService
{
    /// <summary>The authenticated user's SystemUser.UserID, or null when unauthenticated.</summary>
    int? UserId { get; }

    bool IsAuthenticated { get; }
}
