namespace Kuryo.Api.Common;

/// <summary>
/// The headers nginx adds in Compose, for when this app serves the web pages itself (the one-container demo). Text
/// from the API is rendered as text; the policy is a second line of defence.
/// </summary>
public sealed class WebHeadersMiddleware(RequestDelegate next)
{
    private const string Policy =
        "default-src 'self'; script-src 'self'; style-src 'self' 'unsafe-inline'; img-src 'self' data: https://tile.openstreetmap.org; font-src 'self'; " +
        "connect-src 'self'; worker-src 'self'; frame-ancestors 'none'; base-uri 'none'; form-action 'self'";

    public Task InvokeAsync(HttpContext context)
    {
        if (!IsApi(context.Request.Path))
        {
            var headers = context.Response.Headers;
            headers.ContentSecurityPolicy = Policy;
            headers["Permissions-Policy"] = "camera=(), microphone=(), geolocation=(self)";
            headers.XContentTypeOptions = "nosniff";
            headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
        }

        return next(context);
    }

    public static bool IsApi(PathString path) =>
        path.StartsWithSegments("/api") || path.StartsWithSegments("/hubs") || path.StartsWithSegments("/scalar") || path.StartsWithSegments("/openapi")
        || path.StartsWithSegments("/health");
}
