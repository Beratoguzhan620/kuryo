namespace Kuryo.Api.Common;

public sealed record Page<T>(IReadOnlyList<T> Items, int Total, int PageNumber, int PageSize);

internal static class Paging
{
    public const int DefaultSize = 20;
    public const int MaxSize = 100;

    public static (int Page, int Size) Clamp(int? page, int? size) =>
        (Math.Max(1, page ?? 1), Math.Clamp(size ?? DefaultSize, 1, MaxSize));
}
