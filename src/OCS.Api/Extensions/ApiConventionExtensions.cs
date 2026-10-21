using System.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using OCS.Api.Contracts.Common;

namespace OCS.Api.Extensions;

internal static class ApiConventionExtensions
{
    public static IServiceCollection AddApiConventions(this IServiceCollection services)
    {
        services.AddProblemDetails(options => options.CustomizeProblemDetails = context =>
        {
            var problem = context.ProblemDetails;
            problem.Extensions.TryAdd("code", GetErrorCode(problem.Status));
            problem.Extensions.TryAdd(
                "traceId",
                Activity.Current?.Id ?? context.HttpContext.TraceIdentifier);
        });

        services.Configure<ApiBehaviorOptions>(options =>
        {
            options.InvalidModelStateResponseFactory = context =>
            {
                var details = new ValidationProblemDetails(context.ModelState)
                {
                    Status = StatusCodes.Status400BadRequest,
                    Title = "One or more validation errors occurred.",
                    Type = "https://httpstatuses.com/400"
                };

                details.Extensions["code"] = ApiErrorCodes.ValidationFailed;
                details.Extensions["traceId"] = Activity.Current?.Id ?? context.HttpContext.TraceIdentifier;

                return new BadRequestObjectResult(details);
            };
        });

        return services;
    }

    private static string GetErrorCode(int? statusCode) => statusCode switch
    {
        StatusCodes.Status400BadRequest => ApiErrorCodes.BadRequest,
        StatusCodes.Status401Unauthorized => ApiErrorCodes.Unauthorized,
        StatusCodes.Status403Forbidden => ApiErrorCodes.Forbidden,
        StatusCodes.Status404NotFound => ApiErrorCodes.NotFound,
        StatusCodes.Status409Conflict => ApiErrorCodes.Conflict,
        _ => ApiErrorCodes.InternalError
    };
}
