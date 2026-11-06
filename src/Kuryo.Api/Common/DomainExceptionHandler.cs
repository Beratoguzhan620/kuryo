using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Kuryo.Core;

namespace Kuryo.Api.Common;

/// <summary>
/// Turns business rule violations into RFC 9457 problem responses with a stable <c>code</c> field.
/// Lost updates (a concurrency token mismatch) become 409 conflicts. Anything else falls through to the default handler (500 without details).
/// </summary>
internal sealed class DomainExceptionHandler(IProblemDetailsService problemDetails) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        var domain = exception switch
        {
            DomainException d => d,
            // Two people saved the same record at once; the second one gets to look again instead of overwriting.
            DbUpdateConcurrencyException => new DomainException(ErrorCodes.Conflict, "Someone else changed this at the same time. Reload and try again."),
            _ => null,
        };
        if (domain is null)
        {
            return false;
        }

        var status = ErrorStatuses.StatusFor(domain.Code);
        httpContext.Response.StatusCode = status;

        return await problemDetails.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            Exception = exception,
            ProblemDetails = new ProblemDetails
            {
                Status = status,
                Title = ReasonPhrases.GetReasonPhrase(status),
                Detail = domain.Message,
                Extensions = { ["code"] = domain.Code },
            },
        });
    }
}
