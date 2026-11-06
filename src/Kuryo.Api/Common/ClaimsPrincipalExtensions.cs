using System.Security.Claims;
using Microsoft.IdentityModel.JsonWebTokens;

namespace Kuryo.Api.Common;

internal static class ClaimsPrincipalExtensions
{
    /// <summary>The signed-in user. Endpoints that call this require authentication, so the claim is present.</summary>
    public static Guid UserId(this ClaimsPrincipal user) =>
        Guid.Parse(user.FindFirstValue(JwtRegisteredClaimNames.Sub)
            ?? throw new InvalidOperationException("The principal has no subject claim."));
}
