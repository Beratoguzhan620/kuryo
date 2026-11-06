using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace Kuryo.Api.Common;

/// <summary>The landing text of the API reference.</summary>
internal sealed class DocumentInfoTransformer : IOpenApiDocumentTransformer
{
    public Task TransformAsync(OpenApiDocument document, OpenApiDocumentTransformerContext context, CancellationToken cancellationToken)
    {
        document.Info = new OpenApiInfo
        {
            Title = "Kuryo",
            Version = "1.0",
            Description = """
                Courier dispatch: merchants send orders, each is offered to the nearest free courier found with PostGIS,
                for a limited time, then to the next; couriers and customers follow it live over SignalR, and the
                courier finishes with a code only the customer has.

                **Getting started**

                1. Create an account at `POST /api/auth/register` and paste the access token into *Authentication*.
                2. Create a company at `POST /api/companies`, then a zone and a merchant.
                3. Add couriers to the team, send an order and watch it find one.

                Roles: couriers ride, merchants send and follow their own orders, dispatchers run everything, owners
                also manage the team.

                **Errors** follow RFC 9457 problem details with a stable `code`, for example `validation_failed`
                or `invalid_state`. Every response carries an `X-Correlation-Id`.
                """,
            License = new OpenApiLicense { Name = "MIT", Url = new Uri("https://github.com/Beratoguzhan620/kuryo/blob/main/LICENSE") },
        };
        return Task.CompletedTask;
    }
}
