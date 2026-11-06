using Kuryo.Core;
using Kuryo.Infrastructure.Persistence;
using Microsoft.AspNetCore.Http.HttpResults;

namespace Kuryo.Api.Features.Live;

internal static class TrackEndpoints
{
    public const string RateLimit = "track";

    public static IEndpointRouteBuilder MapTrackEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/track/{token}", Get)
            .WithTags("Tracking")
            .AllowAnonymous()
            .RequireRateLimiting(RateLimit)
            .WithSummary("The customer's view of a delivery, by the token in their tracking link")
            .WithDescription("No sign-in. Shows status, timings, the courier's first name and position while on the way, and the code to give at the door; " +
                "never the customer's name, phone or address. The link ends 24 hours after delivery or cancellation. Live updates: connect to /hubs/track and call Follow(token).");
        return app;
    }

    private static async Task<Ok<TrackView>> Get(string token, KuryoDbContext db, TimeProvider clock, HttpContext context, CancellationToken ct)
    {
        var found = await TrackQueries.ByTokenAsync(db, token, clock.GetUtcNow(), ct)
            ?? throw new DomainException(ErrorCodes.NotFound, "This tracking link is not valid or has ended.");
        // The token is the only key: keep the page out of shared caches and the link out of referrers.
        context.Response.Headers.CacheControl = "no-store";
        context.Response.Headers["Referrer-Policy"] = "no-referrer";
        return TypedResults.Ok(found.View);
    }
}
