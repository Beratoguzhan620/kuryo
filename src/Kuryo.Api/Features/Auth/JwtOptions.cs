namespace Kuryo.Api.Features.Auth;

internal sealed class JwtOptions
{
    public const string Section = "Jwt";

    public string Issuer { get; set; } = "kuryo";

    public string Audience { get; set; } = "kuryo-clients";

    /// <summary>HMAC key, at least 32 bytes. Comes from user secrets or an environment variable, never from the repository.</summary>
    public string? SigningKey { get; set; }

    public int AccessTokenMinutes { get; set; } = 60;

    /// <summary>Local convenience: without a configured key, generate a throwaway one per process.</summary>
    public bool AllowEphemeralKey { get; set; }
}
