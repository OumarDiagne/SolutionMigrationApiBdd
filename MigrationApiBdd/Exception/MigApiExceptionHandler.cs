using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace MigrationApiBdd.Exception
{
    public class MigApiExceptionHandler : IExceptionHandler
    {
        private readonly ILogger<MigApiExceptionHandler> _logger;

        public MigApiExceptionHandler(ILogger<MigApiExceptionHandler> logger)
        {
            _logger = logger;
        }

        public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, System.Exception exception, CancellationToken cancellationToken)
        {
            if (exception is BusinessRuleException businessRuleException)
            {
                _logger.LogWarning(
                    "Règle métier refusée. Path: {Path}. TraceId: {TraceId}. " +
                    "StatusCode: {StatusCode}. Message: {Message}",
                     httpContext.Request.Path,
                     httpContext.TraceIdentifier,
                     businessRuleException.StatusCode,
                     businessRuleException.Message);

                var title = businessRuleException.StatusCode ==  StatusCodes.Status409Conflict ? "Conflit de concurrence" : "Règle métier non respectée";

                ProblemDetails problemDetails = new ProblemDetails
                {
                    Status = businessRuleException.StatusCode,
                    Title = title,
                    Detail = businessRuleException.Message,
                    Instance = httpContext.Request.Path
                };

                problemDetails.Extensions["traceId"] =
                httpContext.TraceIdentifier;

                httpContext.Response.StatusCode =
                    businessRuleException.StatusCode;

                httpContext.Response.ContentType =
                    "application/problem+json";
                await httpContext.Response.WriteAsJsonAsync(
               problemDetails,
               cancellationToken);

                return true;
            }
            _logger.LogError(
           exception,
           "Erreur inattendue. Path: {Path}. TraceId: {TraceId}",
           httpContext.Request.Path,
           httpContext.TraceIdentifier);

            ProblemDetails unexpectedProblemDetails = new ProblemDetails
            {
                Status = StatusCodes.Status500InternalServerError,
                Title = "Erreur interne du serveur",
                Detail = "Une erreur inattendue est survenue.",
                Instance = httpContext.Request.Path
            };

            unexpectedProblemDetails.Extensions["traceId"] =
                httpContext.TraceIdentifier;

            httpContext.Response.StatusCode =
                StatusCodes.Status500InternalServerError;
            httpContext.Response.ContentType = "application/problem+json";

            await httpContext.Response.WriteAsJsonAsync(
                unexpectedProblemDetails,
                cancellationToken);
            return true;
        }
    }
}
