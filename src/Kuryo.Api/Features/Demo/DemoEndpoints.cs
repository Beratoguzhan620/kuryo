using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Options;

namespace Kuryo.Api.Features.Demo;

public sealed record DemoAccount(string Role, string Email, string Password);

internal static class DemoEndpoints
{
    public static IEndpointRouteBuilder MapDemoEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/demo", Accounts)
            .WithTags("Auth")
            .WithSummary("The public demo's sign-ins")
            .WithDescription("An empty list unless this installation is the public demo.");
        return app;
    }

    // Empty rather than 404 elsewhere, so the sign-in page's question leaves no failed request in the browser console.
    private static Ok<List<DemoAccount>> Accounts(IOptions<DemoOptions> options) =>
        TypedResults.Ok(options.Value is { Enabled: true } demo
            ? new List<DemoAccount>
            {
                new("Dispatcher", demo.DispatcherEmail, demo.Password),
                new("Merchant", demo.MerchantEmail, demo.Password),
                new("Courier", demo.CourierEmail, demo.Password),
            }
            : []);
}
