namespace Kuryo.Api.Common;

internal static class Like
{
    /// <summary>Makes user input safe inside an ILIKE pattern that uses a backslash as its escape character.</summary>
    public static string Escape(string value) =>
        value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("%", "\\%", StringComparison.Ordinal).Replace("_", "\\_", StringComparison.Ordinal);
}
