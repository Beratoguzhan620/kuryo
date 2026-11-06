namespace Kuryo.Core;

public static class Text
{
    /// <summary>Collapses runs of white space and checks the length; <paramref name="what"/> starts the error message.</summary>
    public static string Clean(string? value, int min, int max, string what)
    {
        var clean = string.Join(' ', (value ?? string.Empty).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return clean.Length >= min && clean.Length <= max
            ? clean
            : throw new DomainException(ErrorCodes.ValidationFailed, $"{what} has {min} to {max} characters.");
    }

    /// <summary>Like <see cref="Clean"/> for longer text: keeps line breaks, trims each line, allows empty.</summary>
    public static string CleanParagraphs(string? value, int max, string what)
    {
        var lines = (value ?? string.Empty).Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n').Select(l => l.Trim());
        var clean = string.Join('\n', lines).Trim();
        while (clean.Contains("\n\n\n", StringComparison.Ordinal))
        {
            clean = clean.Replace("\n\n\n", "\n\n", StringComparison.Ordinal);
        }

        return clean.Length <= max ? clean : throw new DomainException(ErrorCodes.ValidationFailed, $"{what} has at most {max} characters.");
    }
}
