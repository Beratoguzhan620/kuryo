namespace Kuryo.Core.Users;

public sealed class User
{
    public const int EmailMaxLength = 254;
    public const int DisplayNameMinLength = 2;
    public const int DisplayNameMaxLength = 50;

    private User()
    {
    }

    public Guid Id { get; private set; }

    public string Email { get; private set; } = string.Empty;

    public string DisplayName { get; private set; } = string.Empty;

    public string PasswordHash { get; private set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; private set; }

    public static User Register(string email, string displayName, DateTimeOffset now)
    {
        var name = displayName.Trim();
        if (name.Length is < DisplayNameMinLength or > DisplayNameMaxLength)
        {
            throw new DomainException(ErrorCodes.ValidationFailed,
                $"A display name has {DisplayNameMinLength} to {DisplayNameMaxLength} characters.");
        }

        return new User { Id = Guid.CreateVersion7(now), Email = NormalizeEmail(email), DisplayName = name, CreatedAt = now };
    }

    public static string NormalizeEmail(string email) => email.Trim().ToLowerInvariant();

    public void SetPasswordHash(string hash) => PasswordHash = hash;
}
