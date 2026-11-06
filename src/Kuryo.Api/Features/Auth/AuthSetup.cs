using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Kuryo.Api.Features.Auth;

internal static class AuthSetup
{
    public static IServiceCollection AddKuryoAuth(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<JwtOptions>()
            .Bind(configuration.GetSection(JwtOptions.Section))
            .Validate(o => o.AccessTokenMinutes is > 0 and <= 24 * 60, "Jwt:AccessTokenMinutes must be between 1 and 1440.")
            .ValidateOnStart();

        services.AddSingleton<SigningKeyProvider>();
        services.AddSingleton<TokenService>();
        services.AddSingleton<PasswordVerifier>();

        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer();
        services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
            .Configure<SigningKeyProvider, IOptions<JwtOptions>, TimeProvider>((bearer, keys, jwt, clock) =>
            {
                bearer.MapInboundClaims = false;
                bearer.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidIssuer = jwt.Value.Issuer,
                    ValidAudience = jwt.Value.Audience,
                    IssuerSigningKey = keys.Key,
                    ValidateIssuerSigningKey = true,
                    ValidAlgorithms = [SecurityAlgorithms.HmacSha256],
                    NameClaimType = JwtRegisteredClaimNames.Sub,
                    ClockSkew = TimeSpan.FromSeconds(30),
                    // Token lifetimes follow the application's clock, the same one that issued them.
                    LifetimeValidator = (notBefore, expires, _, parameters) =>
                    {
                        var now = clock.GetUtcNow().UtcDateTime;
                        return (notBefore is null || notBefore <= now + parameters.ClockSkew)
                            && (expires is null || expires > now - parameters.ClockSkew);
                    },
                };
            });

        // Roles are per company and read from the database on every request (CompanyAccess), not from the token.
        services.AddAuthorization();
        return services;
    }
}
