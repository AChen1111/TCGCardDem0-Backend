using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace AChen.Backend.Api.Infrastructure;

public sealed class ApiExceptionHandler(
    ILogger<ApiExceptionHandler> logger,
    IProblemDetailsService problemDetailsService) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        var knownException = exception as ApiException;
        var statusCode = knownException?.StatusCode ?? StatusCodes.Status500InternalServerError;
        var code = knownException?.Code ?? "INTERNAL_ERROR";

        if (knownException is null)
        {
            logger.LogError(exception, "Unhandled request failure. TraceId: {TraceId}", httpContext.TraceIdentifier);
        }

        httpContext.Response.StatusCode = statusCode;
        return await problemDetailsService.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            ProblemDetails = new ProblemDetails
            {
                Status = statusCode,
                Title = knownException is null ? "An unexpected error occurred." : exception.Message,
                Detail = knownException is null ? "The server could not complete the request." : null,
                Extensions =
                {
                    ["code"] = code,
                    ["traceId"] = httpContext.TraceIdentifier
                }
            },
            Exception = exception
        });
    }
}
