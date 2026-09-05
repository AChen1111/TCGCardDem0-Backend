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
        var problemDetails = new ProblemDetails
        {
            Status = statusCode,
            Title = knownException is null ? "服务器暂时无法处理请求" : exception.Message,
            Detail = knownException is null ? "请稍后重试，如问题持续请联系管理员。" : null,
            Extensions =
            {
                ["code"] = code,
                ["traceId"] = httpContext.TraceIdentifier
            }
        };
        if (exception is IApiValidationException validationException)
        {
            problemDetails.Extensions["errors"] = validationException.Errors;
        }

        return await problemDetailsService.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            ProblemDetails = problemDetails,
            Exception = exception
        });
    }
}
