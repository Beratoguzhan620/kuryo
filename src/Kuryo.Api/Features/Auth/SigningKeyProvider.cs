using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace Kuryo.Api.Features.Auth;

internal sealed class SigningKeyProvider
{
    private const int MinimumKeyBytes = 32;

    public SigningKeyProvider(IOptions<JwtOptions> options, ILogger<SigningKeyProvider> logger)
    {
        var configured = options.Value.SigningKey;
        if (!string.IsNullOrWhiteSpace(configured))
        {
            var bytes = Encoding.UTF8.GetBytes(configured);
            if (bytes.Length < MinimumKeyBytes)
            {
                throw new InvalidOperationException($"Jwt:SigningKey must be at least {MinimumKeyBytes} bytes long.");
            }

            Key = new SymmetricSecurityKey(bytes);
        }
        else if (options.Value.AllowEphemeralKey)
        {
            Key = new SymmetricSecurityKey(RandomNumberGenerator.GetBytes(64));
            logger.LogWarning("Jwt:SigningKey is not set; using a throwaway key. Issued tokens stop working when the process restarts.");
        }
        else
        {
            throw new InvalidOperationException(
                "Jwt:SigningKey is not configured. Set it with 'dotnet user-secrets' or the Jwt__SigningKey environment variable.");
        }
    }

    public SymmetricSecurityKey Key { get; }
}
